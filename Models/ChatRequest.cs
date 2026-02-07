namespace LocalRagAssistant.Models;

public class ChatRequest
{
    public string Query { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
}

public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new();
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
}

public class Citation
{
    public string FileName { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ConversationMessage> Messages { get; set; } = new();
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<Citation>? Citations { get; set; }
}
