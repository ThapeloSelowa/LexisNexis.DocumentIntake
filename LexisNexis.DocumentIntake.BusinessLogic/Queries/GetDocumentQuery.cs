using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Queries
{
    public record GetDocumentQuery(DocumentId DocumentId) : IRequest<DocumentDto?>;

    public record DocumentDto(
        string DocumentId,
        string SourceDocumentId,
        string Provider,
        string Title,
        string? Jurisdiction,
        List<string> Tags,
        string ContentType,
        string FileName,
        string Status,
        int SubmissionCount,
        DateTimeOffset ReceivedAt,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<AuditEntryDto> AuditTrail);

    public record AuditEntryDto(string Event, DateTimeOffset Timestamp, string? Detail);

    public class GetDocumentQueryHandler(IDocumentRepository repo)
        : IRequestHandler<GetDocumentQuery, DocumentDto?>
    {
        public async Task<DocumentDto?> Handle(GetDocumentQuery query, CancellationToken ct)
        {
            var doc = await repo.FindByIdAsync(query.DocumentId, ct);
            if (doc is null) return null;

            return new DocumentDto(
                doc.Id.ToString(),
                doc.SourceDocumentId,
                doc.Provider,
                doc.Title,
                doc.Jurisdiction,
                doc.Tags,
                doc.ContentType,
                doc.FileName,
                doc.Status.ToString(),
                doc.SubmissionCount,
                doc.ReceivedAt,
                doc.UpdatedAt,
                doc.AuditTrail.Select(a =>
                    new AuditEntryDto(a.Event.ToString(), a.Timestamp, a.Detail)).ToList());
        }
    }
}
