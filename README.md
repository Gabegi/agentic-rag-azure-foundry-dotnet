# Azure AI Foundry — Invoice Indexer (Agentic Retrieval)

A .NET batch pipeline that ingests PDF invoices from Azure Blob Storage, extracts structured data via Document Intelligence, generates vector embeddings, and indexes everything into Azure AI Search — ready to be queried via an Azure AI Foundry knowledge base.

## Architecture

```
Azure Blob Storage (PDFs)
        │
        ▼
Document Intelligence      ← extracts vendor, amount, date, category...
        │
        ▼
Azure OpenAI Embeddings    ← text-embedding-3-large
        │
        ▼
Azure AI Search Index      ← vector + semantic search
        │
        ▼
AI Foundry Knowledge Base  ← query interface for the support agent
```

## Pipeline Steps

1. **EnsureIndex** — creates or updates the AI Search index with vector and semantic config
2. **ReadBlobs** — lists all PDFs from the blob container
3. **ExtractDocuments** — runs each PDF through Document Intelligence (`prebuilt-invoice`)
4. **EmbedDocuments** — generates 3072-dimension vectors via `text-embedding-3-large`
5. **UploadDocuments** — uploads documents + vectors to AI Search
6. **EnsureKnowledgeSource** — creates an AI Search knowledge source in AI Foundry
7. **EnsureKnowledgeBase** — creates a knowledge base backed by the index

## Azure Services

| Service | Purpose |
|---|---|
| Azure Blob Storage | Stores source PDF invoices |
| Azure Document Intelligence | Extracts structured fields from PDFs |
| Azure OpenAI | Generates embeddings + powers the knowledge base |
| Azure AI Search | Hosts the vector + semantic index |
| Azure Container Instance | Runs the indexer as a batch job |
| Azure Container Registry | Stores the Docker image |

## Getting Started

### Prerequisites

- Azure CLI (`az login`)
- Terraform >= 1.0
- .NET 8 SDK
- Docker

### 1. Deploy Infrastructure

Set your subscription ID in `infra/terraform.tfvars`:
```
subscription_id = "your-subscription-id"
```

Then run the **1 - Deploy Infrastructure** GitHub Actions pipeline, or locally:
```bash
cd infra
terraform init
terraform apply -var="subscription_id=<your-sub-id>"
```

### 2. Upload Invoices

Place PDFs in the `invoices/` folder and run **2 - Upload Invoices**, or manually upload to the `documents` blob container.

### 3. Run the Indexer

Run **3 - Deploy Invoice Indexer** — this builds the Docker image, pushes to ACR, and starts the ACI. The full pipeline runs automatically.

To re-index, restart the container:
```bash
az container start --resource-group rg-support-agent-dev --name aci-invoice-indexer-dev
```

### 4. Query the Knowledge Base

Open **Azure AI Foundry → Knowledge bases → invoices-knowledge-base → Test** to query indexed invoices directly.

### Local Development

```bash
# Set environment variables (copy and fill in values)
cp infra/terraform.tfvars.example infra/terraform.tfvars

# Run locally
cd src/InvoiceIndexer
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

Authenticate with:
```bash
az login
az account set --subscription <your-subscription-id>
```

## Required Environment Variables

| Variable | Description |
|---|---|
| `SEARCH_ENDPOINT` | Azure AI Search endpoint |
| `OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `OPENAI_EMBEDDING_DEPLOYMENT` | Embedding model deployment name |
| `OPENAI_GPT_DEPLOYMENT` | GPT deployment name |
| `OPENAI_GPT_MODEL_NAME` | GPT model name (e.g. `gpt-4o`) |
| `STORAGE_ACCOUNT_URL` | Blob storage account URL |
| `STORAGE_CONTAINER` | Blob container name |
| `SEARCH_INDEX_NAME` | AI Search index name |
| `KNOWLEDGE_SOURCE_NAME` | AI Foundry knowledge source name |
| `KNOWLEDGE_BASE_NAME` | AI Foundry knowledge base name |
| `DOCUMENT_INTELLIGENCE_ENDPOINT` | Document Intelligence endpoint |
