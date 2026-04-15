using Microsoft.Extensions.Logging;

namespace InvoiceIndexer.Services;

public class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IIndexService _indexService;
    private readonly IDocumentService _documentService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IIndexService indexService,
        IDocumentService documentService,
        IEmbeddingService embeddingService,
        IKnowledgeService knowledgeService,
        ILogger<PipelineOrchestrator> logger)
    {
        _indexService     = indexService;
        _documentService  = documentService;
        _embeddingService = embeddingService;
        _knowledgeService = knowledgeService;
        _logger           = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Pipeline started");

        await _indexService.EnsureIndexAsync();

        var blobs     = await _documentService.ReadBlobsAsync(ct);
        var extracted = await _documentService.ExtractDocumentsAsync(blobs, ct);
        var embedded  = await _embeddingService.EmbedDocumentsAsync(extracted, ct);
        await _embeddingService.UploadDocumentsAsync(embedded, ct);

        await _knowledgeService.EnsureKnowledgeSourceAsync(ct);
        await _knowledgeService.EnsureKnowledgeBaseAsync(ct);

        _logger.LogInformation("Pipeline complete");
    }
}
