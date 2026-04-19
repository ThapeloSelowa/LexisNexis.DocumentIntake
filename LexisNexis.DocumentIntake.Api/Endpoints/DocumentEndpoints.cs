using LexisNexis.DocumentIntake.BusinessLogic.Commands;
using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using LexisNexis.DocumentIntake.BusinessLogic.Queries;
using LexisNexis.DocumentIntake.Reporting;
using LexisNexis.DocumentIntake_Api.Validation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.ComponentModel;

namespace LexisNexis.DocumentIntake_Api.Endpoints
{
    /// <summary>
    /// Minimal API route definitions.
    /// Each endpoint is fully annotated for Swagger:
    /// - Summary and description
    /// - Expected request body
    /// - All possible response types and status codes
    /// - Tags for grouping in Swagger UI
    /// </summary>
    public static class DocumentEndpoints
    {
        public static void MapDocumentEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/v1/documents")
                .WithTags("Documents")
                .RequireRateLimiting("PerIpPolicy");

            // POST /api/v1/documents 
            group.MapPost("/", SubmitDocumentAsync)
                .WithName("SubmitDocument")
                .WithSummary("Submit a document for intake and processing")
                .WithDescription("""
                Accepts a multipart form upload with document metadata and file content.

                **Deduplication:** If a document with the same `provider` + `sourceDocumentId`
                already exists, the existing record is updated and resubmission is noted
                in the audit trail. A new record is NOT created.

                **Background Processing:** A preview/summary is generated asynchronously.
                Poll `/api/v1/documents/{id}/status` to check when processing is complete.

                **Idempotency:** Include an `Idempotency-Key` header to safely retry
                on network timeout without risk of duplicate processing.
                """)
                .Accepts<SubmitDocumentCommandRequest>("multipart/form-data")
                .Produces<SubmitDocumentResponseDto>(StatusCodes.Status201Created)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
                .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)
                .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests)
                .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

            // GET /api/v1/documents/{id}
            group.MapGet("/{id}", GetDocumentAsync)
                .WithName("GetDocument")
                .WithSummary("Retrieve document metadata and audit trail")
                .Produces<DocumentDto>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
                .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

            // GET /api/v1/documents/{id}/status
            group.MapGet("/{id}/status", GetStatusAsync)
                .WithName("GetDocumentStatus")
                .WithSummary("Get processing status and generated preview")
                .WithDescription("""
                Returns the current processing status and the generated preview/summary
                once processing is complete.

                **Status values:** Received → Stored → Queued → Processing → Processed | Failed
                """)
                .Produces<DocumentStatusDto>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
                .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

            // GET /api/v1/documents/{id}/content 
            group.MapGet("/{id}/content", DownloadContentAsync)
                .WithName("DownloadDocumentContent")
                .WithSummary("Download the raw document file")
                .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
                .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

            // GET /api/v1/documents 
            group.MapGet("/", ListDocumentsAsync)
                .WithName("ListDocuments")
                .WithSummary("List documents with optional filters")
                .Produces<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

            var reportGroup = app.MapGroup("/api/v1/reports")
    .WithTags("Reports")
    .RequireRateLimiting("PerIpPolicy");

            reportGroup.MapGet("/", async (
                [FromQuery] DateTimeOffset? from,
                [FromQuery] DateTimeOffset? to,
                ReportingService reporting,
                CancellationToken ct) =>
            {
                var report = await reporting.GenerateAsync(from, to, ct);
                return Results.Ok(report);
            })
            .WithName("GetDocumentReport")
            .WithSummary("Generate a business report on document intake activity")
            .Produces<DocumentReport>(StatusCodes.Status200OK);
        }

        // Handlers 
        private static async Task<IResult> SubmitDocumentAsync(HttpRequest httpRequest,IMediator mediator,
                                           FileContentValidator contentValidator,CancellationToken ct)
        {
            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");

            if (file is null)
                return Results.BadRequest(new ErrorResponse
                {
                    TransactionId = httpRequest.HttpContext.Items["TransactionId"]?.ToString()
                        ?? Guid.NewGuid().ToString("N"),
                    Status = 400,
                    Title = "Bad Request",
                    Detail = "No file was included in the request."
                });

            var contentType = form["contentType"].ToString();

            // Magic bytes validation — BEFORE the file is stored
            if (!contentValidator.IsContentTypeValid(file, contentType))
                return Results.UnprocessableEntity(new ErrorResponse
                {
                    TransactionId = httpRequest.HttpContext.Items["TransactionId"]?.ToString()
                        ?? Guid.NewGuid().ToString("N"),
                    Status = 422,
                    Title = "Invalid File Content",
                    Detail = $"The file content does not match the declared contentType '{contentType}'."
                });

            var command = new SubmitDocumentCommandRequest
            {
                SourceDocumentId = form["sourceDocumentId"].ToString(),
                Provider = form["provider"].ToString(),
                Title = form["title"].ToString(),
                Jurisdiction = form["jurisdiction"].ToString(),
                Tags = form["tags"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                ContentType = contentType,
                FileName = file.FileName,
                FileContent = file.OpenReadStream(),
                CorrelationId = httpRequest.HttpContext.Items["TransactionId"]?.ToString()
            };

            var result = await mediator.Send(command, ct);

            return Results.Created($"/api/v1/documents/{result.DocumentId}", new SubmitDocumentResponseDto(
                result.DocumentId.ToString(),
                result.IsResubmission,
                result.TransactionId,
                "Document accepted. Processing has been queued."));
        }

        private static async Task<IResult> GetDocumentAsync(string id, IMediator mediator, CancellationToken ct)
        {
            var result = await mediator.Send(new GetDocumentQuery(DocumentId.Parse(id)), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        private static async Task<IResult> GetStatusAsync(string id, IMediator mediator, CancellationToken ct)
        {
            var result = await mediator.Send(new GetDocumentStatusQuery(DocumentId.Parse(id)), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }

        private static async Task<IResult> DownloadContentAsync(string id, IMediator mediator, IStorageService storage,
                                           IDocumentRepository repo, CancellationToken ct)
        {
            var doc = await repo.FindByIdAsync(DocumentId.Parse(id), ct);
            if (doc is null || doc.StorageKey is null) return Results.NotFound();

            var stream = await storage.DownloadAsync(doc.StorageKey.Value, ct);

            return Results.File(
                stream,
                contentType: doc.ContentType,
                fileDownloadName: doc.FileName,
                lastModified: doc.UpdatedAt,
                entityTag: new EntityTagHeaderValue($"\"{doc.ETag}\""));
        }

        private static async Task<IResult> ListDocumentsAsync([FromQuery] string? provider,[FromQuery] string? tag,
                                           [FromQuery] string? status,[FromQuery] int page = 1,
                                           [FromQuery] int pageSize = 20,IDocumentRepository repo = null!,
                                           CancellationToken ct = default)
        {
            ProcessingStatus? parsedStatus = Enum.TryParse<ProcessingStatus>(status, out var s) ? s : null;

            var docs = await repo.QueryAsync(
                new DocumentQueryFilter(provider, tag, parsedStatus, page, pageSize), ct);

            return Results.Ok(docs);
        }
    }

    // Response DTOs for Swagger documentation
    public record SubmitDocumentResponseDto(
        
    [property: Description("Internal document ID assigned by this service")]
    string DocumentId,

    [property: Description("True if this is a resubmission of an existing document")]
    bool IsResubmission,

    [property: Description("Use this ID to trace this request in logs")]
    string TransactionId,

    [property: Description("Human-readable message about the intake result")]
    string Message);
}
