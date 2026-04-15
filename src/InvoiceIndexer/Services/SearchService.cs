using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Storage.Blobs;
using InvoiceIndexer.Configuration;
using InvoiceIndexer.Models;

namespace InvoiceIndexer.Services;

public class SearchService : ISearchService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly IndexerConfig _config;
    private readonly TokenCredential _credential;
    private const string IndexName = "invoices";

    public SearchService(IndexerConfig config, TokenCredential credential)
    {
        _config       = config;
        _credential   = credential;
        _indexClient  = new SearchIndexClient(new Uri(config.SearchEndpoint), credential);
        _searchClient = new SearchClient(new Uri(config.SearchEndpoint), IndexName, credential);
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
                new SimpleField("id", SearchFieldDataType.String)           { IsKey = true, IsFilterable = true, IsRetrievable = true },
                new SearchableField("vendor")                               { IsFilterable = true, IsFacetable = true, IsRetrievable = true },
                new SimpleField("amount",   SearchFieldDataType.Double)     { IsFilterable = true, IsSortable = true, IsRetrievable = true },
                new SimpleField("discount", SearchFieldDataType.Double)     { IsFilterable = true, IsSortable = true, IsRetrievable = true },
                new SearchableField("category")                             { IsFilterable = true, IsFacetable = true, IsRetrievable = true },
                new SimpleField("date", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true, IsRetrievable = true },
                new SearchableField("payment_terms")                        { IsFilterable = true, IsRetrievable = true },
                new SimpleField("source_file", SearchFieldDataType.String)  { IsFilterable = true, IsRetrievable = true },
                new SearchableField("content")                              { IsRetrievable = true, AnalyzerName = "en.microsoft" },
                new VectorSearchField("content_vector", 1536, "vector-profile") { IsStored = false }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index);
    }

    public async Task EmbedDocumentsAsync()
    {
        throw new NotImplementedException();
    }

    public async Task UploadChunksAsync()
    {
        throw new NotImplementedException();
    }
}
