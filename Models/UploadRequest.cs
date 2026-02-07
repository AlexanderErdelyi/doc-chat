namespace LocalRagAssistant.Models;

public class UploadRequest
{
    public IFormFile File { get; set; } = null!;
}

public class UploadResponse
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int ChunksCreated { get; set; }
    public string Message { get; set; } = string.Empty;
}
