using LexisNexis.DocumentIntake.BusinessLogic.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IQueueService
    {
        Task EnqueueAsync(ProcessingMessage message, CancellationToken ct = default);
        Task<ProcessingMessage?> DequeueAsync(CancellationToken ct = default);
    }
}
