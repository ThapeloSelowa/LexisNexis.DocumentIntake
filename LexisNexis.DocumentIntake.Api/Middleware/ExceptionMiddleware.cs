using FluentValidation;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using LexisNexis.DocumentIntake.Infrastructure.Persistence;
using System.Data;

namespace LexisNexis.DocumentIntake_Api.Middleware
{
    /// <summary>
    /// Central exception handler for all unhandled exceptions.
    ///
    /// Every request gets a server-generated TransactionId (never trusted from caller).
    /// This ID is:
    /// - Added to all log entries via a logging scope
    /// - Returned in the response header X-Transaction-Id
    /// - Returned in the response body so the caller can report it for investigation
    ///
    /// When an error occurs in production, the operations team can grep CloudWatch
    /// logs by TransactionId to see the full request lifecycle instantly.
    /// </summary>
    public class ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IMetricsService metrics)
    {
        public async Task InvokeAsync(HttpContext ctx)
        {
            // Generate server-side transaction ID — NEVER trust one from the caller
            var transactionId = Guid.NewGuid().ToString("N");

            ctx.Items["TransactionId"] = transactionId;
            ctx.Response.Headers["X-Transaction-Id"] = transactionId;

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["TransactionId"] = transactionId,
                ["ClientIp"] = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                ["Method"] = ctx.Request.Method,
                ["Path"] = ctx.Request.Path.ToString()
            });

            try
            {
                await next(ctx);
            }
            catch (ValidationException ex)
            {
                logger.LogWarning(
                    "[{TransactionId}] Validation failed on {Path}: {Errors}",
                    transactionId,
                    ctx.Request.Path,
                    string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));

                ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse
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

                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse
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
                    transactionId, ctx.Request.Method, ctx.Request.Path);

                await metrics.IncrementAsync("UnhandledExceptions");

                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse
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
