using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace LexisNexis.DocumentIntake.Infrastructure.Startup
{
    /// <summary>
    /// Creates the S3 bucket on startup if it doesn't exist.
    /// Self-heals when LocalStack restarts. In Development, connection failures are
    /// logged as warnings so the API can still start without LocalStack running.
    /// </summary>
    public class S3BucketInitialiser(
        IAmazonS3 s3,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<S3BucketInitialiser> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken ct)
        {
            var bucket = config["AWS:BucketName"]
                ?? throw new InvalidOperationException("AWS:BucketName is not configured.");

            try
            {
                try
                {
                    await s3.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket }, ct);
                    logger.LogInformation("S3 bucket '{BucketName}' already exists.", bucket);
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    await s3.PutBucketAsync(bucket, ct);
                    logger.LogInformation("Created S3 bucket: {BucketName}", bucket);
                }
            }
            catch (Exception ex)
            {
                if (env.IsDevelopment())
                {
                    // LocalStack may not be running locally — warn but allow startup to continue
                    logger.LogWarning(ex,
                        "Could not connect to S3 for bucket '{BucketName}'. " +
                        "Start LocalStack (docker compose up) to enable storage. " +
                        "Document uploads will fail until S3 is available.", bucket);
                }
                else
                {
                    logger.LogError(ex, "Failed to initialise S3 bucket '{BucketName}'.", bucket);
                    throw;
                }
            }
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
