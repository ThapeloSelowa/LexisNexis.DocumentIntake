using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain.Events
{
    public record DocumentFailedEvent(DocumentId DocumentId, string Reason) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }
}
