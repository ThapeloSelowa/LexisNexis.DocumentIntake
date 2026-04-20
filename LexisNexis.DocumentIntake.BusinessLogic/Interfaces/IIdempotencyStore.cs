namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IIdempotencyStore
    {
        Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default);
        Task SetAsync(string key, IdempotencyEntry entry, CancellationToken ct = default);
    }

    public record IdempotencyEntry(int StatusCode, string Body, DateTimeOffset CreatedAt);

}
