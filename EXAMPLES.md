# LocalRagAssistant - API Examples

This file contains examples of how to use the LocalRagAssistant API.

## Prerequisites

Ensure you have a local LLM server running (e.g., Ollama):

```bash
# Install Ollama
curl https://ollama.ai/install.sh | sh

# Pull required models
ollama pull llama3.2
ollama pull nomic-embed-text

# Ollama will run on http://localhost:11434 by default
```

## Starting the Application

```bash
# Clone and navigate to the repository
git clone https://github.com/AlexanderErdelyi/doc-chat.git
cd doc-chat

# Run the application
dotnet run

# The API will be available at http://localhost:5000
# The Web UI will be available at http://localhost:5000
```

## API Examples

### 1. Check Health

```bash
curl http://localhost:5000/health
```

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2026-02-07T12:00:00Z",
  "service": "LocalRagAssistant"
}
```

### 2. Upload a Document

```bash
# Upload a PDF file
curl -X POST http://localhost:5000/upload \
  -F "file=@/path/to/document.pdf"

# Upload a DOCX file
curl -X POST http://localhost:5000/upload \
  -F "file=@/path/to/document.docx"

# Upload a Markdown file
curl -X POST http://localhost:5000/upload \
  -F "file=@/path/to/document.md"

# Upload a text file
curl -X POST http://localhost:5000/upload \
  -F "file=@/path/to/document.txt"
```

**Response:**
```json
{
  "documentId": "abc123-def456-ghi789",
  "fileName": "document.pdf",
  "chunksCreated": 15,
  "message": "Document uploaded and processed successfully"
}
```

### 3. Chat with Your Documents

```bash
# First chat message (creates a new conversation)
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What is this document about?"
  }'
```

**Response:**
```json
{
  "answer": "Based on the provided documents, this document discusses...",
  "citations": [
    {
      "fileName": "document.pdf",
      "chunkIndex": 0,
      "content": "This is the relevant chunk text..."
    },
    {
      "fileName": "document.pdf",
      "chunkIndex": 3,
      "content": "Another relevant chunk..."
    }
  ],
  "conversationId": "conv-123-456-789"
}
```

```bash
# Continue the conversation (use the conversationId from previous response)
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{
    "query": "Can you elaborate on that?",
    "conversationId": "conv-123-456-789"
  }'
```

### 4. Rebuild Index

```bash
curl -X POST http://localhost:5000/indexes/rebuild
```

**Response:**
```json
{
  "message": "Index rebuilt successfully",
  "documentCount": 5,
  "chunkCount": 75
}
```

## Using the Web Interface

1. Navigate to `http://localhost:5000` in your browser
2. Drag and drop or click to browse for files (PDF, DOCX, TXT, MD)
3. Click "Upload" to process the documents
4. Once uploaded, type your question in the chat input
5. Click "Send" or press Enter to get answers with citations
6. Continue the conversation - it's automatically saved!

## Configuration Examples

### Environment Variables

```bash
# Set custom LLM endpoints
export LLM_CHAT_URL="http://localhost:11434/api/chat"
export LLM_EMBEDDING_URL="http://localhost:11434/api/embed"

# Use different models
export CHAT_MODEL="mistral"
export EMBEDDING_MODEL="nomic-embed-text"

# Customize chunking
export CHUNK_SIZE=500
export CHUNK_OVERLAP=100
export TOP_K=3

# Set custom directories
export DATA_DIRECTORY="./my-data"
export UPLOAD_DIRECTORY="./my-uploads"

# Run the application
dotnet run
```

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

## Common Use Cases

### 1. Document Q&A
Upload your documents and ask questions:
- "What are the main topics covered?"
- "Summarize the key findings"
- "What does it say about X?"

### 2. Multi-Document Search
Upload multiple documents and ask cross-document questions:
- "Compare the approaches in document A and B"
- "What do all documents say about topic X?"

### 3. Research Assistant
Upload research papers and get insights:
- "What methodology was used?"
- "What were the conclusions?"
- "Are there any limitations mentioned?"

### 4. Technical Documentation
Upload technical docs and get help:
- "How do I configure feature X?"
- "What are the requirements for Y?"
- "Show me examples of Z"

## Troubleshooting

### LLM Connection Issues
If you get connection errors:
1. Ensure Ollama (or your LLM server) is running
2. Check the URL configuration matches your LLM server
3. Verify the models are pulled: `ollama list`

### Upload Failures
If uploads fail:
1. Check file size (max 50MB)
2. Verify file type is supported (PDF, DOCX, TXT, MD)
3. Check logs in `logs/` directory

### Slow Responses
If responses are slow:
1. Reduce `TopK` to retrieve fewer chunks
2. Reduce `ChunkSize` for faster embedding
3. Use a faster embedding model
4. Consider using a smaller chat model

## Performance Tips

1. **Chunking**: Adjust `ChunkSize` and `ChunkOverlap` based on your documents
   - Larger chunks: More context, slower processing
   - Smaller chunks: Faster processing, less context

2. **Top-K**: Adjust the number of chunks retrieved
   - Higher TopK: More context, slower responses
   - Lower TopK: Faster responses, may miss context

3. **Models**: Choose appropriate models for your hardware
   - Smaller models (3B): Faster, less accurate
   - Larger models (13B+): Slower, more accurate

## Security Notes

- This application is designed for local use
- All data is stored locally in JSON files
- No data is sent to external services
- Suitable for sensitive documents
- Enable authentication if exposing to network

## Next Steps

- Add more document types
- Implement authentication
- Add document management UI
- Support for images and tables
- Multi-language support
- Export conversations
