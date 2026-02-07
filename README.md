# LocalRagAssistant

A C#/.NET 8 Minimal API for AI-powered document chat using local LLMs. Upload documents (PDF, DOCX, TXT, MD) and chat with them using RAG (Retrieval Augmented Generation) with local embeddings and chat models.

## Features

- 📄 **Document Processing**: Upload and process PDF, DOCX, TXT, and MD files
- 🔍 **Semantic Search**: Vector-based similarity search using local embeddings
- 💬 **Chat Interface**: Ask questions about your documents with context-aware responses
- 📚 **Citations**: Get answers with references to source documents and chunks
- 💾 **Conversation History**: Persistent chat conversations
- 🌐 **Web UI**: Beautiful, responsive interface for document upload and chat
- 🔒 **100% Local**: All processing happens locally using your own LLM

## Architecture

- **Backend**: .NET 8 Minimal API with Serilog, FluentValidation, and Polly
- **Document Processing**: iText7 (PDF), DocumentFormat.OpenXml (DOCX), Markdig (MD)
- **Vector Store**: In-memory with JSON persistence
- **LLM Integration**: Configurable local LLM endpoints (Ollama, LM Studio, etc.)
- **Frontend**: Vanilla HTML/CSS/JavaScript

## Prerequisites

- .NET 8 SDK
- Local LLM server (e.g., Ollama, LM Studio) running:
  - Chat model (default: llama3.2)
  - Embedding model (default: nomic-embed-text)

### Setting up Ollama (Recommended)

```bash
# Install Ollama (https://ollama.ai)
curl https://ollama.ai/install.sh | sh

# Pull required models
ollama pull llama3.2
ollama pull nomic-embed-text
```

## Installation

```bash
# Clone the repository
git clone https://github.com/AlexanderErdelyi/doc-chat.git
cd doc-chat

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

The API will start on `http://localhost:5000` (or `https://localhost:5001`)

## Configuration

Configuration can be done via `appsettings.json` or environment variables:

### appsettings.json
```json
{
  "AppSettings": {
    "LlmChatUrl": "http://localhost:11434/api/chat",
    "LlmEmbeddingUrl": "http://localhost:11434/api/embed",
    "ChatModel": "llama3.2",
    "EmbeddingModel": "nomic-embed-text",
    "ChunkSize": 1000,
    "ChunkOverlap": 200,
    "TopK": 5,
    "DataDirectory": "data",
    "UploadDirectory": "uploads"
  }
}
```

### Environment Variables
- `LLM_CHAT_URL`: URL for chat completions
- `LLM_EMBEDDING_URL`: URL for embeddings
- `CHAT_MODEL`: Name of chat model
- `EMBEDDING_MODEL`: Name of embedding model
- `CHUNK_SIZE`: Size of text chunks (default: 1000)
- `CHUNK_OVERLAP`: Overlap between chunks (default: 200)
- `TOP_K`: Number of chunks to retrieve (default: 5)
- `DATA_DIRECTORY`: Directory for persistent storage
- `UPLOAD_DIRECTORY`: Directory for uploaded files

## API Endpoints

### GET /health
Health check endpoint
```bash
curl http://localhost:5000/health
```

### POST /upload
Upload and process documents
```bash
curl -X POST http://localhost:5000/upload \
  -F "file=@document.pdf"
```

### POST /chat
Chat with your documents
```bash
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What is this document about?",
    "conversationId": "optional-conversation-id"
  }'
```

### POST /indexes/rebuild
Rebuild the vector index
```bash
curl -X POST http://localhost:5000/indexes/rebuild
```

## Web Interface

Navigate to `http://localhost:5000` to access the web interface:

1. **Upload Documents**: Drag and drop or browse for files
2. **Chat**: Ask questions about your uploaded documents
3. **View Citations**: See which document chunks were used for each answer
4. **Conversation History**: Conversations are automatically saved

## Features in Detail

### Document Processing
- **PDF**: Extracts text from all pages
- **DOCX**: Extracts text from Word documents
- **TXT**: Plain text files
- **MD**: Markdown files

### Text Chunking
- Configurable chunk size (default: 1000 characters)
- Configurable overlap (default: 200 characters)
- Preserves context across chunks

### Vector Search
- Cosine similarity search
- Configurable top-K results
- Local embedding generation

### Chat
- Context-aware responses
- Citation support with document and chunk references
- Conversation history
- Polly retry policies for resilience

## Development

### Project Structure
```
.
├── Models/              # Data models
├── Services/            # Business logic services
├── Validators/          # FluentValidation validators
├── wwwroot/            # Static web files
├── Program.cs          # API endpoints and configuration
└── appsettings.json    # Configuration
```

### Dependencies
- **Serilog**: Logging
- **FluentValidation**: Input validation
- **Polly**: Resilience and retry policies
- **iText7**: PDF processing
- **DocumentFormat.OpenXml**: DOCX processing
- **Markdig**: Markdown processing

## License

This project is open source and available under the MIT License.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
