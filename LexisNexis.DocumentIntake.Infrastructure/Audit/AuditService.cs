using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using Microsoft.Extensions.Logging;

namespace LexisNexis.DocumentIntake.Infrastructure.Audit
{
    /// <summary>
    /// Audit service that logs entries both to the structured log (CloudWatch) and to the in-memory document record.
    /// </summary>
    public class AuditService(IDocumentRepository repo, ILogger<AuditService> logger) : IAuditService
    {
        public async Task RecordAsync(DocumentId documentId, AuditEvent auditEvent,string? detail = null,
            CancellationToken ct = default)
        {
            logger.LogInformation(
                "AUDIT | DocumentId: {DocumentId} | Event: {Event} | Detail: {Detail}",
                documentId, auditEvent, detail ?? "—");
        }
    }
}
