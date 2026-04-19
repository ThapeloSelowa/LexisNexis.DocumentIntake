using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.Infrastructure.Metrics
{
    /// <summary>
    /// Used in local development — logs metrics to the console instead of sending to CloudWatch.
    /// When you see a log line prefixed with "METRIC |" locally,
    /// that's what would be a CloudWatch custom metric in production.
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
