#!/bin/bash

echo "LocalRagAssistant - Setup Script"
echo "================================="
echo ""

# Check if Ollama is installed
if ! command -v ollama &> /dev/null; then
    echo "❌ Ollama is not installed."
    echo ""
    echo "Please install Ollama first:"
    echo "  curl https://ollama.ai/install.sh | sh"
    echo ""
    echo "Or visit: https://ollama.ai"
    exit 1
fi

echo "✓ Ollama is installed"
echo ""

# Check if Ollama is running
if ! curl -s http://localhost:11434/api/tags > /dev/null 2>&1; then
    echo "⚠️  Ollama is not running. Starting Ollama..."
    ollama serve &
    sleep 5
fi

echo "✓ Ollama is running"
echo ""

# Pull required models
echo "Pulling required models..."
echo ""

echo "1. Pulling chat model (llama3.2)..."
ollama pull llama3.2
echo "✓ Chat model ready"
echo ""

echo "2. Pulling embedding model (nomic-embed-text)..."
ollama pull nomic-embed-text
echo "✓ Embedding model ready"
echo ""

echo "================================="
echo "✓ Setup complete!"
echo ""
echo "You can now run the application:"
echo "  dotnet run"
echo ""
echo "Or with Docker:"
echo "  docker-compose up"
echo ""
echo "The application will be available at:"
echo "  http://localhost:5000"
echo ""
