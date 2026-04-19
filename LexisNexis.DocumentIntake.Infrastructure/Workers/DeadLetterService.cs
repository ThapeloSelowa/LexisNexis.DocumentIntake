using LexisNexis.DocumentIntake.BusinessLogic.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.Infrastructure.Workers
{
    /// <summary>
    /// Holds messages that could not be processed after all retries.
    /// Exposed via a GET endpoint so operations teams can inspect failures.
    /// In production this would write to an SQS Dead Letter Queue.
    /// </summary>
    public class DeadLetterService(ILogger<DeadLetterService> logger)
    {
        private readonly ConcurrentQueue<DeadLetterEntry> _entries = new();

        public void Enqueue(ProcessingMessage message, Exception ex)
        {
            var entry = new DeadLetterEntry(message, ex.Message, DateTimeOffset.UtcNow);
            _entries.Enqueue(entry);

            logger.LogError(
                "DEAD LETTER | DocumentId: {DocumentId} | Reason: {Reason}",
                message.DocumentId, ex.Message);
        }

        public IReadOnlyList<DeadLetterEntry> GetAll() => _entries.ToList();
    }

    public record DeadLetterEntry(
        ProcessingMessage Message,
        string FailureReason,
        DateTimeOffset FailedAt);
}
