using System.Text.Json;
using LocalRagAssistant.Models;
using Microsoft.Extensions.Options;

namespace LocalRagAssistant.Services;

public interface IConversationService
{
    Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    Task SaveMessageAsync(string conversationId, string role, string content, List<Citation>? citations, CancellationToken cancellationToken = default);
    Task<List<Conversation>> GetAllConversationsAsync(CancellationToken cancellationToken = default);
}

public class ConversationService : IConversationService
{
    private readonly AppSettings _settings;
    private readonly ILogger<ConversationService> _logger;
    private readonly string _conversationsFilePath;
    private Dictionary<string, Conversation> _conversations = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ConversationService(IOptions<AppSettings> settings, ILogger<ConversationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        
        if (!Directory.Exists(_settings.DataDirectory))
        {
            Directory.CreateDirectory(_settings.DataDirectory);
        }

        _conversationsFilePath = Path.Combine(_settings.DataDirectory, "conversations.json");
        LoadConversations();
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _conversations.TryGetValue(conversationId, out var conversation) ? conversation : null;
    }

    public async Task SaveMessageAsync(string conversationId, string role, string content, List<Citation>? citations, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!_conversations.TryGetValue(conversationId, out var conversation))
            {
                conversation = new Conversation { Id = conversationId };
                _conversations[conversationId] = conversation;
            }

            conversation.Messages.Add(new ConversationMessage
            {
                Role = role,
                Content = content,
                Citations = citations
            });

            await SaveConversationsAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<Conversation>> GetAllConversationsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _conversations.Values.OrderByDescending(c => c.CreatedAt).ToList();
    }

    private void LoadConversations()
    {
        if (File.Exists(_conversationsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_conversationsFilePath);
                var conversations = JsonSerializer.Deserialize<List<Conversation>>(json) ?? new List<Conversation>();
                _conversations = conversations.ToDictionary(c => c.Id, c => c);
                _logger.LogInformation("Loaded {Count} conversations from storage", _conversations.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading conversations from storage");
                _conversations = new Dictionary<string, Conversation>();
            }
        }
    }

    private async Task SaveConversationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(_conversations.Values.ToList(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_conversationsFilePath, json, cancellationToken);
            _logger.LogDebug("Saved {Count} conversations to storage", _conversations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving conversations to storage");
            throw;
        }
    }
}
