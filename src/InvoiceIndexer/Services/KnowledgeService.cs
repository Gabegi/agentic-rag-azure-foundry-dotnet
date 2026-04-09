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

    public KnowledgeService(
        SearchIndexClient indexClient,
        IndexerConfig config,
        ILogger<KnowledgeService> logger)
    {
        _indexClient = indexClient;
        _config      = config;
        _logger      = logger;
    }

    public async Task EnsureKnowledgeSourceAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Ensuring knowledge source '{Name}'...", _config.KnowledgeSourceName);

        var ks = new SearchIndexKnowledgeSource(
            name: _config.KnowledgeSourceName,
            searchIndexParameters: new SearchIndexKnowledgeSourceParameters(_config.SearchIndexName)
            {
                SourceDataFields =
                {
                    new SearchIndexFieldReference("id"),
                    new SearchIndexFieldReference("page_chunk"),
                    new SearchIndexFieldReference("page_number"),
                    new SearchIndexFieldReference("source_file")
                }
            }
        );

        await _indexClient.CreateOrUpdateKnowledgeSourceAsync(ks, cancellationToken: ct);
        _logger.LogInformation("Knowledge source '{Name}' ready.", _config.KnowledgeSourceName);
    }

    public async Task EnsureKnowledgeBaseAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Ensuring knowledge base '{Name}'...", _config.KnowledgeBaseName);

        var kb = new KnowledgeBase(
            name: _config.KnowledgeBaseName,
            knowledgeSources: [new KnowledgeSourceReference(_config.KnowledgeSourceName)]
        )
        {
            RetrievalReasoningEffort = new KnowledgeRetrievalLowReasoningEffort(),
            OutputMode               = KnowledgeRetrievalOutputMode.AnswerSynthesis,
            AnswerInstructions       = "Answer questions about invoices concisely based only on the retrieved documents. Always cite the source invoice file.",
            Models =
            {
                new KnowledgeBaseAzureOpenAIModel(
                    new AzureOpenAIVectorizerParameters
                    {
                        ResourceUri    = new Uri(_config.OpenAiEndpoint),
                        DeploymentName = _config.OpenAiGptDeployment,
                        ModelName      = "gpt-4.1-mini"
                    }
                )
            }
        };

        await _indexClient.CreateOrUpdateKnowledgeBaseAsync(kb, cancellationToken: ct);
        _logger.LogInformation("Knowledge base '{Name}' ready.", _config.KnowledgeBaseName);
    }
}
