using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Commands
{
    /// <summary>
    /// Handles document submission. This is the core business logic.
    /// Flow:
    /// 1. Check deduplication
    /// 2. Upload file to S3
    /// 3. Persist document metadata
    /// 4. Enqueue background processing message
    /// 5. Return result
    /// </summary>
    public class SubmitDocumentCommandHandler(IDocumentRepository repo,IStorageService storage,IQueueService queue,
        IMetricsService metrics, ILogger<SubmitDocumentCommandHandler> logger): IRequestHandler<SubmitDocumentCommandRequest, SubmitDocumentResult>
    {
        public async Task<SubmitDocumentResult> Handle(
            SubmitDocumentCommandRequest cmd,
            CancellationToken ct)
        {
            var transactionId = cmd.CorrelationId ?? Guid.NewGuid().ToString("N");
            var dedupKey = new DedupKey(cmd.Provider, cmd.SourceDocumentId);

            // Step 1: Deduplication check
            var existing = await repo.FindByDedupKeyAsync(dedupKey, ct);
            var isResubmission = existing is not null;

            var document = isResubmission
                ? existing!
                : Document.CreateNew(
                    cmd.SourceDocumentId,
                    cmd.Provider,
                    cmd.Title,
                    cmd.Jurisdiction,
                    cmd.Tags,
                    cmd.ContentType,
                    cmd.FileName,
                    transactionId);

            if (isResubmission)
            {
                document.RecordResubmission(transactionId);
                logger.LogInformation(
                    "[{TransactionId}] Resubmission detected. DocumentId: {DocumentId}, Count: {Count}",
                    transactionId, document.Id, document.SubmissionCount);

                await metrics.IncrementAsync("DocumentResubmitted", ct);
            }

            // Step 2: Upload to S3 
            var storageKey = await storage.UploadAsync(
                document.Id,
                cmd.Provider,
                cmd.FileName,
                cmd.ContentType,
                cmd.FileContent,
                ct);

            document.MarkAsStored(storageKey, transactionId);

            // Step 3: Persist
            await repo.UpsertAsync(document, document.Version - 1, ct);

            // Step 4: Enqueue
            await queue.EnqueueAsync(new ProcessingMessage
            {
                DocumentId = document.Id,
                SourceDocumentId = cmd.SourceDocumentId,
                Action = "GeneratePreview",
                SubmittedAt = DateTimeOffset.UtcNow,
                CorrelationId = cmd.CorrelationId,
                TransactionId = transactionId
            }, ct);

            document.MarkAsQueued(transactionId);
            await repo.UpsertAsync(document, document.Version - 1, ct);

            await metrics.IncrementAsync(
                isResubmission ? "DocumentResubmitted" : "DocumentSubmitted", ct);

            return new SubmitDocumentResult(document.Id, isResubmission, transactionId);
        }
    }
}
