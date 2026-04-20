using FluentValidation;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using LexisNexis.DocumentIntake.Infrastructure.Persistence;
using System.Data;

namespace LexisNexis.DocumentIntake_Api.Middleware
{
    /// <summary>
    /// Central exception handler for all unhandled exceptions.
    /// Every request gets a server-generated TransactionId
    /// This ID is:
    /// - Added to all log entries via a logging scope
    /// - Returned in the response header X-Transaction-Id
    /// - Returned in the response body so the caller can report it for investigation
    /// When an error occurs, the development team can use the TransactionId to see the full request lifecycle instantly on the logs.
    /// </summary>
    public class ExceptionMiddleware(RequestDelegate next,ILogger<ExceptionMiddleware> logger,IMetricsService metrics)
    {
        public async Task InvokeAsync(HttpContext httpContext)
        {
            var transactionId = Guid.NewGuid().ToString("N");

            httpContext.Items["TransactionId"] = transactionId;
            httpContext.Response.Headers["X-Transaction-Id"] = transactionId;

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["TransactionId"] = transactionId,
                ["ClientIp"] = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                ["Method"] = httpContext.Request.Method,
                ["Path"] = httpContext.Request.Path.ToString()
            });

            try
            {
                await next(httpContext);
            }
            catch (ValidationException ex)
            {
                logger.LogWarning(
                    "[{TransactionId}] Validation failed on {Path}: {Errors}",
                    transactionId,
                    httpContext.Request.Path,
                    string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));

                httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await httpContext.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    TransactionId = transactionId,
                    Status = 422,
                    Title = "Validation Failed",
                    Detail = "One or more validation rules were violated.",
                    Errors = ex.Errors.Select(e => e.ErrorMessage).ToList()
                });
            }
            catch (ConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "[{TransactionId}] Concurrency conflict: {Message}", transactionId, ex.Message);

                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await httpContext.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    TransactionId = transactionId,
                    Status = 409,
                    Title = "Conflict",
                    Detail = "The document was modified by another request. Please retry."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[{TransactionId}] Unhandled exception on {Method} {Path}",
                    transactionId, httpContext.Request.Method, httpContext.Request.Path);

                await metrics.IncrementAsync("UnhandledExceptions");

                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    TransactionId = transactionId,
                    Status = 500,
                    Title = "Internal Server Error",
                    Detail = $"An unexpected error occurred. Quote TransactionId '{transactionId}' when reporting this issue."
                });
            }
        }
    }
}
