using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;

namespace LexisNexis.DocumentIntake_Api.Middleware
{
    /// <summary>
    /// Prevents duplicate processing of identical POST requests.
    ///
    /// Problem it solves: If an upstream provider sends a request, the network times out,
    /// and they retry — without idempotency the document would be submitted twice even
    /// though the deduplication key would catch it. With idempotency, we return the
    /// exact same response as the first call without re-running any logic.
    ///
    /// The caller must send an "Idempotency-Key" header (a UUID they generate).
    /// Responses are cached for 24 hours.
    /// </summary>
    public class IdempotencyMiddleware(RequestDelegate next,IIdempotencyStore store)
    {
        public async Task InvokeAsync(HttpContext ctx)
        {
            if (!ctx.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await next(ctx); return;
            }

            if (!ctx.Request.Headers.TryGetValue("Idempotency-Key", out var key))
            {
                await next(ctx); return;
            }

            var cached = await store.GetAsync(key!);
            if (cached is not null)
            {
                ctx.Response.StatusCode = cached.StatusCode;
                ctx.Response.Headers["X-Idempotent-Replayed"] = "true";
                await ctx.Response.WriteAsJsonAsync(cached.Body);
                return;
            }

            // Capture the response so we can cache it
            var originalBody = ctx.Response.Body;
            await using var buffer = new MemoryStream();
            ctx.Response.Body = buffer;
            var responseBody = string.Empty;

            try
            {
                await next(ctx);
            }
            finally
            {
                // Always restore the original body stream, even when an exception propagates.
                // Without this, ExceptionMiddleware writes the error JSON to the buffer
                // which is never flushed to the client, resulting in an empty 500 body.
                buffer.Seek(0, SeekOrigin.Begin);
                responseBody = await new StreamReader(buffer).ReadToEndAsync();
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
                ctx.Response.Body = originalBody;
            }

            if (ctx.Response.StatusCode is >= 200 and < 300)
            {
                await store.SetAsync(key!, new IdempotencyEntry(
                    ctx.Response.StatusCode,
                    responseBody,
                    DateTimeOffset.UtcNow));
            }
        }
    }
}
