using Amazon.S3;
using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection.Metadata;
using System.Text;

namespace LexisNexis.DocumentIntake.Infrastructure.Storage
{
    /// <summary>
    /// Uploads and downloads documents from AWS S3.
    /// Works with both real AWS and LocalStack — the difference is just the ServiceURL config.
    /// Uses Polly for retry + circuit breaker (configured in ResiliencePipelines).
    /// </summary>
    public class S3StorageService(
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<S3StorageService> logger) : IStorageService
    {
        private readonly string _bucketName = config["AWS:BucketName"]
            ?? throw new InvalidOperationException("AWS:BucketName is not configured.");

        private readonly ResiliencePipeline _pipeline =
            ResiliencePipelines.ForStorage();

        public async Task<StorageKey> UploadAsync(
            DocumentId documentId,
            string provider,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken ct = default)
        {
            var key = new StorageKey(provider, documentId, fileName);

            await _pipeline.ExecuteAsync(async token =>
            {
                var request = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key.ToString(),
                    InputStream = content,
                    ContentType = contentType,
                    Metadata =
                {
                    ["documentId"] = documentId.ToString(),
                    ["provider"]   = provider
                }
                };

                await s3.PutObjectAsync(request, token);

                logger.LogInformation(
                    "Uploaded document {DocumentId} to S3 at key {Key}",
                    documentId, key);
            }, ct);

            return key;
        }

        public async Task<Stream> DownloadAsync(StorageKey key, CancellationToken ct = default)
        {
            var response = await _pipeline.ExecuteAsync(async token =>
                await s3.GetObjectAsync(_bucketName, key.ToString(), token), ct);

            return response.ResponseStream;
        }

        public async Task<bool> ExistsAsync(StorageKey key, CancellationToken ct = default)
        {
            try
            {
                await s3.GetObjectMetadataAsync(_bucketName, key.ToString(), ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}
