using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.Infrastructure.Metrics
{
    /// <summary>
    /// Sends custom metrics to AWS CloudWatch.
    /// Each metric can trigger a CloudWatch Alarm — e.g., alert if ProcessingFailed > 0.
    /// </summary>
    public class CloudWatchMetricsService(IAmazonCloudWatch cloudWatch,IHostEnvironment env) : IMetricsService
    {
        private const string Namespace = "DocumentIntake";

        public async Task IncrementAsync(string metricName, CancellationToken ct = default)
        {
            await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
            {
                Namespace = Namespace,
                MetricData =
                [
                    new MetricDatum
                {
                    MetricName = metricName,
                    Value      = 1,
                    Unit       = StandardUnit.Count,
                    Timestamp  = DateTime.UtcNow,
                    Dimensions =
                    [
                        new Dimension { Name = "Environment", Value = env.EnvironmentName }
                    ]
                }
                ]
            }, ct);
        }

        public async Task RecordDurationAsync(string metricName, double milliseconds, CancellationToken ct = default)
        {
            await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
            {
                Namespace = Namespace,
                MetricData =
                [
                    new MetricDatum
                {
                    MetricName = metricName,
                    Value      = milliseconds,
                    Unit       = StandardUnit.Milliseconds,
                    Timestamp  = DateTime.UtcNow
                }
                ]
            }, ct);
        }
    }
}
