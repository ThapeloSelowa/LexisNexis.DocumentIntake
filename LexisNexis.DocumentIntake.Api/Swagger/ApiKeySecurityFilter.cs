using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LexisNexis.DocumentIntake_Api.Swagger;

/// <summary>
/// Registers the X-Api-Key security scheme and stamps it on every operation so
/// Swagger UI sends the header automatically after the user clicks Authorize.
/// </summary>
public class ApiKeySecurityFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "API key — in development use: dev-api-key-change-in-prod"
        };

        // OpenApiSecuritySchemeReference requires the document so the reference can resolve.
        // Without it the serializer emits {} instead of {"ApiKey": []}.
        var apiKeyRef = new OpenApiSecuritySchemeReference("ApiKey", document);
        var requirement = new OpenApiSecurityRequirement { { apiKeyRef, [] } };

        // Global security — Swagger UI sends X-Api-Key on every request once Authorized
        document.Security ??= [];
        document.Security.Add(requirement);

        // Per-operation security — belt-and-suspenders for Swagger UI compatibility
        foreach (var path in document.Paths.Values)
            foreach (var operation in path.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("ApiKey", document), [] }
                });
            }
    }
}
