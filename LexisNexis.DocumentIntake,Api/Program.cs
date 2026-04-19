using HealthChecks.UI.Client;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using LexisNexis.DocumentIntake_Api.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Formatting.Compact;
using System.Threading.RateLimiting;
using AWS.Logger.Core;
using AWS.Logger.SeriLog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "DocumentIntakeService")
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithMachineName()
       .Enrich.WithProperty("Application", "DocumentIntakeService")
       .WriteTo.Console(new RenderedCompactJsonFormatter()); // Local

    // Only add CloudWatch in non-Development environments
    if (!ctx.HostingEnvironment.IsDevelopment())
    {
        cfg.WriteTo.AWSSeriLog(new AWS.Logger.AWSLoggerConfig
        {
            LogGroup = "/document-intake/api",
            LogStreamNamePrefix = $"{Environment.MachineName}/{DateTime.UtcNow:yyyy-MM-dd}"
        }, textFormatter: new RenderedCompactJsonFormatter());
    }
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(options =>
{
    var permitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 30);
    var windowMinutes = builder.Configuration.GetValue("RateLimiting:WindowMinutes", 1);

    // Per-IP fixed window — 30 requests per minute per IP address
    options.AddPolicy("PerIpPolicy", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Return a consistent JSON error with TransactionId on 429
    options.OnRejected = async (rejectedCtx, ct) =>
    {
        var transactionId = Guid.NewGuid().ToString("N");
        rejectedCtx.HttpContext.Response.Headers["X-Transaction-Id"] = transactionId;
        rejectedCtx.HttpContext.Response.StatusCode = 429;

        await rejectedCtx.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse
        {
            TransactionId = transactionId,
            Status = 429,
            Title = "Too Many Requests",
            Detail = $"Rate limit of {permitLimit} requests per {windowMinutes} minute(s) exceeded. Please slow down."
        }, ct);

        // Emit metric so CloudWatch can alarm on rate limit spikes
        var metricsService = rejectedCtx.HttpContext.RequestServices
            .GetRequiredService<IMetricsService>();
        await metricsService.IncrementAsync("RateLimitHit", ct);
    };
});

builder.Services.AddHealthChecks()
    .AddCheck<S3HealthCheck>("s3-storage", tags: ["ready"])
    .AddCheck<QueueHealthCheck>("queue", tags: ["ready"])
    .AddCheck("self", () => HealthCheckResult.Healthy("Service is running."), tags: ["live"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Document Intake Service API",
        Version = "v1",
        Description = """
            LexisNexis Document Intake and Processing Service.

            ## Authentication
            All endpoints (except /health and /swagger) require an API key passed
            in the `X-Api-Key` header.

            ## Rate Limiting
            30 requests per minute per IP address. Exceeding this returns HTTP 429.

            ## Error Handling
            All errors return a consistent `ErrorResponse` object containing a
            `TransactionId`. Provide this ID when reporting issues to the development team.

            ## Deduplication
            Documents are deduplicated by `provider` + `sourceDocumentId`.
            Resubmissions update the existing record and are noted in the audit trail.
            """,
        Contact = new OpenApiContact
        {
            Name = "Platform Team",
            Email = "platform@lexisnexis.com"
        }
    });

    // Add API key security definition
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API key required to access this service. Contact the platform team to obtain one."
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("ApiKey"),
            new List<string>()
        }
    });

    // Include XML comments from all projects for rich Swagger documentation
    var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml");
    foreach (var xmlFile in xmlFiles)
        c.IncludeXmlComments(xmlFile);
});

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
