using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog;

namespace LexisNexis.DocumentIntake.Infrastructure.Resilience
{
    /// <summary>
    /// Polly v8 resilience pipelines for external service calls.
    ///
    /// ForStorage() wraps all S3 calls with:
    /// 1. Retry — 3 attempts with exponential backoff + jitter
    /// 2. Circuit Breaker — opens if 50% of calls fail over 30 seconds
    /// 3. Timeout — no single call takes longer than 10 seconds
    ///
    /// The jitter on retries is important: without it, all callers retry at the
    /// same time after a transient failure, which causes a thundering herd.
    /// </summary>
    public static class ResiliencePipelines
    {
        public static ResiliencePipeline ForStorage() =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        Log.Warning(
                            "S3 call retry {Attempt} after delay {Delay}. Outcome: {Outcome}",
                            args.AttemptNumber, args.RetryDelay, args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(15),
                    OnOpened = args =>
                    {
                        Log.Error("S3 circuit breaker OPENED. Calls will fail fast for {BreakDuration}.",
                            args.BreakDuration);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        Log.Information("S3 circuit breaker CLOSED. Calls resuming normally.");
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(10))
                .Build();
    }
}
