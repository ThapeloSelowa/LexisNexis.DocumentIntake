using Polly;
using Polly.Retry;

namespace LexisNexis.DocumentIntake.Infrastructure.Resilience
{
    /// <summary>
    /// Pre-built resilience pipelines for infrastructure services.
    /// </summary>
    public static class ResiliencePipelines
    {
        /// <summary>
        /// Returns a resilience pipeline suitable for storage operations (S3).
        /// Retries up to 3 times with exponential back-off.
        /// </summary>
        public static ResiliencePipeline ForStorage()
        {
            return new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(500)
                })
                .Build();
        }
    }
}
