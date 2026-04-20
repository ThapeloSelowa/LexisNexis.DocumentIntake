using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using System.Collections.Concurrent;

namespace LexisNexis.DocumentIntake.Infrastructure.Persistence;

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _store = new();

    public Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var entry))
        {
            return Task.FromResult<IdempotencyEntry?>(null);
        }


        if (entry.CreatedAt.AddHours(24) < DateTimeOffset.UtcNow)
        {
            _store.TryRemove(key, out _);
            return Task.FromResult<IdempotencyEntry?>(null);
        }

        return Task.FromResult<IdempotencyEntry?>(entry);
    }

    public Task SetAsync(string key, IdempotencyEntry entry, CancellationToken ct = default)
    {
        _store[key] = entry;
        return Task.CompletedTask;
    }
}
