using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using InvoiceIndexer.Configuration;
using InvoiceIndexer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(log =>
    {
        log.ClearProviders();
        log.AddConsole();
        log.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = new IndexerConfig
        {
            SearchEndpoint               = ctx.Configuration["SEARCH_ENDPOINT"]!,
            OpenAiEndpoint               = ctx.Configuration["OPENAI_ENDPOINT"]!,
            OpenAiEmbeddingDeployment    = ctx.Configuration["OPENAI_EMBEDDING_DEPLOYMENT"]!,
            OpenAiGptDeployment          = ctx.Configuration["OPENAI_GPT_DEPLOYMENT"]!,
            StorageAccountUrl            = ctx.Configuration["STORAGE_ACCOUNT_URL"]!,
            StorageContainer             = ctx.Configuration["STORAGE_CONTAINER"]!,
            SearchIndexName              = ctx.Configuration["SEARCH_INDEX_NAME"]!,
            KnowledgeSourceName          = ctx.Configuration["KNOWLEDGE_SOURCE_NAME"]!,
            KnowledgeBaseName            = ctx.Configuration["KNOWLEDGE_BASE_NAME"]!,
            DocumentIntelligenceEndpoint = ctx.Configuration["DOCUMENT_INTELLIGENCE_ENDPOINT"]!,
        };

        var credential = new DefaultAzureCredential();

        services.AddSingleton(config);
        services.AddSingleton(credential);

        // Azure clients
        services.AddSingleton(_ =>
            new BlobServiceClient(new Uri(config.StorageAccountUrl), credential));

        services.AddSingleton(_ =>
            new DocumentIntelligenceClient(
                new Uri(config.DocumentIntelligenceEndpoint), credential));

        services.AddSingleton(_ =>
            new AzureOpenAIClient(new Uri(config.OpenAiEndpoint), credential));

        // Services
    })
    .Build();

await host.RunAsync();
