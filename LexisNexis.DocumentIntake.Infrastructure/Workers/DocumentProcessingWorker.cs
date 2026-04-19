using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace LexisNexis.DocumentIntake.Infrastructure.Workers
{
    /// <summary>
    /// Long-running background service that continuously dequeues messages
    /// and generates document previews.
    ///
    /// Key design decisions:
    /// - Inherits BackgroundService — managed by the .NET host lifecycle
    /// - Restores CorrelationId from the message so all worker logs link to
    ///   the original HTTP request in CloudWatch (distributed tracing without X-Ray)
    /// - Failed messages go to a dead-letter store and the document is marked Failed
    /// - Retry logic is handled by Polly at the S3 level — the worker itself has a simple
    ///   try/catch for application-level failures
    /// </summary>
    public class DocumentProcessingWorker(
        IQueueService queue,
        IDocumentRepository repo,
        IStorageService storage,
        IMetricsService metrics,
        DeadLetterService deadLetter,
        ILogger<DocumentProcessingWorker> logger,
        IConfiguration config) : BackgroundService
    {
        private readonly int _maxPreviewLength =
            int.TryParse(config["Processing:MaxPreviewLength"], out var maxLen) ? maxLen : 500;

        private readonly int _workerDelayMs =
            int.TryParse(config["Processing:WorkerDelayMs"], out var delayMs) ? delayMs : 500;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("DocumentProcessingWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var message = await queue.DequeueAsync(stoppingToken);

                    if (message is null)
                    {
                        await Task.Delay(_workerDelayMs, stoppingToken);
                        continue;
                    }

                    await ProcessMessageAsync(message, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown — expected on app stop
                    break;
                }
                catch (Exception ex)
                {
                    // Log but keep the worker alive — never crash the loop
                    logger.LogError(ex, "Unexpected error in worker loop.");
                    await Task.Delay(1000, stoppingToken); // Brief pause before retry
                }
            }

            logger.LogInformation("DocumentProcessingWorker stopped.");
        }

        private async Task ProcessMessageAsync(ProcessingMessage message, CancellationToken ct)
        {
            // ── Restore distributed trace context ─────────────────────────────
            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = message.CorrelationId ?? "unknown",
                ["TransactionId"] = message.TransactionId ?? "unknown",
                ["DocumentId"] = message.DocumentId.ToString()
            });

            logger.LogInformation(
                "Processing document {DocumentId} (Attempt #{Retry})",
                message.DocumentId, message.RetryCount + 1);

            var sw = Stopwatch.StartNew();

            try
            {
                var document = await repo.FindByIdAsync(message.DocumentId, ct);
                if (document is null)
                {
                    logger.LogWarning(
                        "Document {DocumentId} not found — skipping.", message.DocumentId);
                    return;
                }

                // ── Mark as in-progress ───────────────────────────────────────
                document.MarkAsProcessing(message.TransactionId);
                await repo.UpsertAsync(document, document.Version - 1, ct);

                // ── Download from S3 ──────────────────────────────────────────
                if (document.StorageKey is null)
                    throw new InvalidOperationException("Document has no storage key.");

                await using var stream = await storage.DownloadAsync(document.StorageKey.Value, ct);

                // ── Generate preview ──────────────────────────────────────────
                var preview = await GeneratePreviewAsync(stream, document.ContentType, ct);

                // ── Mark as processed ─────────────────────────────────────────
                document.MarkAsProcessed(preview, message.TransactionId);
                await repo.UpsertAsync(document, document.Version - 1, ct);

                sw.Stop();
                logger.LogInformation(
                    "Document {DocumentId} processed in {ElapsedMs}ms. Preview: {PreviewLength} chars",
                    message.DocumentId, sw.ElapsedMilliseconds, preview.Length);

                await metrics.IncrementAsync("DocumentProcessed", ct);
                await metrics.RecordDurationAsync("ProcessingDurationMs", sw.ElapsedMilliseconds, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to process document {DocumentId}", message.DocumentId);

                await metrics.IncrementAsync("ProcessingFailed", ct);

                // Attempt to mark document as failed
                try
                {
                    var document = await repo.FindByIdAsync(message.DocumentId, ct);
                    if (document is not null)
                    {
                        document.MarkAsFailed(ex.Message, message.TransactionId);
                        await repo.UpsertAsync(document, document.Version - 1, ct);
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx,
                        "Could not update failed status for document {DocumentId}",
                        message.DocumentId);
                }

                // Send to dead letter queue for investigation
                deadLetter.Enqueue(message, ex);
            }
        }

        private async Task<string> GeneratePreviewAsync(
            Stream stream, string contentType, CancellationToken ct)
        {
            // For text content — read directly
            if (contentType is "text/plain")
            {
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync(ct);
                return content.Length <= _maxPreviewLength
                    ? content
                    : content[.._maxPreviewLength] + "...";
            }

            // For binary content (PDF, DOC) — describe the content type and size
            // In production this would use a PDF extraction library like iText or PdfPig
            var sizeKb = stream.Length / 1024;
            return $"[{contentType}] Document preview — {sizeKb}KB file. " +
                   $"Extraction of {contentType} requires a licensed parser in production.";
        }
    }
}
