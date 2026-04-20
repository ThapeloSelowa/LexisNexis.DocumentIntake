using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LexisNexis.DocumentIntake_Api.Swagger;

/// <summary>
/// Adds an optional Idempotency-Key header input to all POST operations in Swagger UI.
/// </summary>
public class IdempotencyHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = false,
            Schema = new OpenApiSchema { Type = "string", Format = "uuid" },
            Description = "Optional. Generate a UUID and reuse it on retries — " +
                          "the server replays the original response without reprocessing. " +
                          "Cached for 24 hours. Response will include X-Idempotent-Replayed: true on a replay."
        });
    }
}
