namespace LocalRagAssistant.Models;

public class AppSettings
{
    public string LlmChatUrl { get; set; } = "http://localhost:11434/api/chat";
    public string LlmEmbeddingUrl { get; set; } = "http://localhost:11434/api/embed";
    public string ChatModel { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public int TopK { get; set; } = 5;
    public string DataDirectory { get; set; } = "data";
    public string UploadDirectory { get; set; } = "uploads";
}
