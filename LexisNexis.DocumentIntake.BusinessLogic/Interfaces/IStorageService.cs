using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Interfaces
{
    public interface IStorageService
    {
        /// <summary>Uploads the file and returns the storage key.</summary>
        Task<StorageKey> UploadAsync(
            DocumentId documentId,
            string provider,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken ct = default);

        /// <summary>Downloads the file as a stream.</summary>
        Task<Stream> DownloadAsync(StorageKey key, CancellationToken ct = default);

        /// <summary>Returns true if the object exists in storage.</summary>
        Task<bool> ExistsAsync(StorageKey key, CancellationToken ct = default);
    }
}
