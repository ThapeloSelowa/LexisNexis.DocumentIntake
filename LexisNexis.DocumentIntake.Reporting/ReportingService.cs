using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.Reporting
{
    /// <summary>
    /// Generates business-level reports from the document repository.
    /// Exposed via GET /api/v1/reports — useful for operations dashboards.
    /// </summary>
    public class ReportingService(IDocumentRepository repo)
    {
        public async Task<DocumentReport> GenerateAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default)
        {
            var filter = new DocumentQueryFilter(PageSize: int.MaxValue);
            var docs = await repo.QueryAsync(filter, ct);

            // Apply date range filter
            if (from.HasValue) docs = docs.Where(d => d.ReceivedAt >= from.Value).ToList();
            if (to.HasValue) docs = docs.Where(d => d.ReceivedAt <= to.Value).ToList();

            return new DocumentReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                From = from,
                To = to,
                TotalDocuments = docs.Count,

                ByProvider = docs
                    .GroupBy(d => d.Provider)
                    .ToDictionary(g => g.Key, g => g.Count()),

                ByStatus = docs
                    .GroupBy(d => d.Status.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),

                ByJurisdiction = docs
                    .Where(d => d.Jurisdiction is not null)
                    .GroupBy(d => d.Jurisdiction!)
                    .ToDictionary(g => g.Key, g => g.Count()),

                ResubmissionCount = docs.Count(d => d.SubmissionCount > 1),
                FailedCount = docs.Count(d => d.Status == ProcessingStatus.Failed),

                AverageProcessingMs = docs
                    .Where(d => d.ProcessedAt.HasValue)
                    .Select(d => (d.ProcessedAt!.Value - d.ReceivedAt).TotalMilliseconds)
                    .DefaultIfEmpty(0)
                    .Average()
            };
        }
    }

    // Report model
    public class DocumentReport
    {
        public DateTimeOffset GeneratedAt { get; init; }
        public DateTimeOffset? From { get; init; }
        public DateTimeOffset? To { get; init; }
        public int TotalDocuments { get; init; }
        public Dictionary<string, int> ByProvider { get; init; } = [];
        public Dictionary<string, int> ByStatus { get; init; } = [];
        public Dictionary<string, int> ByJurisdiction { get; init; } = [];
        public int ResubmissionCount { get; init; }
        public int FailedCount { get; init; }
        public double AverageProcessingMs { get; init; }
    }
}
