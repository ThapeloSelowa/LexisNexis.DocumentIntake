using LexisNexis.DocumentIntake.BusinessLogic.Domain;

namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IAuditService
    {
        Task RecordAsync(
            DocumentId documentId,
            AuditEvent auditEvent,
            string? detail = null,
            CancellationToken ct = default);
    }
}
