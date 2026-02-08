using System.Text.Json;
using System.Text.RegularExpressions;
using LocalRagAssistant.Models;
using Microsoft.Extensions.Options;

namespace LocalRagAssistant.Services;

public interface IVectorStoreService
{
    Task AddDocumentAsync(Document document, CancellationToken cancellationToken = default);
    Task UpsertChunksAsync(List<Chunk> chunks, CancellationToken cancellationToken = default);
    Task<List<Chunk>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken = default);
    Task<List<Chunk>> HybridSearchAsync(float[] queryEmbedding, string queryText, int topK, CancellationToken cancellationToken = default);
    Task RebuildIndexAsync(CancellationToken cancellationToken = default);
    Task<List<Document>> GetAllDocumentsAsync(CancellationToken cancellationToken = default);
}

public class VectorStoreService : IVectorStoreService
{
    private readonly AppSettings _settings;
    private readonly ILogger<VectorStoreService> _logger;
    private readonly string _dataFilePath;
    private List<Document> _documents = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public VectorStoreService(IOptions<AppSettings> settings, ILogger<VectorStoreService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        
        // Ensure data directory exists
        if (!Directory.Exists(_settings.DataDirectory))
        {
            Directory.CreateDirectory(_settings.DataDirectory);
        }

        _dataFilePath = Path.Combine(_settings.DataDirectory, "documents.json");
        
        // Load existing documents
        LoadDocuments();
    }

    public async Task UpsertChunksAsync(List<Chunk> chunks, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Upserting {Count} chunks", chunks.Count);

            // Find or create document
            var documentId = chunks.FirstOrDefault()?.DocumentId;
            if (string.IsNullOrEmpty(documentId))
            {
                throw new ArgumentException("Chunks must have a DocumentId");
            }

            var document = _documents.FirstOrDefault(d => d.Id == documentId);
            if (document == null)
            {
                throw new InvalidOperationException($"Document {documentId} not found");
            }

            // Update chunks
            document.Chunks = chunks;

            await SaveDocumentsAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<Chunk>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // Make it async-compatible
        
        _logger.LogDebug("Searching for top {TopK} chunks", topK);

        var allChunks = _documents.SelectMany(d => d.Chunks).ToList();
        
        if (allChunks.Count == 0)
        {
            _logger.LogWarning("No chunks available for search");
            return new List<Chunk>();
        }

        // Calculate cosine similarity for each chunk
        var results = allChunks
            .Where(c => c.Embedding != null && c.Embedding.Length > 0)
            .Select(chunk => new
            {
                Chunk = chunk,
                Similarity = CosineSimilarity(queryEmbedding, chunk.Embedding)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        _logger.LogInformation("Found {Count} relevant chunks", results.Count);
        return results;
    }

    public async Task<List<Chunk>> HybridSearchAsync(float[] queryEmbedding, string queryText, int topK, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        
        var allChunks = _documents.SelectMany(d => d.Chunks).ToList();
        if (allChunks.Count == 0)
        {
            _logger.LogWarning("No chunks available for hybrid search");
            return new List<Chunk>();
        }

        // Extract keywords from query
        var queryKeywords = ExtractKeywords(queryText);
        
        // Calculate scores for each chunk
        var results = allChunks
            .Where(c => c.Embedding != null && c.Embedding.Length > 0)
            .Select(chunk =>
            {
                // Semantic similarity score (0-1)
                var semanticScore = CosineSimilarity(queryEmbedding, chunk.Embedding);
                
                // Keyword match score (0-1)
                var keywordScore = CalculateKeywordScore(chunk.Content, queryKeywords);
                
                // Hybrid score: 70% semantic + 30% keyword
                // Adjust weights based on query characteristics
                var semanticWeight = queryKeywords.Any(k => k.All(char.IsDigit)) ? 0.5 : 0.7; // More weight to keywords if numbers present
                var keywordWeight = 1.0 - semanticWeight;
                var hybridScore = (semanticScore * semanticWeight) + (keywordScore * keywordWeight);
                
                return new
                {
                    Chunk = chunk,
                    Score = hybridScore,
                    SemanticScore = semanticScore,
                    KeywordScore = keywordScore
                };
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        _logger.LogInformation("Hybrid search found {Count} relevant chunks", results.Count);
        return results;
    }

    private List<string> ExtractKeywords(string text)
    {
        // Convert to lowercase and extract meaningful words
        var words = Regex.Matches(text.ToLowerInvariant(), @"\b[\wäöüßÄÖÜ]+\b")
            .Select(m => m.Value)
            .Where(w => w.Length > 2) // Filter out very short words
            .ToList();
        
        // Also extract numbers and currency amounts
        var numbers = Regex.Matches(text, @"\d+[.,]?\d*")
            .Select(m => m.Value)
            .ToList();
        
        return words.Concat(numbers).Distinct().ToList();
    }

    private double CalculateKeywordScore(string content, List<string> keywords)
    {
        if (keywords.Count == 0)
            return 0;

        var contentLower = content.ToLowerInvariant();
        var matchCount = 0;
        var totalWeight = 0.0;
        
        foreach (var keyword in keywords)
        {
            var keywordLower = keyword.ToLowerInvariant();
            
            // Count occurrences
            var count = Regex.Matches(contentLower, Regex.Escape(keywordLower)).Count;
            
            if (count > 0)
            {
                matchCount++;
                
                // Weight numbers and longer keywords more heavily
                var weight = keyword.All(char.IsDigit) ? 2.0 : (keyword.Length > 5 ? 1.5 : 1.0);
                totalWeight += Math.Log(1 + count) * weight; // Logarithmic scaling for term frequency
            }
        }
        
        // Score based on percentage of keywords matched and their weights
        var coverage = (double)matchCount / keywords.Count;
        var avgWeight = matchCount > 0 ? totalWeight / matchCount : 0;
        
        // Normalize score to 0-1 range
        return Math.Min(1.0, (coverage * 0.6) + (Math.Min(1.0, avgWeight / 3.0) * 0.4));
    }

    public async Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Rebuilding index");
            
            // In a real implementation, this might rebuild inverted indices, etc.
            // For now, we just reload from disk
            LoadDocuments();
            
            _logger.LogInformation("Index rebuilt with {Count} documents", _documents.Count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<Document>> GetAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _documents.ToList();
    }

    public async Task AddDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _documents.Add(document);
            await SaveDocumentsAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void LoadDocuments()
    {
        if (File.Exists(_dataFilePath))
        {
            try
            {
                var json = File.ReadAllText(_dataFilePath);
                _documents = JsonSerializer.Deserialize<List<Document>>(json) ?? new List<Document>();
                _logger.LogInformation("Loaded {Count} documents from storage", _documents.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading documents from storage");
                _documents = new List<Document>();
            }
        }
        else
        {
            _logger.LogInformation("No existing documents found");
            _documents = new List<Document>();
        }
    }

    private async Task SaveDocumentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(_documents, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_dataFilePath, json, cancellationToken);
            _logger.LogInformation("Saved {Count} documents to storage", _documents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving documents to storage");
            throw;
        }
    }

    private float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same length");
        }

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0;
        }

        return dotProduct / (float)(Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
