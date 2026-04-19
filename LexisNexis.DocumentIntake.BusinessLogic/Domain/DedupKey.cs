using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Domain
{
    /// <summary>
    /// The deduplication key is a combination of the upstream provider name
    /// and the provider's own document ID. Two submissions with the same key
    /// represent the same external document and should NOT create two records.
    /// </summary>
    public readonly record struct DedupKey(string Provider, string SourceDocumentId)
    {
        public override string ToString() =>
            $"{Provider.ToLowerInvariant()}:{SourceDocumentId}";
    }
}
