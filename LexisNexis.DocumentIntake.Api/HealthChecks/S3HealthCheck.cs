using Amazon.S3;
using Amazon.S3.Util;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LexisNexis.DocumentIntake_Api.HealthChecks
{
    // <summary>
    /// Checks if S3 / LocalStack is reachable by listing the bucket.
    /// Used by the /health/ready endpoint.
    /// Kubernetes uses this to know when the pod is ready to serve traffic.
    /// </summary>
    public class S3HealthCheck(IAmazonS3 s3, IConfiguration config) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken ct = default)
        {
            try
            {
                var bucket = config["AWS:BucketName"] ?? "documents-local";
                var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket);

                return exists
                    ? HealthCheckResult.Healthy("S3 bucket is accessible.")
                    : HealthCheckResult.Degraded($"S3 bucket '{bucket}' does not exist.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("S3 is unreachable.", ex);
            }
        }
    }
}
