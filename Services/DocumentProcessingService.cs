using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using LocalRagAssistant.Models;
using Microsoft.Extensions.Options;

namespace LocalRagAssistant.Services;

public interface IDocumentProcessingService
{
    Task<Models.Document> ProcessDocumentAsync(string filePath, string fileName, string contentType, CancellationToken cancellationToken = default);
    List<Chunk> ChunkText(string text, string documentId);
}

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly AppSettings _settings;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(IOptions<AppSettings> settings, ILogger<DocumentProcessingService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Models.Document> ProcessDocumentAsync(string filePath, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing document: {FileName}", fileName);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        string text;

        try
        {
            text = extension switch
            {
                ".pdf" => await ExtractTextFromPdfAsync(filePath, cancellationToken),
                ".docx" => await ExtractTextFromDocxAsync(filePath, cancellationToken),
                ".txt" => await File.ReadAllTextAsync(filePath, cancellationToken),
                ".md" => await ExtractTextFromMarkdownAsync(filePath, cancellationToken),
                _ => throw new NotSupportedException($"File type {extension} is not supported")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from {FileName}", fileName);
            throw;
        }

        var document = new Models.Document
        {
            FileName = fileName,
            FilePath = filePath,
            ContentType = contentType
        };

        document.Chunks = ChunkText(text, document.Id);
        _logger.LogInformation("Created {ChunkCount} chunks for document {FileName}", document.Chunks.Count, fileName);

        return document;
    }

    public List<Chunk> ChunkText(string text, string documentId)
    {
        var chunks = new List<Chunk>();
        var chunkSize = _settings.ChunkSize;
        var overlap = _settings.ChunkOverlap;

        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        var startIndex = 0;
        var chunkIndex = 0;

        while (startIndex < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - startIndex);
            var chunkText = text.Substring(startIndex, length);

            chunks.Add(new Chunk
            {
                DocumentId = documentId,
                Content = chunkText.Trim(),
                ChunkIndex = chunkIndex++
            });

            startIndex += chunkSize - overlap;
        }

        return chunks;
    }

    private async Task<string> ExtractTextFromPdfAsync(string filePath, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make it async-compatible
        
        using var pdfReader = new PdfReader(filePath);
        using var pdfDocument = new PdfDocument(pdfReader);
        
        var text = new System.Text.StringBuilder();
        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
        {
            var page = pdfDocument.GetPage(i);
            var strategy = new SimpleTextExtractionStrategy();
            var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
            text.AppendLine(pageText);
        }

        return text.ToString();
    }

    private async Task<string> ExtractTextFromDocxAsync(string filePath, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make it async-compatible
        
        using var wordDocument = WordprocessingDocument.Open(filePath, false);
        var body = wordDocument.MainDocumentPart?.Document?.Body;
        
        if (body == null)
        {
            return string.Empty;
        }

        return body.InnerText;
    }

    private async Task<string> ExtractTextFromMarkdownAsync(string filePath, CancellationToken cancellationToken)
    {
        var markdown = await File.ReadAllTextAsync(filePath, cancellationToken);
        var pipeline = new MarkdownPipelineBuilder().Build();
        var html = Markdown.ToHtml(markdown, pipeline);
        
        // For simplicity, we'll just use the markdown text directly
        // In a production system, you might want to strip HTML tags
        return markdown;
    }
}
