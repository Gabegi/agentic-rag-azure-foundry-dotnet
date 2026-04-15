using Azure.Core;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using InvoiceIndexer.Configuration;

namespace InvoiceIndexer.Services;

public class SearchService : ISearchService
{
    private readonly SearchIndexClient _indexClient;
    private readonly IndexerConfig _config;
    private const string IndexName = "invoices";

    public SearchService(IndexerConfig config, TokenCredential credential)
    {
        _config      = config;
        _indexClient = new SearchIndexClient(new Uri(config.SearchEndpoint), credential);
    }

    public async Task EnsureIndexAsync()
    {
        var vectorSearch = new VectorSearch();
        vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration("hnsw-config"));
        vectorSearch.Profiles.Add(new VectorSearchProfile("vector-profile", "hnsw-config")
        {
            VectorizerName = "openai-vectorizer"
        });
        vectorSearch.Vectorizers.Add(new AzureOpenAIVectorizer("openai-vectorizer")
        {
            Parameters = new AzureOpenAIVectorizerParameters
            {
                ResourceUri    = new Uri(_config.OpenAiEndpoint),
                DeploymentName = "text-embedding-3-large",
                ModelName      = "text-embedding-3-large"
            }
        });

        var semanticConfig = new SemanticConfiguration("semantic-config", new SemanticPrioritizedFields
        {
            ContentFields  = { new SemanticField("content") },
            KeywordsFields = { new SemanticField("vendor"), new SemanticField("category") }
        });

        var semanticSearch = new SemanticSearch();
        semanticSearch.Configurations.Add(semanticConfig);
        semanticSearch.DefaultConfigurationName = "semantic-config";

        var index = new SearchIndex(IndexName)
        {
            Description    = "Invoice index containing vendor, amount, discount, category, date and payment terms from PDF invoices.",
            VectorSearch   = vectorSearch,
            SemanticSearch = semanticSearch,
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String)           { IsKey = true, IsFilterable = true },
                new SearchableField("vendor")                               { IsFilterable = true, IsFacetable = true },
                new SimpleField("amount",   SearchFieldDataType.Double)     { IsFilterable = true, IsSortable = true },
                new SimpleField("discount", SearchFieldDataType.Double)     { IsFilterable = true, IsSortable = true },
                new SearchableField("category")                             { IsFilterable = true, IsFacetable = true },
                new SimpleField("date", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                new SearchableField("payment_terms")                        { IsFilterable = true },
                new SimpleField("source_file", SearchFieldDataType.String)  { IsFilterable = true },
                new SearchableField("content")                              { AnalyzerName = "en.microsoft" },
                new VectorSearchField("content_vector", 1536, "vector-profile") { IsHidden = true, IsStored = false }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index);
    }
}
