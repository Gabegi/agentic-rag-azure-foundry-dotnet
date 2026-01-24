# Azure AI Foundry Support Agent (RAG)

A .NET-based intelligent support agent that combines Retrieval-Augmented Generation (RAG) with Azure OpenAI and Azure AI Search to provide context-aware customer support.

## Architecture Overview

```
User Input → Conversation History → RAG Retrieval → Prompt Construction → GPT-4 → Function Router → Response
```

## Core Components

### 1. Document Ingestion Service
Converts FAQ documents into searchable vectors.

```
FAQ docs → Text Chunking (~500 tokens) → Embeddings (text-embedding-ada-002) → Azure AI Search Index
```

- Reads markdown/text files from `/docs`
- Generates embeddings via Azure OpenAI
- Stores vectors + metadata in AI Search index

### 2. RAG Retrieval Engine
Finds relevant documentation for user queries.

```
User query → Generate embedding → Vector search → Top 3-5 chunks (similarity > 0.7)
```

### 3. Agent Orchestrator
The main brain that coordinates LLM, tools, and context.

**Responsibilities:**
- Maintain conversation state (last 5-10 messages)
- Construct system prompts with RAG context
- Route to functions when needed
- Generate final responses

### 4. Function/Tool Layer
Extends agent capabilities beyond Q&A.

| Function | Description |
|----------|-------------|
| `CheckOrderStatus(orderId)` | Look up order status from database |
| `CreateTicket(issue, priority)` | Create a support ticket |
| `SearchDocs(query)` | Explicit knowledge base search |

## Data Schema

**AI Search Index:**
```json
{
  "id": "doc_001_chunk_01",
  "content": "Our return policy allows...",
  "embedding": [0.123, -0.456, ...],
  "metadata": {
    "source_file": "return-policy.md",
    "chunk_index": 1
  }
}
```

## Project Structure

```
SupportAgent.Core/
├── Models/
│   ├── ChatMessage.cs
│   ├── Order.cs
│   └── Ticket.cs
├── Services/
│   ├── EmbeddingService.cs       # Generate embeddings
│   ├── SearchService.cs          # Query AI Search
│   ├── AgentOrchestrator.cs      # Main agent logic
│   └── FunctionExecutor.cs       # Handle tool calls
├── Functions/
│   ├── OrderFunction.cs
│   └── TicketFunction.cs
└── Configuration/
    └── AzureConfig.cs

SupportAgent.Ingest/
└── Program.cs                     # Index documents

SupportAgent.Console/
└── Program.cs                     # Chat interface
```

## Example Flow

**User:** "My order #12345 hasn't shipped yet"

1. Orchestrator receives input
2. History Manager adds to conversation context
3. RAG Retrieval finds shipping policy chunks
4. Prompt Constructor builds context with tools and history
5. GPT-4 decides to call `CheckOrderStatus("12345")`
6. Function returns: `{ status: "Shipped", eta: "Jan 26" }`
7. GPT-4 responds: "Good news! Your order shipped and arrives Jan 26..."

## Azure Services Required

- **Azure OpenAI** - GPT-4 for reasoning, text-embedding-ada-002 for embeddings
- **Azure AI Search** - Vector store for RAG retrieval
