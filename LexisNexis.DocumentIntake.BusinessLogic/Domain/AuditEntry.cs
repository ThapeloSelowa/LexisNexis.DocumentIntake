using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain
{
    public enum AuditEvent
    {
        Received,
        Stored,
        Queued,
        ProcessingStarted,
        Processed,
        Failed,
        Resubmitted
    }

    /// <summary>
    /// An immutable record of something that happened to a document.
    /// DateTimeOffset (not DateTime) is used to preserve UTC offset information.
    /// This matters when logs come from servers in different time zones.
    /// </summary>
    public sealed record AuditEntry
    {
        public AuditEvent Event { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public string? Detail { get; init; }
        public string? CorrelationId { get; init; }

        public static AuditEntry Create(
            AuditEvent auditEvent,
            string? detail = null,
            string? correlationId = null) =>
            new()
            {
                Event = auditEvent,
                Timestamp = DateTimeOffset.UtcNow,
                Detail = detail,
                CorrelationId = correlationId
            };
    }
}
