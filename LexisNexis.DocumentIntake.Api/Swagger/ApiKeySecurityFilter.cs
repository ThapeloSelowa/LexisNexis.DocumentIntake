using Microsoft.OpenApi.Models;
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
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "API key — in development use: dev-api-key-change-in-prod"
        };

        var securityRef = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "ApiKey"
            }
        };

        var requirement = new OpenApiSecurityRequirement { { securityRef, [] } };

        document.SecurityRequirements ??= new List<OpenApiSecurityRequirement>();
        document.SecurityRequirements.Add(requirement);

        foreach (var path in document.Paths.Values)
            foreach (var operation in path.Operations.Values)
            {
                operation.Security ??= new List<OpenApiSecurityRequirement>();
                operation.Security.Add(requirement);
            }
    }
}
