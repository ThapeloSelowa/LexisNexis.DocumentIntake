using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using Microsoft.Extensions.Logging;

namespace LexisNexis.DocumentIntake.Infrastructure.Metrics
{
    /// <summary>
    /// Used in local development — logs metrics to the console instead of sending to CloudWatch.
    /// </summary>
    public class NoOpMetricsService(ILogger<NoOpMetricsService> logger) : IMetricsService
    {
        public Task IncrementAsync(string metricName, CancellationToken ct = default)
        {
            logger.LogInformation("METRIC | {MetricName} +1", metricName);
            return Task.CompletedTask;
        }

        public Task RecordDurationAsync(string metricName, double milliseconds, CancellationToken ct = default)
        {
            logger.LogInformation("METRIC | {MetricName} = {Ms}ms", metricName, milliseconds);
            return Task.CompletedTask;
        }
    }
}
