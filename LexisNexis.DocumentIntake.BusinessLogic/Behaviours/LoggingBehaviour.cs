using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace LexisNexis.DocumentIntake.BusinessLogic.Behaviours
{
    /// <summary>
    /// Logs every command and query with timing information.
    /// Runs first in the pipeline — captures ALL requests including those that fail validation.
    /// The TransactionId from Activity.Current links this log to the HTTP request log.
    /// </summary>
    public class LoggingBehaviour<TRequest, TResponse>(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request,RequestHandlerDelegate<TResponse> next,CancellationToken ct)
        {
            var requestName = typeof(TRequest).Name;
            var transactionId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();

            logger.LogInformation(
                "[{TransactionId}] --> Handling {RequestName}",
                transactionId, requestName);

            try
            {
                var response = await next();
                sw.Stop();

                logger.LogInformation(
                    "[{TransactionId}] <-- Completed {RequestName} in {ElapsedMs}ms",
                 transactionId, requestName, sw.ElapsedMilliseconds);

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(ex,
                    "[{TransactionId}] <-- Failed {RequestName} after {ElapsedMs}ms",
                    transactionId, requestName, sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
