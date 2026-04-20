namespace LexisNexis.DocumentIntake.BusinessLogic.Domain
{
    /// <summary>
    /// The S3 object key for a stored document.
    /// Structured as: provider/documentId/filename
    /// </summary>
    public readonly record struct StorageKey(string Provider, DocumentId DocumentId, string FileName)
    {
        public override string ToString() =>
            $"{Provider.ToLowerInvariant()}/{DocumentId}/{FileName}";
    }
}
