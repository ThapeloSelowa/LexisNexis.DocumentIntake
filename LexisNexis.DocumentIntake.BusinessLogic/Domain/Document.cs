using LexisNexis.DocumentIntake.BusinessLogic.Domain.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain
{
    public sealed class Document
    {
        // ── Identity ───
        public DocumentId Id { get; private set; }
        public DedupKey DedupKey { get; private set; }
        public int Version { get; private set; }
        public string ETag => $"{Id}-v{Version}";

        // ── Submission metadata ───
        public string SourceDocumentId { get; private set; }
        public string Provider { get; private set; }
        public string Title { get; private set; }
        public string? Jurisdiction { get; private set; }
        public List<string> Tags { get; private set; } = [];
        public string ContentType { get; private set; }
        public string FileName { get; private set; }
        public int SubmissionCount { get; private set; }
        public DateTimeOffset ReceivedAt { get; private set; }
        public DateTimeOffset? UpdatedAt { get; private set; }

        // ── Storage ───
        public StorageKey? StorageKey { get; private set; }

        // ── Processing ───────────────────────────────────────────────────────────
        public ProcessingStatus Status { get; private set; }
        public string? Preview { get; private set; }
        public DateTimeOffset? ProcessedAt { get; private set; }

        // ── Audit & events ───────────────────────────────────────────────────────
        private readonly List<AuditEntry> _auditTrail = [];
        private readonly List<IDomainEvent> _domainEvents = [];

        public IReadOnlyList<AuditEntry> AuditTrail => _auditTrail.AsReadOnly();
        public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        // Private constructor — use factory methods below
        private Document() { }


        /// <summary>Creates a brand new document from a submission command.</summary>
        public static Document CreateNew(
            string sourceDocumentId,
            string provider,
            string title,
            string? jurisdiction,
            List<string> tags,
            string contentType,
            string fileName,
            string? correlationId = null)
        {
            var doc = new Document
            {
                Id = DocumentId.New(),
                SourceDocumentId = sourceDocumentId,
                Provider = provider,
                Title = title,
                Jurisdiction = jurisdiction,
                Tags = tags,
                ContentType = contentType,
                FileName = fileName,
                DedupKey = new DedupKey(provider, sourceDocumentId),
                Status = ProcessingStatus.Received,
                ReceivedAt = DateTimeOffset.UtcNow,
                SubmissionCount = 1,
                Version = 1
            };

            doc._auditTrail.Add(AuditEntry.Create(AuditEvent.Received, correlationId: correlationId));
            doc._domainEvents.Add(new DocumentReceivedEvent(doc.Id));
            return doc;
        }

        // ── Behaviour methods ───
        /// <summary>
        /// Called when the document is resubmitted by an upstream provider.
        /// Updates metadata and increments the submission count rather than creating a new record.
        /// </summary>
        public void RecordResubmission(string? correlationId = null)
        {
            // Reset to Received so the new file goes through the full pipeline again.
            // Skip transition if already Received (immediate resubmission before first upload completed).
            if (Status != ProcessingStatus.Received)
                TransitionTo(ProcessingStatus.Received);
            SubmissionCount++;
            UpdatedAt = DateTimeOffset.UtcNow;
            _auditTrail.Add(AuditEntry.Create(AuditEvent.Resubmitted,
                $"Resubmission #{SubmissionCount}", correlationId));
        }

        /// <summary>Called once the raw file has been successfully uploaded to S3.</summary>
        public void MarkAsStored(StorageKey storageKey, string? correlationId = null)
        {
            TransitionTo(ProcessingStatus.Stored);
            StorageKey = storageKey;
            UpdatedAt = DateTimeOffset.UtcNow;
            _auditTrail.Add(AuditEntry.Create(AuditEvent.Stored,
                storageKey.ToString(), correlationId));
        }

        /// <summary>Called once the processing message has been sent to the queue.</summary>
        public void MarkAsQueued(string? correlationId = null)
        {
            TransitionTo(ProcessingStatus.Queued);
            UpdatedAt = DateTimeOffset.UtcNow;
            _auditTrail.Add(AuditEntry.Create(AuditEvent.Queued, correlationId: correlationId));
        }

        /// <summary>Called by the background worker when processing begins.</summary>
        public void MarkAsProcessing(string? correlationId = null)
        {
            TransitionTo(ProcessingStatus.Processing);
            _auditTrail.Add(AuditEntry.Create(AuditEvent.ProcessingStarted,
                correlationId: correlationId));
        }

        /// <summary>Called by the background worker after preview generation succeeds.</summary>
        public void MarkAsProcessed(string preview, string? correlationId = null)
        {
            TransitionTo(ProcessingStatus.Processed);
            Preview = preview;
            ProcessedAt = DateTimeOffset.UtcNow;
            UpdatedAt = DateTimeOffset.UtcNow;
            _auditTrail.Add(AuditEntry.Create(AuditEvent.Processed,
                $"Preview length: {preview.Length} chars", correlationId));
            _domainEvents.Add(new DocumentProcessedEvent(Id, preview));
        }

        /// <summary>Called whenever processing fails at any stage.</summary>
        public void MarkAsFailed(string reason, string? correlationId = null)
        {
            TransitionTo(ProcessingStatus.Failed);
            UpdatedAt = DateTimeOffset.UtcNow;
            _auditTrail.Add(AuditEntry.Create(AuditEvent.Failed, reason, correlationId));
            _domainEvents.Add(new DocumentFailedEvent(Id, reason));
        }

        /// <summary>Clears domain events after they have been dispatched.</summary>
        public void ClearDomainEvents() => _domainEvents.Clear();

        /// <summary>Called by the repository on every save to prevent race conditions.</summary>
        public void IncrementVersion() => Version++;

        // ── Private helpers ───────────────────────────────────────────────────────

        private void TransitionTo(ProcessingStatus next)
        {
            if (!Status.CanTransitionTo(next))
                throw new InvalidOperationException(
                    $"Cannot transition document {Id} from {Status} to {next}.");
            Status = next;
        }
    }
}
