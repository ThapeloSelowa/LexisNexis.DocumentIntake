using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain.Events
{
    public record DocumentProcessedEvent(DocumentId DocumentId, string PreviewText) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }
}
