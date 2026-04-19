using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Models
{
    /// <summary>
    /// The message that travels through the queue from the intake handler to the worker.
    /// CorrelationId and TransactionId are propagated so the worker's logs can be
    /// linked back to the original HTTP request in CloudWatch.
    /// </summary>
    public record ProcessingMessage
    {
        public DocumentId DocumentId { get; init; }
        public string SourceDocumentId { get; init; } = string.Empty;
        public string Action { get; init; } = "GeneratePreview";
        public DateTimeOffset SubmittedAt { get; init; }
        public string? CorrelationId { get; init; }  // Links async work to HTTP request
        public string? TransactionId { get; init; }  // For log investigation
        public int RetryCount { get; init; }  // Track retries
    }
}
