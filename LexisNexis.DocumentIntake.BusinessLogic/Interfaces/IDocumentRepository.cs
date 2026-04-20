using LexisNexis.DocumentIntake.BusinessLogic.Domain;

namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IDocumentRepository
    {
        Task<Document?> FindByIdAsync(DocumentId id, CancellationToken ct = default);
        Task<Document?> FindByDedupKeyAsync(DedupKey key, CancellationToken ct = default);
        Task UpsertAsync(Document document, int expectedVersion, CancellationToken ct = default);
        Task<IReadOnlyList<Document>> QueryAsync(DocumentQueryFilter filter, CancellationToken ct = default);
    }

    public record DocumentQueryFilter(
        string? Provider = null,
        string? Tag = null,
        ProcessingStatus? Status = null,
        int Page = 1,
        int PageSize = 20);
}
