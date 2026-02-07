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

        // Get query embedding
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query, cancellationToken);

        // Search for relevant chunks
        var relevantChunks = await _vectorStoreService.SearchAsync(queryEmbedding, _settings.TopK, cancellationToken);

        // Get document information for citations
        var documents = await _vectorStoreService.GetAllDocumentsAsync(cancellationToken);
        var citations = relevantChunks.Select(chunk =>
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
        var context = string.Join("\n\n", relevantChunks.Select((c, i) => 
            $"[Document: {citations[i].FileName}, Chunk #{c.ChunkIndex}]\n{c.Content}"));

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
        var prompt = BuildPrompt(request.Query, context, conversationHistory);

        // Call LLM
        var answer = await CallLlmAsync(prompt, cancellationToken);

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

    private string BuildPrompt(string query, string context, string conversationHistory)
    {
        var promptBuilder = new StringBuilder();
        
        promptBuilder.AppendLine("You are a helpful assistant that answers questions based on the provided context.");
        promptBuilder.AppendLine("Use the context below to answer the question. If you can't answer based on the context, say so.");
        promptBuilder.AppendLine("When referencing information, mention the document name and chunk number.");
        promptBuilder.AppendLine();

        if (!string.IsNullOrEmpty(conversationHistory))
        {
            promptBuilder.AppendLine("Previous conversation:");
            promptBuilder.AppendLine(conversationHistory);
            promptBuilder.AppendLine();
        }

        promptBuilder.AppendLine("Context:");
        promptBuilder.AppendLine(context);
        promptBuilder.AppendLine();
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
        var chatResponse = JsonSerializer.Deserialize<LlmChatResponse>(responseContent);

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
