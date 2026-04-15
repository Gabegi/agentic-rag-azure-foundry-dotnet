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

    public async Task<IEnumerable<InvoiceDocument>> EmbedDocumentsAsync()
    {
        var containerClient = new BlobContainerClient(
            new Uri(_config.StorageAccountUrl),
            _config.StorageContainer,
            _credential);

        var diClient = new DocumentIntelligenceClient(
            new Uri(_config.DocumentIntelligenceEndpoint), _credential);

        var embeddingClient = new AzureOpenAIClient(
            new Uri(_config.OpenAiEndpoint), _credential)
            .GetEmbeddingClient("text-embedding-3-large");

        var documents = new List<InvoiceDocument>();

        await foreach (var blob in containerClient.GetBlobsAsync())
        {
            var blobClient = containerClient.GetBlobClient(blob.Name);
            var download   = await blobClient.DownloadContentAsync();

            var operation = await diClient.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-invoice",
                new AnalyzeDocumentContent
                {
                    Base64Source = BinaryData.FromBytes(download.Value.Content.ToArray())
                }
            );

            var invoice = operation.Value.Documents?.FirstOrDefault();
            if (invoice == null) continue;

            var vendor       = invoice.Fields.TryGetValue("VendorName",    out var v)  ? v.Content     : null;
            var amount       = invoice.Fields.TryGetValue("InvoiceTotal",  out var a)  ? a.ValueNumber : null;
            var discount     = invoice.Fields.TryGetValue("TotalDiscount", out var d)  ? d.ValueNumber : null;
            var category     = invoice.Fields.TryGetValue("PurchaseOrder", out var c)  ? c.Content     : null;
            var date         = invoice.Fields.TryGetValue("InvoiceDate",   out var dt) ? dt.ValueDate  : null;
            var paymentTerms = invoice.Fields.TryGetValue("PaymentTerm",   out var p)  ? p.Content     : null;

            var content = $"Invoice from {vendor} dated {date:yyyy-MM-dd}. " +
                          $"Category: {category}. Amount: ${amount}. "       +
                          $"Discount: {discount}%. Payment terms: {paymentTerms}.";

            var embeddingResult = await embeddingClient.GenerateEmbeddingAsync(content);
            var vector          = embeddingResult.Value.Vector.ToArray();

            documents.Add(new InvoiceDocument
            {
                Id            = blob.Name.Replace(".pdf", "").Replace("/", "-"),
                Vendor        = vendor,
                Amount        = amount,
                Discount      = discount,
                Category      = category,
                Date          = date,
                PaymentTerms  = paymentTerms,
                SourceFile    = blob.Name,
                Content       = content,
                ContentVector = vector
            });
        }

        return documents;
    }

    public async Task UploadChunksAsync()
    {
        throw new NotImplementedException();
    }
}
