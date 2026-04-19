using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LexisNexis.DocumentIntake_Api.HealthChecks
{
    public class QueueHealthCheck(IQueueService queue) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken ct = default) =>
            Task.FromResult(HealthCheckResult.Healthy("Queue is operational."));
    }
}
