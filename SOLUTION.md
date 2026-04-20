# Solution Design & Trade-offs

## Overview

The service is a document intake pipeline. Upstream providers POST a file with metadata; the API stores it in S3, enqueues a processing message, and a background worker generates a text preview asynchronously. The caller gets an immediate acknowledgement and polls for the result.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        HTTP Layer                           │
│  ExceptionMiddleware → ApiKeyMiddleware → Idempotency       │
│  → RateLimiter → Minimal API Endpoints                      │
└────────────────────────────┬────────────────────────────────┘
                             │ MediatR
┌────────────────────────────▼────────────────────────────────┐
│                     Business Logic                          │
│  SubmitDocumentCommandHandler                               │
│  Logging → Validation → Audit pipeline behaviours          │
│  Document aggregate + ProcessingStatus state machine        │
└──────────┬─────────────────────────────┬────────────────────┘
           │                             │
    ┌──────▼──────┐               ┌──────▼──────┐
    │  S3 Storage │               │    Queue    │
    │ (LocalStack │               │ (In-Memory/ │
    │  in dev)    │               │    SQS)     │
    └─────────────┘               └──────┬──────┘
                                         │
                          ┌──────────────▼──────────────┐
                          │   DocumentProcessingWorker  │
                          │   Downloads from S3         │
                          │   Extracts text preview     │
                          │   Retries up to 3x          │
                          │   Dead-letter on exhaustion │
                          └─────────────────────────────┘
```

---

## Key Design Decisions

### 1. CQRS via MediatR

Commands (`SubmitDocumentCommand`) and queries (`GetDocumentQuery`) are separate concerns dispatched through MediatR. This keeps handlers small and focused, and makes it straightforward to add cross-cutting pipeline behaviours (logging, validation, audit) without touching handler code.

**Trade-off:** Adds indirection. For a service this size, a direct service class would have been simpler. MediatR pays off as the command surface grows.

### 2. Asynchronous Processing

The HTTP handler stores the file and enqueues a message — it does not wait for preview generation. The caller gets a `201` in milliseconds and polls for the result.

**Trade-off:** Requires clients to poll. A webhook callback or Server-Sent Events would be friendlier but adds complexity (retry delivery, subscriber management).

### 3. Document State Machine

`ProcessingStatus` has six states with an explicit allowed-transitions map:

```
Received → Stored → Queued → Processing → Processed
```

Every transition is validated before it happens. Invalid transitions throw rather than silently corrupt state.

**Trade-off:** Rigid. Adding a new state requires updating the transitions map. The benefit is that bugs (e.g. a worker accidentally marking a document `Processed` before it was `Queued`) are caught immediately.

### 4. Deduplication by DedupKey

The combination of `provider + sourceDocumentId` is treated as a natural key. Resubmitting the same document updates the existing record instead of creating a new one. The `SubmissionCount` and audit trail record each submission.

**Trade-off:** "Same document" is defined only by provider and source ID — two submissions with the same key but different files silently replace the previous file. This is intentional for the re-intake use case, but surprising if the caller expects two independent documents.

### 5. Optimistic Concurrency

`UpsertAsync` requires the caller to pass the current `Version`. If it does not match the stored version, the write is rejected. This prevents a race where two workers concurrently process the same document and both commit their result.

**Trade-off:** The caller must read before writing. A simple last-write-wins approach would need no version tracking, but would allow silent data loss under concurrent updates.

### 6. In-Memory Persistence

`InMemoryDocumentRepository` uses a `ConcurrentDictionary`. All data is lost on restart.

**Trade-off:** Zero infrastructure overhead for local development and easy to reason about in tests. In production, this must be replaced with a durable store (DynamoDB is the natural fit given the existing AWS infrastructure). The `IDocumentRepository` interface makes the swap a one-line change in `Program.cs`.

### 7. In-Memory Queue (Dev) vs SQS (Production)

The same `IQueueService` interface is backed by a `System.Threading.Channels` bounded channel locally and SQS in production. Switching is controlled by `ASPNETCORE_ENVIRONMENT`.

**Trade-off:** The in-memory queue does not survive restarts and has no visibility (no AWS console). For local dev this is acceptable. SQS adds dead-letter queue support and visibility timeouts natively in production.

### 8. Worker Retry Strategy

On failure the worker re-enqueues the message with an incremented `RetryCount`. After `MaxRetryAttempts` (default 3), the document is marked `Failed` and the message goes to the in-memory dead-letter store.

**Trade-off:** There is no backoff delay between retries — the re-queued message is processed again almost immediately. For transient network errors a short exponential backoff would be safer. Not added to keep the implementation simple; Polly at the S3 layer already handles transient S3 errors.

### 9. Idempotency Middleware

The `Idempotency-Key` header is optional. When provided, the first `2xx` response is cached for 24 hours. Subsequent requests with the same key return the cached response without re-running any business logic.

**Trade-off:** The cache is in-memory, so idempotency guarantees are lost on restart. A distributed cache (Redis, DynamoDB) would be needed for production-grade guarantees. The 24-hour TTL is enforced on read — there is no background eviction sweep, so stale entries are only cleaned up when the key is looked up again.

### 10. Text Preview Extraction

Different libraries are used per content type:

| Format | Library |
|---|---|
| PDF | PdfPig (pure .NET, no native deps) |
| DOCX | DocumentFormat.OpenXml (Microsoft, 349M downloads) |
| XLSX | DocumentFormat.OpenXml |
| Text / JSON / XML / CSV | Built-in `StreamReader` |

**Trade-off:** Adds two packages to the Infrastructure project. The alternative (byte scanning for printable ASCII runs) was already in place and fast but produced unusable output for compressed PDF streams and structured Office documents.

### 11. Magic Bytes Validation

`FileContentValidator` checks the actual file header bytes against the declared `contentType` before the file is stored. A `.exe` renamed to `.pdf` is rejected with `422 Unprocessable Entity`.

**Trade-off:** Only covers the most common types. A full MIME sniffing library would cover more formats but adds a dependency. The current coverage (PDF, plain text, Word) matches the accepted content types.

### 12. Rate Limiting

Fixed-window rate limiter — 200 requests per minute per IP address in development, configurable via `appsettings.json`. Rejection returns a structured `429` JSON response with a `TransactionId` for traceability.

**Trade-off:** Fixed window allows bursting at the window boundary (up to 2× the limit across the boundary). A sliding window or token bucket would be smoother but is harder to reason about. For this use case fixed window is sufficient.

### 13. Reporting

The `GET /api/v1/reports` endpoint loads all documents into memory, applies an optional date filter, and aggregates client-side. This is simple but does not scale to millions of documents.

**Trade-off:** For a production system with large volumes, aggregation should happen at the database level. With DynamoDB that would require a separate GSI or a pre-aggregated stats table updated on each write. The current design is appropriate while the store is in-memory.

---

## What Would Change for Production

| Concern | Dev (current) | Production |
|---|---|---|
| Persistence | In-memory dictionary | DynamoDB |
| Queue | In-memory channel | AWS SQS |
| Idempotency store | In-memory dictionary | DynamoDB / ElastiCache |
| Metrics | No-op | AWS CloudWatch |
| Secrets | `appsettings.Development.json` | AWS Secrets Manager / env vars |
| S3 | LocalStack | AWS S3 |
| Dead letters | In-memory list | SQS Dead Letter Queue |
| Retry backoff | None (immediate re-queue) | Exponential backoff with jitter |
| Reporting aggregation | In-memory LINQ | Database-level aggregation |
