using LexisNexis.DocumentIntake.BusinessLogic.Models;
namespace LexisNexis.DocumentIntake_Api.Middleware
{
    /// <summary>
    /// Validates the X-Api-Key header on all requests.
    /// Returns 401 if the key is missing or incorrect.
    /// The TransactionId is still generated and returned even on auth failures — for traceability.
    /// Excluded paths: /health, /swagger (so monitoring and docs work without a key).
    /// </summary>
    public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        private const string ApiKeyHeader = "X-Api-Key";
        private static readonly string[] ExcludedPaths = ["/health", "/swagger", "/favicon.ico"];

        public async Task InvokeAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value ?? string.Empty;

            if (ExcludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await next(ctx);
                return;
            }

            if (!ctx.Request.Headers.TryGetValue(ApiKeyHeader, out var receivedKey)
                || receivedKey != config["Security:ApiKey"])
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    TransactionId = Guid.NewGuid().ToString("N"),
                    Status = 401,
                    Title = "Unauthorised",
                    Detail = $"A valid '{ApiKeyHeader}' header is required."
                });
                return;
            }

            await next(ctx);
        }
    }
}
