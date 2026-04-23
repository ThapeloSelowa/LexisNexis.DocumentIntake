using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;

namespace LexisNexis.DocumentIntake.Reporting
{
    /// <summary>
    /// Generates business-level reports from the document repository.
    /// Exposed via GET /api/v1/reports — useful for operations dashboards.
    /// </summary>
    public class ReportingService(IDocumentRepository repo)
    {
        private static readonly ProcessingStatus[] PendingStatuses =
            [ProcessingStatus.Received, ProcessingStatus.Stored, ProcessingStatus.Queued, ProcessingStatus.Processing];

        public async Task<DocumentReport> GenerateAsync(DateTimeOffset? from = null,DateTimeOffset? to = null,CancellationToken ct = default)
        {
            var filter = new DocumentQueryFilter(PageSize: int.MaxValue);
            var docs = await repo.QueryAsync(filter, ct);

            if (from.HasValue) { docs = docs.Where(d => d.ReceivedAt >= from.Value).ToList(); }
            if (to.HasValue) { docs = docs.Where(d => d.ReceivedAt <= to.Value).ToList(); }

            var total = docs.Count;
            var failedCount = docs.Count(d => d.Status == ProcessingStatus.Failed);
            var processed = docs.Count(d => d.Status == ProcessingStatus.Processed);

            var processingTimes = docs
                .Where(d => d.ProcessedAt.HasValue)
                .Select(d => (d.ProcessedAt!.Value - d.ReceivedAt).TotalMilliseconds)
                .ToList();

            return new DocumentReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                From = from,
                To = to,
                TotalDocuments = total,
                PendingCount = docs.Count(d => PendingStatuses.Contains(d.Status)),
                ProcessedCount = processed,
                FailedCount = failedCount,
                SuccessRate = total > 0 ? Math.Round((double)processed / total * 100, 1) : 0,
                ResubmissionCount = docs.Count(d => d.SubmissionCount > 1),

                ByProvider = docs
                    .GroupBy(d => d.Provider)
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count()),

                ByStatus = docs
                    .GroupBy(d => d.Status.ToString())
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count()),

                ByJurisdiction = docs
                    .Where(d => d.Jurisdiction is not null)
                    .GroupBy(d => d.Jurisdiction!)
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count()),

                ByContentType = docs
                    .GroupBy(d => d.ContentType)
                    .OrderByDescending(g => g.Count())
                    .ToDictionary(g => g.Key, g => g.Count()),

                ProcessingTime = processingTimes.Count > 0
                    ? new ProcessingTimeStats
                    {
                        AverageMs = Math.Round(processingTimes.Average(), 0),
                        MinMs = Math.Round(processingTimes.Min(), 0),
                        MaxMs = Math.Round(processingTimes.Max(), 0)
                    }
                    : null
            };
        }
    }

    public class DocumentReport
    {
        public DateTimeOffset GeneratedAt { get; init; }
        public DateTimeOffset? From { get; init; }
        public DateTimeOffset? To { get; init; }
        public int TotalDocuments { get; init; }
        public int PendingCount { get; init; }
        public int ProcessedCount { get; init; }
        public int FailedCount { get; init; }
        public double SuccessRate { get; init; }
        public int ResubmissionCount { get; init; }
        public Dictionary<string, int> ByProvider { get; init; } = [];
        public Dictionary<string, int> ByStatus { get; init; } = [];
        public Dictionary<string, int> ByJurisdiction { get; init; } = [];
        public Dictionary<string, int> ByContentType { get; init; } = [];
        public ProcessingTimeStats? ProcessingTime { get; init; }
    }

    public class ProcessingTimeStats
    {
        public double AverageMs { get; init; }
        public double MinMs { get; init; }
        public double MaxMs { get; init; }
    }
}
