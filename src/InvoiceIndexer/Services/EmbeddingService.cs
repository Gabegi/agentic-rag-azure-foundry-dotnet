using Azure.AI.OpenAI;
using InvoiceIndexer.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace InvoiceIndexer.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(AzureOpenAIClient client, IndexerConfig config, ILogger<EmbeddingService> logger)
    {
        _client = client.GetEmbeddingClient(config.OpenAiEmbeddingDeployment);
        _logger = logger;
    }

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating embedding ({Chars} chars)", text.Length);

        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats();
    }
}
