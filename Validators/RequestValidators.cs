using FluentValidation;
using LocalRagAssistant.Models;

namespace LocalRagAssistant.Validators;

public class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Query is required")
            .MaximumLength(5000).WithMessage("Query must not exceed 5000 characters");
    }
}

public class UploadRequestValidator : AbstractValidator<UploadRequest>
{
    private static readonly string[] AllowedExtensions = { ".pdf", ".docx", ".txt", ".md" };
    private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB

    public UploadRequestValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required")
            .Must(file => file.Length > 0).WithMessage("File cannot be empty")
            .Must(file => file.Length <= MaxFileSize).WithMessage($"File size must not exceed {MaxFileSize / (1024 * 1024)} MB")
            .Must(file => AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            .WithMessage($"File must be one of the following types: {string.Join(", ", AllowedExtensions)}");
    }
}
