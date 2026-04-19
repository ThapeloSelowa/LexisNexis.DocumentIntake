using Amazon.Runtime;
using Amazon.S3;
using AWS.Logger.Core;
using AWS.Logger.SeriLog;
using FluentValidation;
using HealthChecks.UI.Client;
using LexisNexis.DocumentIntake.BusinessLogic.Behaviours;
using LexisNexis.DocumentIntake.BusinessLogic.Commands;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using LexisNexis.DocumentIntake.Infrastructure.Audit;
using LexisNexis.DocumentIntake.Infrastructure.Metrics;
using LexisNexis.DocumentIntake.Infrastructure.Persistence;
using LexisNexis.DocumentIntake.Infrastructure.Queue;
using LexisNexis.DocumentIntake.Infrastructure.Startup;
using LexisNexis.DocumentIntake.Infrastructure.Storage;
using LexisNexis.DocumentIntake.Infrastructure.Workers;
using LexisNexis.DocumentIntake.Reporting;
using LexisNexis.DocumentIntake_Api.Endpoints;
using LexisNexis.DocumentIntake_Api.HealthChecks;
using LexisNexis.DocumentIntake_Api.Middleware;
using LexisNexis.DocumentIntake_Api.Validation;
using MediatR;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using System.Threading.RateLimiting;
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Auto-start LocalStack in Development
    if (builder.Environment.IsDevelopment())
    {
        await LexisNexis.DocumentIntake.Infrastructure.Startup.LocalStackBootstrapper
            .EnsureRunningAsync();
    }

    // Serilog 
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.WithMachineName()
           .Enrich.WithProperty("Application", "DocumentIntakeService")
           .WriteTo.Console(new RenderedCompactJsonFormatter());
    });

    // AWS SDK
    builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

    var awsServiceUrl = builder.Configuration["AWS:ServiceURL"];
    var forcePathStyle = builder.Configuration.GetValue("AWS:ForcePathStyle", false);

    builder.Services.AddSingleton<IAmazonS3>(_ =>
    {
        var config = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.USEast1
        };
        if (!string.IsNullOrEmpty(awsServiceUrl))
        {
            config.ServiceURL = awsServiceUrl;    // LocalStack
            config.ForcePathStyle = forcePathStyle;
        }
        return new AmazonS3Client(
            new BasicAWSCredentials("test", "test"), config);
    });

    // Application services 
    builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
    builder.Services.AddSingleton<IAuditService, AuditService>();
    builder.Services.AddSingleton<DeadLetterService>();
    builder.Services.AddSingleton<InMemoryIdempotencyStore>();
    builder.Services.AddSingleton<IIdempotencyStore>(sp =>
        sp.GetRequiredService<InMemoryIdempotencyStore>());
    builder.Services.AddSingleton<FileContentValidator>();
    builder.Services.AddScoped<ReportingService>();

    // Storage — use S3 (pointing to LocalStack in dev)
    builder.Services.AddSingleton<IStorageService, S3StorageService>();

    // Queue — in-memory for local, SQS for production
    if (builder.Environment.IsDevelopment())
        builder.Services.AddSingleton<IQueueService, InMemoryQueueService>();
    else
        builder.Services.AddSingleton<IQueueService, SqsQueueService>();

    // Metrics — no-op console locally, CloudWatch in production
    if (builder.Environment.IsDevelopment())
        builder.Services.AddSingleton<IMetricsService, NoOpMetricsService>();
    else
        builder.Services.AddSingleton<IMetricsService, CloudWatchMetricsService>();

    // MediatR + FluentValidation
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(SubmitDocumentCommandRequest).Assembly);
        // Pipeline order matters — Logging → Validation → Audit → Handler
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditBehaviour<,>));
    });

    builder.Services.AddValidatorsFromAssembly(typeof(SubmitDocumentCommandValidator).Assembly);
    // Background worker
    builder.Services.AddHostedService<DocumentProcessingWorker>();

    // S3 bucket self-healing on startup
    builder.Services.AddHostedService<S3BucketInitialiser>();

    // Rate limiting
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

    // Health checks
    builder.Services.AddHealthChecks()
        .AddCheck<S3HealthCheck>("s3-storage", tags: ["ready"])
        .AddCheck<QueueHealthCheck>("queue", tags: ["ready"])
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "Document Intake API",
            Version = "v1",
            Description = "LexisNexis Document Intake & Processing Service"
        });

        options.DocumentFilter<LexisNexis.DocumentIntake_Api.Swagger.ApiKeySecurityFilter>();
    });

    // OpenTelemetry
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation()
                   .AddSource("DocumentIntake");

            // Only export traces to console in Development — suppresses the noisy
            // S3/LocalStack connection-refused traces when Docker isn't running
            if (!builder.Environment.IsDevelopment())
                tracing.AddHttpClientInstrumentation()
                       .AddConsoleExporter();
        });

    var app = builder.Build();

    // Middleware pipeline (ORDER MATTERS)
    app.UseSerilogRequestLogging(opts =>
    {
        opts.EnrichDiagnosticContext = (diag, ctx) =>
        {
            diag.Set("ClientIp", ctx.Connection.RemoteIpAddress?.ToString());
            diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
            diag.Set("RequestHost", ctx.Request.Host.ToString());
        };
    });

    app.UseMiddleware<ExceptionMiddleware>();   // 1. Catch all exceptions + TransactionId
    app.UseMiddleware<ApiKeyMiddleware>();      // 2. Authenticate
    app.UseMiddleware<IdempotencyMiddleware>(); // 3. Idempotency check
    app.UseRateLimiter();                      // 4. Rate limiting

    if (app.Environment.IsDevelopment())
    {
        // Patch swagger.json security: OpenApiSecuritySchemeReference serializes as {} in
        // Microsoft.OpenApi 2.x; replace every empty security object with {"ApiKey": []}
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == "/swagger/v1/swagger.json")
            {
                var originalBody = ctx.Response.Body;
                using var buffer = new MemoryStream();
                ctx.Response.Body = buffer;
                try { await next(ctx); } finally { ctx.Response.Body = originalBody; }

                buffer.Seek(0, SeekOrigin.Begin);
                var json = await new StreamReader(buffer).ReadToEndAsync();
                var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;

                System.Text.Json.Nodes.JsonObject MakeApiKeyReq() =>
                    new() { ["ApiKey"] = new System.Text.Json.Nodes.JsonArray() };

                void PatchSecurityArray(System.Text.Json.Nodes.JsonArray? arr)
                {
                    if (arr is null) return;
                    for (var i = 0; i < arr.Count; i++)
                        if (arr[i] is System.Text.Json.Nodes.JsonObject o && o.Count == 0)
                            arr[i] = MakeApiKeyReq();
                }

                PatchSecurityArray(node["security"]?.AsArray());

                if (node["paths"] is System.Text.Json.Nodes.JsonObject paths)
                    foreach (var path in paths)
                        if (path.Value is System.Text.Json.Nodes.JsonObject pathItem)
                            foreach (var op in pathItem)
                                if (op.Value is System.Text.Json.Nodes.JsonObject operation)
                                    PatchSecurityArray(operation["security"]?.AsArray());

                var bytes = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
                ctx.Response.ContentLength = bytes.Length;
                await ctx.Response.Body.WriteAsync(bytes);
            }
            else
            {
                await next(ctx);
            }
        });

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Document Intake API v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Route registration 
    app.MapDocumentEndpoints();

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live")
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("ready")
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}