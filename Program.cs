using Serilog;
using FluentValidation;
using LocalRagAssistant.Models;
using LocalRagAssistant.Services;
using LocalRagAssistant.Validators;
using Microsoft.AspNetCore.Http.Features;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/localrag-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting LocalRagAssistant API");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Configure AppSettings from environment variables and appsettings.json
    builder.Services.Configure<AppSettings>(options =>
    {
        builder.Configuration.GetSection("AppSettings").Bind(options);
        
        // Override with environment variables if present
        options.LlmChatUrl = Environment.GetEnvironmentVariable("LLM_CHAT_URL") ?? options.LlmChatUrl;
        options.LlmEmbeddingUrl = Environment.GetEnvironmentVariable("LLM_EMBEDDING_URL") ?? options.LlmEmbeddingUrl;
        options.ChatModel = Environment.GetEnvironmentVariable("CHAT_MODEL") ?? options.ChatModel;
        options.EmbeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? options.EmbeddingModel;
        options.ChunkSize = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_SIZE"), out var chunkSize) ? chunkSize : options.ChunkSize;
        options.ChunkOverlap = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_OVERLAP"), out var overlap) ? overlap : options.ChunkOverlap;
        options.TopK = int.TryParse(Environment.GetEnvironmentVariable("TOP_K"), out var topK) ? topK : options.TopK;
        options.DataDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY") ?? options.DataDirectory;
        options.UploadDirectory = Environment.GetEnvironmentVariable("UPLOAD_DIRECTORY") ?? options.UploadDirectory;
    });

    // Configure file upload limits
    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 52428800; // 50 MB
    });

    // Add services
    builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>();
    builder.Services.AddHttpClient<IChatService, ChatService>();
    
    builder.Services.AddSingleton<IDocumentProcessingService, DocumentProcessingService>();
    builder.Services.AddSingleton<IVectorStoreService, VectorStoreService>();
    builder.Services.AddSingleton<IConversationService, ConversationService>();
    
    // Add validators
    builder.Services.AddValidatorsFromAssemblyContaining<ChatRequestValidator>();

    // Add CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // Add OpenAPI
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Configure middleware
    app.UseSerilogRequestLogging();
    app.UseCors();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // Serve static files from wwwroot
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // Health endpoint
    app.MapGet("/health", () =>
    {
        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "LocalRagAssistant"
        });
    })
    .WithName("Health")
    .WithOpenApi();

    // Upload endpoint
    app.MapPost("/upload", async (
        HttpRequest request,
        IDocumentProcessingService documentService,
        IEmbeddingService embeddingService,
        IVectorStoreService vectorStore,
        IValidator<UploadRequest> validator,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Invalid content type. Expected multipart/form-data" });
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");

            if (file == null)
            {
                return Results.BadRequest(new { error = "No file uploaded" });
            }

            var uploadRequest = new UploadRequest { File = file };
            var validationResult = await validator.ValidateAsync(uploadRequest, cancellationToken);

            if (!validationResult.IsValid)
            {
                return Results.BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
            }

            // Create upload directory if it doesn't exist
            var uploadDir = "uploads";
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            // Save file
            var filePath = Path.Combine(uploadDir, $"{Guid.NewGuid()}_{file.FileName}");
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            logger.LogInformation("File uploaded: {FileName}", file.FileName);

            // Process document
            var document = await documentService.ProcessDocumentAsync(filePath, file.FileName, file.ContentType, cancellationToken);

            // Generate embeddings for chunks
            logger.LogInformation("Generating embeddings for {Count} chunks", document.Chunks.Count);
            foreach (var chunk in document.Chunks)
            {
                chunk.Embedding = await embeddingService.GetEmbeddingAsync(chunk.Content, cancellationToken);
            }

            // Store document and chunks
            await vectorStore.AddDocumentAsync(document, cancellationToken);
            await vectorStore.UpsertChunksAsync(document.Chunks, cancellationToken);

            return Results.Ok(new UploadResponse
            {
                DocumentId = document.Id,
                FileName = document.FileName,
                ChunksCreated = document.Chunks.Count,
                Message = "Document uploaded and processed successfully"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing upload");
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    })
    .WithName("Upload")
    .DisableAntiforgery()
    .WithOpenApi();

    // Chat endpoint
    app.MapPost("/chat", async (
        ChatRequest request,
        IChatService chatService,
        IValidator<ChatRequest> validator,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                return Results.BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
            }

            var response = await chatService.ChatAsync(request, cancellationToken);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing chat request");
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    })
    .WithName("Chat")
    .WithOpenApi();

    // Rebuild index endpoint
    app.MapPost("/indexes/rebuild", async (
        IVectorStoreService vectorStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            logger.LogInformation("Rebuilding index");
            await vectorStore.RebuildIndexAsync(cancellationToken);
            
            var documents = await vectorStore.GetAllDocumentsAsync(cancellationToken);
            var totalChunks = documents.Sum(d => d.Chunks.Count);

            return Results.Ok(new
            {
                message = "Index rebuilt successfully",
                documentCount = documents.Count,
                chunkCount = totalChunks
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rebuilding index");
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    })
    .WithName("RebuildIndex")
    .WithOpenApi();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
