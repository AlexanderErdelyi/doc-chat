using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using LocalRagAssistant.Models;
using Polly;
using Polly.Retry;

namespace LocalRagAssistant.Services;

public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<List<float[]>> GetEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default);
}

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public EmbeddingService(
        HttpClient httpClient,
        IOptions<AppSettings> settings,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

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

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting embedding for text of length {Length}", text.Length);

        var request = new
        {
            model = _settings.EmbeddingModel,
            input = text
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            return await _httpClient.PostAsync(_settings.LlmEmbeddingUrl, content, cancellationToken);
        });

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseContent);

        if (embeddingResponse?.Embeddings == null || embeddingResponse.Embeddings.Length == 0)
        {
            throw new InvalidOperationException("Failed to get embedding from LLM");
        }

        return embeddingResponse.Embeddings;
    }

    public async Task<List<float[]>> GetEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = new List<float[]>();

        foreach (var text in texts)
        {
            var embedding = await GetEmbeddingAsync(text, cancellationToken);
            embeddings.Add(embedding);
        }

        return embeddings;
    }

    private class EmbeddingResponse
    {
        public float[] Embeddings { get; set; } = Array.Empty<float>();
    }
}
