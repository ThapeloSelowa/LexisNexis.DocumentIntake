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
            [ProcessingStatus.Received]   = [ProcessingStatus.Stored,   ProcessingStatus.Failed],
            // Received = resubmission with a new file while upload is in-flight
            [ProcessingStatus.Stored]     = [ProcessingStatus.Received, ProcessingStatus.Queued,    ProcessingStatus.Failed],
            [ProcessingStatus.Queued]     = [ProcessingStatus.Received, ProcessingStatus.Processing, ProcessingStatus.Failed],
            [ProcessingStatus.Processing] = [ProcessingStatus.Received, ProcessingStatus.Processed,  ProcessingStatus.Failed],
            // Resubmission resets the document so it goes through the full pipeline again
            [ProcessingStatus.Processed]  = [ProcessingStatus.Received],
            // Failed: Queued = retry existing file; Received = resubmission with new file
            [ProcessingStatus.Failed]     = [ProcessingStatus.Received, ProcessingStatus.Queued],
        };

        public static bool CanTransitionTo(this ProcessingStatus current, ProcessingStatus next) =>
            ValidTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next);
    }
}
