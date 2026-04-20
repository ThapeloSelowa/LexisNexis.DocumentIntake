using LexisNexis.DocumentIntake.BusinessLogic.Models;

namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IQueueService
    {
        Task EnqueueAsync(ProcessingMessage message, CancellationToken ct = default);
        Task<ProcessingMessage?> DequeueAsync(CancellationToken ct = default);
    }
}
