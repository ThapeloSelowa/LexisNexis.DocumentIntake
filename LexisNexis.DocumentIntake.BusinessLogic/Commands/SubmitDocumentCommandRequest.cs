using FluentValidation;
using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using MediatR;


namespace LexisNexis.DocumentIntake.BusinessLogic.Commands
{
    public record SubmitDocumentCommandRequest : IRequest<SubmitDocumentResult>
    {
        public required string SourceDocumentId { get; init; }
        public required string Provider { get; init; }
        public required string Title { get; init; }
        public string? Jurisdiction { get; init; }
        public List<string> Tags { get; init; } = [];
        public required string ContentType { get; init; }
        public required string FileName { get; init; }
        public required Stream FileContent { get; init; }
        public string? CorrelationId { get; init; }
    }

    public record SubmitDocumentResult(DocumentId DocumentId,bool IsResubmission,string TransactionId);

    public class SubmitDocumentCommandValidator : AbstractValidator<SubmitDocumentCommandRequest>
    {
        private static readonly string[] AllowedContentTypes =
        [
            "application/pdf",
            "text/plain",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        ];

        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

        public SubmitDocumentCommandValidator()
        {
            RuleFor(x => x.SourceDocumentId)
                .NotEmpty()
                    .WithMessage("SourceDocumentId is required.")
                .MaximumLength(100)
                    .WithMessage("SourceDocumentId must not exceed 100 characters.")
                .Matches(@"^[a-zA-Z0-9\-_\.]+$")
                    .WithMessage("SourceDocumentId may only contain letters, numbers, hyphens, underscores and dots.");

            RuleFor(x => x.Provider)
                .NotEmpty()
                    .WithMessage("Provider is required.")
                .MaximumLength(50)
                    .WithMessage("Provider must not exceed 50 characters.");

            RuleFor(x => x.Title)
                .NotEmpty()
                    .WithMessage("Title is required.")
                .MaximumLength(200)
                    .WithMessage("Title must not exceed 200 characters.");

            RuleFor(x => x.ContentType)
                .NotEmpty()
                    .WithMessage("ContentType is required.")
                .Must(ct => AllowedContentTypes.Contains(ct))
                    .WithMessage($"ContentType must be one of: {string.Join(", ", AllowedContentTypes)}.");

            RuleFor(x => x.FileContent)
                .NotNull()
                    .WithMessage("File content is required.")
                .Must(f => f != null && f.Length > 0)
                    .WithMessage("File must not be empty.")
                .Must(f => f == null || f.Length <= MaxFileSizeBytes)
                    .WithMessage("File must not exceed 5 MB.");

            RuleFor(x => x.FileName)
                .NotEmpty()
                    .WithMessage("FileName is required.")
                .MaximumLength(255)
                    .WithMessage("FileName must not exceed 255 characters.");

            RuleFor(x => x.Tags)
                .Must(t => t == null || t.Count <= 20)
                    .WithMessage("A document may have at most 20 tags.");
        }
    }
}
