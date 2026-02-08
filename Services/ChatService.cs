using System.Text;
using System.Text.Json;
using LocalRagAssistant.Models;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace LocalRagAssistant.Services;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

public class ChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly ILogger<ChatService> _logger;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStoreService;
    private readonly IConversationService _conversationService;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public ChatService(
        HttpClient httpClient,
        IOptions<AppSettings> settings,
        ILogger<ChatService> logger,
        IEmbeddingService embeddingService,
        IVectorStoreService vectorStoreService,
        IConversationService conversationService)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _embeddingService = embeddingService;
        _vectorStoreService = vectorStoreService;
        _conversationService = conversationService;

        // Configure Polly retry policy
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}s due to: {Reason}",
                        retryCount, timespan.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing chat request: {Query}", request.Query);

        List<Citation> citations = new();
        string context = string.Empty;
        bool useRag = false;

        try
        {
            // Try to get query embedding and search documents
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query, cancellationToken);

            // Hybrid search: combine semantic similarity with keyword matching for better accuracy
            var relevantChunks = await _vectorStoreService.HybridSearchAsync(queryEmbedding, request.Query, _settings.TopK, cancellationToken);

            if (relevantChunks.Any())
            {
                useRag = true;
                
                // Get document information for citations
                var documents = await _vectorStoreService.GetAllDocumentsAsync(cancellationToken);
                citations = relevantChunks.Select(chunk =>
                {
                    var doc = documents.FirstOrDefault(d => d.Id == chunk.DocumentId);
                    return new Citation
                    {
                        FileName = doc?.FileName ?? "Unknown",
                        ChunkIndex = chunk.ChunkIndex,
                        Content = chunk.Content
                    };
                }).ToList();

                // Build context from relevant chunks
                context = string.Join("\n\n", relevantChunks.Select((c, i) => 
                    $"[Document: {citations[i].FileName}, Chunk #{c.ChunkIndex}]\n{c.Content}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve document context. Continuing with normal chat mode.");
            useRag = false;
        }

        // Get conversation history if conversationId is provided
        var conversationHistory = string.Empty;
        if (!string.IsNullOrEmpty(request.ConversationId))
        {
            var conversation = await _conversationService.GetConversationAsync(request.ConversationId, cancellationToken);
            if (conversation != null && conversation.Messages.Count > 0)
            {
                conversationHistory = string.Join("\n", conversation.Messages.TakeLast(5).Select(m => 
                    $"{m.Role}: {m.Content}"));
            }
        }

        // Build prompt
        var prompt = BuildPrompt(request.Query, context, conversationHistory, useRag);

        // Call LLM
        string answer;
        try
        {
            answer = await CallLlmAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call LLM");
            answer = "I'm sorry, but I'm unable to connect to the AI service. Please ensure Ollama is running with the required models (llama3.2 and nomic-embed-text).";
        }

        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();
        
        // Save conversation
        await _conversationService.SaveMessageAsync(conversationId, "user", request.Query, null, cancellationToken);
        await _conversationService.SaveMessageAsync(conversationId, "assistant", answer, citations, cancellationToken);

        return new ChatResponse
        {
            Answer = answer,
            Citations = citations,
            ConversationId = conversationId
        };
    }

    private string BuildPrompt(string query, string context, string conversationHistory, bool useRag)
    {
        var promptBuilder = new StringBuilder();
        
        if (useRag && !string.IsNullOrEmpty(context))
        {
            promptBuilder.AppendLine("You are a helpful assistant that answers questions based ONLY on the provided context.");
            promptBuilder.AppendLine("IMPORTANT: Answer the question directly using the information from the context below.");
            promptBuilder.AppendLine("If the answer is clearly stated in the context, provide it concisely without adding unnecessary commentary.");
            promptBuilder.AppendLine("When asked about costs, fees, payments, or financial information, carefully search through ALL provided chunks for relevant numbers and amounts.");
            promptBuilder.AppendLine("Look for terms like: Gebühr, Kosten, Preis, monatlich, jährlich, Pauschale, Tagessatz, Euro, payment, fee, cost.");
            promptBuilder.AppendLine("Only say you cannot answer if the context truly does not contain relevant information.");
            promptBuilder.AppendLine("When referencing information, you may mention the document name and chunk number.");
            promptBuilder.AppendLine();
        }
        else
        {
            promptBuilder.AppendLine("You are a helpful, friendly AI assistant. Answer questions naturally and conversationally.");
            promptBuilder.AppendLine();
        }

        if (!string.IsNullOrEmpty(conversationHistory))
        {
            promptBuilder.AppendLine("Previous conversation:");
            promptBuilder.AppendLine(conversationHistory);
            promptBuilder.AppendLine();
        }

        if (useRag && !string.IsNullOrEmpty(context))
        {
            promptBuilder.AppendLine("Context:");
            promptBuilder.AppendLine(context);
            promptBuilder.AppendLine();
        }
        
        promptBuilder.AppendLine($"Question: {query}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Answer:");

        return promptBuilder.ToString();
    }

    private async Task<string> CallLlmAsync(string prompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _settings.ChatModel,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            stream = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            return await _httpClient.PostAsync(_settings.LlmChatUrl, content, cancellationToken);
        });

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<LlmChatResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        return chatResponse?.Message?.Content ?? "I couldn't generate a response.";
    }

    private class LlmChatResponse
    {
        public MessageContent? Message { get; set; }
    }

    private class MessageContent
    {
        public string Content { get; set; } = string.Empty;
    }
}
