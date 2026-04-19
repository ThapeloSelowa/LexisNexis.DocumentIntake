using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IMetricsService
    {
        Task IncrementAsync(string metricName, CancellationToken ct = default);
        Task RecordDurationAsync(string metricName, double milliseconds, CancellationToken ct = default);
    }
}
