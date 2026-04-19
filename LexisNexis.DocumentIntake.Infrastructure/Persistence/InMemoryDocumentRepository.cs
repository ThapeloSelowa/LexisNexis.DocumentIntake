using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace LexisNexis.DocumentIntake.Infrastructure.Persistence
{
    /// <summary>
    /// Thread-safe in-memory repository.
    /// ConcurrentDictionary handles concurrent reads safely.
    /// A SemaphoreSlim guards writes to enforce optimistic concurrency.
    ///
    /// Limitation: State is lost on restart — acceptable per the assignment scope.
    /// Production would replace this with DynamoDB
    /// </summary>
    public class InMemoryDocumentRepository : IDocumentRepository
    {
        private readonly ConcurrentDictionary<string, Document> _byId = new();
        private readonly ConcurrentDictionary<string, string> _byDedupKey = new(); // dedupKey → id
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public Task<Document?> FindByIdAsync(DocumentId id, CancellationToken ct = default) =>
            Task.FromResult(_byId.TryGetValue(id.ToString(), out var doc) ? doc : null);

        public Task<Document?> FindByDedupKeyAsync(DedupKey key, CancellationToken ct = default)
        {
            if (!_byDedupKey.TryGetValue(key.ToString(), out var id)) return Task.FromResult<Document?>(null);
            return FindByIdAsync(DocumentId.Parse(id), ct);
        }

        public async Task UpsertAsync(Document document, int expectedVersion, CancellationToken ct = default)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                // Optimistic concurrency check
                if (_byId.TryGetValue(document.Id.ToString(), out var existing)
                    && existing.Version != expectedVersion)
                {
                    throw new ConcurrencyException(
                        document.Id, expectedVersion, existing.Version);
                }

                document.IncrementVersion();
                _byId[document.Id.ToString()] = document;
                _byDedupKey[document.DedupKey.ToString()] = document.Id.ToString();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task<IReadOnlyList<Document>> QueryAsync(
            DocumentQueryFilter filter, CancellationToken ct = default)
        {
            var query = _byId.Values.AsQueryable();

            if (!string.IsNullOrEmpty(filter.Provider))
                query = query.Where(d => d.Provider == filter.Provider);

            if (!string.IsNullOrEmpty(filter.Tag))
                query = query.Where(d => d.Tags.Contains(filter.Tag));

            if (filter.Status.HasValue)
                query = query.Where(d => d.Status == filter.Status.Value);

            var result = query
                .OrderByDescending(d => d.ReceivedAt)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToList();

            return Task.FromResult<IReadOnlyList<Document>>(result);
        }
    }

    public class ConcurrencyException(DocumentId id, int expected, int actual)
    : Exception($"Concurrency conflict on Document {id}. Expected version {expected}, found {actual}.");
}
