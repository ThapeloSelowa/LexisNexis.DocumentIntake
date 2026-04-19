using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain
{
    public enum ProcessingStatus
    {
        Received = 0,
        Stored = 1,
        Queued = 2,
        Processing = 3,
        Processed = 4,
        Failed = 5
    }

    /// <summary>
    /// Extension that enforces valid status transitions.
    /// A document can only move forward — never from Processed back to Queued, for example.
    /// This prevents bugs where worker code accidentally resets a successfully processed doc.
    /// </summary>
    public static class ProcessingStatusExtensions
    {
        private static readonly Dictionary<ProcessingStatus, ProcessingStatus[]> ValidTransitions = new()
        {
            // From Received, the document can either be successfully Stored or fail
            [ProcessingStatus.Received] = [ProcessingStatus.Stored, ProcessingStatus.Failed],
            // From Stored, the document can be Queued for processing or fail
            [ProcessingStatus.Stored] = [ProcessingStatus.Queued, ProcessingStatus.Failed],
            // From Queued, the document can be Processing or fail
            [ProcessingStatus.Queued] = [ProcessingStatus.Processing, ProcessingStatus.Failed],
            // From Processing, the document can be Processed or fail
            [ProcessingStatus.Processing] = [ProcessingStatus.Processed, ProcessingStatus.Failed],
            [ProcessingStatus.Processed] = [],  // Terminal state
            [ProcessingStatus.Failed] = [ProcessingStatus.Queued], // Retry is allowed
        };

        public static bool CanTransitionTo(this ProcessingStatus current, ProcessingStatus next) =>
            ValidTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next);
    }
}
