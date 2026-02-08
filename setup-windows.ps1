Write-Host "=================================" -ForegroundColor Cyan
Write-Host "Ollama Setup for Windows" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Step 1: Download and install Ollama" -ForegroundColor Yellow
Write-Host "  The download page is now open in your browser" -ForegroundColor White
Write-Host "  Download and run the installer" -ForegroundColor White
Write-Host ""
Write-Host "Step 2: After installation, run these commands:" -ForegroundColor Yellow
Write-Host "  ollama pull llama3.2" -ForegroundColor Green
Write-Host "  ollama pull nomic-embed-text" -ForegroundColor Green
Write-Host ""
Write-Host "Step 3: Verify installation:" -ForegroundColor Yellow
Write-Host "  ollama list" -ForegroundColor Green
Write-Host ""
Write-Host "=================================" -ForegroundColor Cyan
Write-Host "Press Enter after installing Ollama to continue with model download..." -ForegroundColor White

$null = Read-Host

Write-Host ""
Write-Host "Pulling llama3.2 model..." -ForegroundColor Yellow
ollama pull llama3.2

Write-Host ""
Write-Host "Pulling nomic-embed-text model..." -ForegroundColor Yellow
ollama pull nomic-embed-text

Write-Host ""
Write-Host " Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Available models:" -ForegroundColor Cyan
ollama list
