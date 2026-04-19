using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain.Events
{
    /// <summary>
    /// Domain events represent something that happened in the domain.
    /// They allow the Document to signal side effects (e.g., send notification)
    /// without needing to know about the services that handle them.
    /// </summary>
    public interface IDomainEvent
    {
        DateTimeOffset OccurredAt { get; }
    }
}
