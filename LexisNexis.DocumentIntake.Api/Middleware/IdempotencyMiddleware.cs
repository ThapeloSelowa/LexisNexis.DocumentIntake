using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;

namespace LexisNexis.DocumentIntake_Api.Middleware
{
    /// <summary>
    /// Prevents duplicate processing of identical POST requests.
    /// The caller must include an "Idempotency-Key" header (a UUID they generate).
    /// Cached responses are replayed for 24 hours; requests without the header pass through unchanged.
    /// </summary>
    public class IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store)
    {
        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (!httpContext.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || !httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var key))
            {
                await next(httpContext); return;
            }

            var cached = await store.GetAsync(key.ToString());
            if (cached is not null)
            {
                httpContext.Response.StatusCode = cached.StatusCode;
                httpContext.Response.Headers["X-Idempotent-Replayed"] = "true";
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(cached.Body);
                return;
            }

            var originalBody = httpContext.Response.Body;
            await using var buffer = new MemoryStream();
            httpContext.Response.Body = buffer;
            var responseBody = string.Empty;

            try
            {
                await next(httpContext);
            }
            finally
            {
                // Restore the original stream so ExceptionMiddleware can write error bodies correctly.
                buffer.Seek(0, SeekOrigin.Begin);
                responseBody = await new StreamReader(buffer).ReadToEndAsync();
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
                httpContext.Response.Body = originalBody;
            }

            if (httpContext.Response.StatusCode is >= 200 and < 300)
            {
                await store.SetAsync(key.ToString(), new IdempotencyEntry(
                httpContext.Response.StatusCode, responseBody, DateTimeOffset.UtcNow));
            }
        }
    }
}
