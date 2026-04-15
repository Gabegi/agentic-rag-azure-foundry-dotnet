using Azure.Core;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.KnowledgeBases;
using Azure.Search.Documents.KnowledgeBases.Models;
using InvoiceIndexer.Configuration;
using Microsoft.Extensions.Logging;

namespace InvoiceIndexer.Services;

public class KnowledgeService : IKnowledgeService
{
    private readonly SearchIndexClient _indexClient;
    private readonly IndexerConfig _config;
    private readonly ILogger<KnowledgeService> _logger;

    private const string IndexName           = "invoices";
    private const string KnowledgeSourceName = "invoices-knowledge-source";
    private const string KnowledgeBaseName   = "invoices-knowledge-base";

    public KnowledgeService(
        IndexerConfig config,
        TokenCredential credential,
        ILogger<KnowledgeService> logger)
    {
        _indexClient = new SearchIndexClient(new Uri(config.SearchEndpoint), credential);
        _config      = config;
        _logger      = logger;
    }

    public async Task EnsureKnowledgeSourceAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Creating knowledge source '{Name}'", KnowledgeSourceName);

        var knowledgeSource = new SearchIndexKnowledgeSource(
            name: KnowledgeSourceName,
            searchIndexParameters: new SearchIndexKnowledgeSourceParameters(IndexName)
            {
                SourceDataFields =
                {
                    new SearchIndexFieldReference("id"),
                    new SearchIndexFieldReference("source_file"),
                    new SearchIndexFieldReference("content")
                }
            }
        )
        {
            Description = "Knowledge source for invoice index"
        };

        await _indexClient.CreateOrUpdateKnowledgeSourceAsync(knowledgeSource);

        _logger.LogInformation("Knowledge source '{Name}' created or updated", KnowledgeSourceName);
    }

    public async Task EnsureKnowledgeBaseAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Creating knowledge base '{Name}'", KnowledgeBaseName);

        var aoaiParams = new AzureOpenAIVectorizerParameters
        {
            ResourceUri    = new Uri(_config.OpenAiEndpoint),
            DeploymentName = _config.OpenAiGptDeployment,
            ModelName      = _config.OpenAiGptDeployment
        };

        var knowledgeBase = new KnowledgeBase(
            name: KnowledgeBaseName,
            knowledgeSources: new[] { new KnowledgeSourceReference(KnowledgeSourceName) }
        )
        {
            Description              = "Knowledge base for invoice retrieval",
            OutputMode               = KnowledgeRetrievalOutputMode.ExtractiveData,
            RetrievalReasoningEffort = new KnowledgeRetrievalLowReasoningEffort(),
            Models                   = { new KnowledgeBaseAzureOpenAIModel(aoaiParams) }
        };

        await _indexClient.CreateOrUpdateKnowledgeBaseAsync(knowledgeBase);

        _logger.LogInformation("Knowledge base '{Name}' created or updated", KnowledgeBaseName);
    }
}
