using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Queries
{
    public record GetDocumentStatusQuery(DocumentId DocumentId) : IRequest<DocumentStatusDto?>;

    public record DocumentStatusDto(
        string DocumentId,
        string SourceDocumentId,
        string Status,
        string? Preview,
        int? PreviewLength,
        DateTimeOffset ReceivedAt,
        DateTimeOffset? ProcessedAt,
        DateTimeOffset? UpdatedAt);

    public class GetDocumentStatusQueryHandler(IDocumentRepository repo)
        : IRequestHandler<GetDocumentStatusQuery, DocumentStatusDto?>
    {
        public async Task<DocumentStatusDto?> Handle(
            GetDocumentStatusQuery query, CancellationToken ct)
        {
            var doc = await repo.FindByIdAsync(query.DocumentId, ct);
            if (doc is null) return null;

            return new DocumentStatusDto(
                doc.Id.ToString(),
                doc.SourceDocumentId,
                doc.Status.ToString(),
                doc.Preview,
                doc.Preview?.Length,
                doc.ReceivedAt,
                doc.ProcessedAt,
                doc.UpdatedAt);
        }
    }
}
