using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Search.Documents;
using InvoiceIndexer.Configuration;
using InvoiceIndexer.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace InvoiceIndexer.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly SearchClient _searchClient;
    private readonly ILogger<EmbeddingService> _logger;
    private const string IndexName = "invoices";

    public EmbeddingService(
        IndexerConfig config,
        TokenCredential credential,
        ILogger<EmbeddingService> logger)
    {
        _embeddingClient = new AzureOpenAIClient(new Uri(config.OpenAiEndpoint), credential)
            .GetEmbeddingClient("text-embedding-3-large");

        _searchClient = new SearchClient(
            new Uri(config.SearchEndpoint),
            IndexName,
            credential);

        _logger = logger;
    }

    public async Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync(
        IEnumerable<InvoiceDocument> documents,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Embedding {Count} documents", documents.Count());

        var embedded = new List<InvoiceDocument>();

        foreach (var document in documents)
        {
            _logger.LogInformation("Embedding document {Id}", document.Id);

            var result = await _embeddingClient.GenerateEmbeddingAsync(
                document.Content, cancellationToken: ct);

            document.ContentVector = result.Value.ToFloats().ToArray();
            embedded.Add(document);

            _logger.LogInformation("Embedded document {Id}", document.Id);
        }

        _logger.LogInformation("Embedded {Count} documents", embedded.Count);
        return embedded;
    }

    public async Task UploadDocumentsAsync(
        IEnumerable<InvoiceDocument> documents,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Uploading {Count} documents to index", documents.Count());

        var response = await _searchClient.UploadDocumentsAsync(documents, cancellationToken: ct);

        foreach (var result in response.Value.Results)
        {
            if (!result.Succeeded)
                _logger.LogWarning("Failed to upload document {Key}: {Error}", result.Key, result.ErrorMessage);
            else
                _logger.LogInformation("Uploaded document {Key}", result.Key);
        }

        _logger.LogInformation("Upload complete");
    }
}
