using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using System;
using System.Collections.Generic;
using System.Text;

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
