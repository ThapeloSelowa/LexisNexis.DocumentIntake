using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using UglyToad.PdfPig;

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
    public class DocumentProcessingWorker(IQueueService queue,IDocumentRepository repo,IStorageService storage,
        IMetricsService metrics,DeadLetterService deadLetter1,ILogger<DocumentProcessingWorker> logger,
        IConfiguration config) : BackgroundService
    {
        private readonly int _maxPreviewLength =
            int.TryParse(config["Processing:MaxPreviewLength"], out var maxLen) ? maxLen : 500;

        private readonly int _workerDelayMs =
            int.TryParse(config["Processing:WorkerDelayMs"], out var delayMs) ? delayMs : 500;

        private readonly int _maxRetryAttempts =
            int.TryParse(config["Processing:MaxRetryAttempts"], out var maxRetry) ? maxRetry : 3;

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
                await repo.UpsertAsync(document, document.Version, ct);

                // ── Download from S3 ──────────────────────────────────────────
                if (document.StorageKey is null)
                    throw new InvalidOperationException("Document has no storage key.");

                await using var stream = await storage.DownloadAsync(document.StorageKey.Value, ct);

                // ── Generate preview ──────────────────────────────────────────
                var preview = await GeneratePreviewAsync(stream, document.ContentType, ct);

                // ── Mark as processed ─────────────────────────────────────────
                document.MarkAsProcessed(preview, message.TransactionId);
                await repo.UpsertAsync(document, document.Version, ct);

                sw.Stop();
                logger.LogInformation(
                    "Document {DocumentId} processed in {ElapsedMs}ms. Preview: {PreviewLength} chars",
                    message.DocumentId, sw.ElapsedMilliseconds, preview.Length);

                await metrics.IncrementAsync("DocumentProcessed", ct);
                await metrics.RecordDurationAsync("ProcessingDurationMs", sw.ElapsedMilliseconds, ct);
            }
            catch (Exception ex)
            {
                if (message.RetryCount < _maxRetryAttempts)
                {
                    var nextAttempt = message.RetryCount + 1;
                    logger.LogWarning(ex,
                        "Document {DocumentId} processing failed (attempt {Attempt}/{Max}) — re-queuing for retry.",
                        message.DocumentId, nextAttempt, _maxRetryAttempts);

                    try
                    {
                        var document = await repo.FindByIdAsync(message.DocumentId, ct);
                        if (document is not null)
                        {
                            document.MarkAsQueued(message.TransactionId);
                            await repo.UpsertAsync(document, document.Version, ct);
                        }
                    }
                    catch (Exception innerEx)
                    {
                        logger.LogError(innerEx,
                            "Could not reset status for retry on document {DocumentId}", message.DocumentId);
                    }

                    await queue.EnqueueAsync(message with { RetryCount = nextAttempt }, ct);
                    return;
                }

                logger.LogError(ex,
                    "Document {DocumentId} failed after {Max} attempts — sending to dead letter.",
                    message.DocumentId, _maxRetryAttempts);

                await metrics.IncrementAsync("ProcessingFailed", ct);

                try
                {
                    var document = await repo.FindByIdAsync(message.DocumentId, ct);
                    if (document is not null)
                    {
                        document.MarkAsFailed(ex.Message, message.TransactionId);
                        await repo.UpsertAsync(document, document.Version, ct);
                    }
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx,
                        "Could not update failed status for document {DocumentId}", message.DocumentId);
                }

                deadLetter.Enqueue(message, ex);
            }
        }

        private async Task<string> GeneratePreviewAsync(
            Stream stream, string contentType, CancellationToken ct)
        {
            if (contentType.StartsWith("text/") ||
                contentType is "application/json" or "application/xml"
                              or "application/csv" or "application/xhtml+xml")
            {
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync(ct);
                return content.Length <= _maxPreviewLength
                    ? content
                    : content[.._maxPreviewLength] + "...";
            }

            // CopyToAsync handles non-seekable network streams (e.g. S3 response stream)
            // where stream.Length is unavailable and ReadAsync may return partial data.
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            var text = contentType switch
            {
                "application/pdf" => ExtractPdfText(bytes),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ExtractDocxText(bytes),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ExtractXlsxText(bytes),
                _ => string.Empty
            };

            if (text.Length > 0)
                return text.Length <= _maxPreviewLength ? text : text[.._maxPreviewLength] + "...";

            return $"[{contentType}] No readable text could be extracted from this document.";
        }

        private static string ExtractPdfText(byte[] bytes)
        {
            try
            {
                using var pdf = PdfDocument.Open(bytes);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                    foreach (var word in page.GetWords())
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(word.Text);
                    }
                return sb.ToString().Trim();
            }
            catch { return string.Empty; }
        }

        private static string ExtractDocxText(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                using var doc = WordprocessingDocument.Open(ms, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body is null) return string.Empty;
                return string.Join(" ", body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t)))
                    .Trim();
            }
            catch { return string.Empty; }
        }

        private static string ExtractXlsxText(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                using var doc = SpreadsheetDocument.Open(ms, false);
                var workbook = doc.WorkbookPart;
                if (workbook is null) return string.Empty;

                var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable
                    .Elements<SharedStringItem>()
                    .Select(s => s.InnerText)
                    .ToArray() ?? [];

                var sb = new StringBuilder();
                foreach (var sheet in workbook.WorksheetParts)
                    foreach (var cell in sheet.Worksheet.Descendants<Cell>())
                    {
                        var value = cell.DataType?.Value == CellValues.SharedString
                            && int.TryParse(cell.CellValue?.Text, out var idx)
                            ? sharedStrings[idx]
                            : cell.CellValue?.Text ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(value);
                        }
                    }
                return sb.ToString().Trim();
            }
            catch { return string.Empty; }
        }
    }
}
