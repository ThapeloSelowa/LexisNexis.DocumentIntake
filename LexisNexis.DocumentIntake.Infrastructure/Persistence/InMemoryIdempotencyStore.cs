using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using System.Collections.Concurrent;

namespace LexisNexis.DocumentIntake.Infrastructure.Persistence;

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _store = new();

    public Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        _store.TryGetValue(key, out var entry);
        return Task.FromResult(entry);
    }

    public Task SetAsync(string key, IdempotencyEntry entry, CancellationToken ct = default)
    {
        _store[key] = entry;
        return Task.CompletedTask;
    }
}
