# LexisNexis Document Intake Service

An ASP.NET Core 8 REST API for ingesting, storing, and processing documents from upstream providers.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 9.x | Required to build `.slnx` solution format |
| Docker Desktop | any recent | Required for LocalStack (local S3) |
| PowerShell | 5.1+ or pwsh 7+ | Required for `run.ps1` |

---

## Running Locally

The easiest way is via `run.ps1` at the repo root. It handles Docker, LocalStack, and the API together.

```powershell
# Start LocalStack + API (foreground — Ctrl+C to stop)
.\run.ps1 run

# Start in the background
.\run.ps1 run-detached

# Stop everything
.\run.ps1 stop
```

Once running, open Swagger UI at: **http://localhost:5000/swagger**

### Running from Visual Studio

1. Make sure Docker Desktop is running
2. Start LocalStack:
   ```powershell
   docker compose up localstack -d
   ```
3. Set `LexisNexis.DocumentIntake.Api` as the startup project
4. Press **F5**

Swagger will open at **http://localhost:5161/swagger**

### Running from the CLI

```bash
docker compose up localstack -d
dotnet run --project LexisNexis.DocumentIntake.Api --urls http://localhost:5161
```

---

## All run.ps1 Commands

```powershell
.\run.ps1 run           # Start full stack (LocalStack + API) — foreground
.\run.ps1 run-detached  # Start full stack — background
.\run.ps1 stop          # Stop all containers
.\run.ps1 build         # dotnet build --configuration Release
.\run.ps1 test          # dotnet test (all projects)
.\run.ps1 logs          # Tail API container logs
.\run.ps1 clean         # Remove containers, volumes, bin/ and obj/ folders
.\run.ps1 help          # Show usage
```

---

## Authentication

All endpoints require an `X-Api-Key` header.

**Development key:** `dev-api-key-change-in-prod`

In Swagger UI click **Authorize** and enter the key once — it will be sent on all subsequent requests.

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/v1/documents` | Submit a document for intake and processing |
| `GET` | `/api/v1/documents` | List documents (filter by `provider`, `tag`, `status`) |
| `GET` | `/api/v1/documents/{id}` | Get document metadata and audit trail |
| `GET` | `/api/v1/documents/{id}/status` | Get processing status and generated preview |
| `GET` | `/api/v1/documents/{id}/content` | Download the raw document file |
| `GET` | `/api/v1/reports` | Business report (optional `?from=` / `?to=` date filters) |
| `GET` | `/health/live` | Liveness probe |
| `GET` | `/health/ready` | Readiness probe (checks S3 and queue) |

### Submitting a Document

```bash
curl -X POST http://localhost:5161/api/v1/documents \
  -H "X-Api-Key: dev-api-key-change-in-prod" \
  -F "sourceDocumentId=doc-001" \
  -F "provider=MyProvider" \
  -F "title=My Document" \
  -F "contentType=application/pdf" \
  -F "jurisdiction=ZA" \
  -F "tags=legal,2026" \
  -F "file=@/path/to/document.pdf;type=application/pdf"
```

Returns `201 Created`:
```json
{
  "documentId": "...",
  "isResubmission": false,
  "transactionId": "...",
  "message": "Document accepted. Processing has been queued."
}
```

Processing is asynchronous. Poll `/api/v1/documents/{id}/status` until `status` is `"Processed"` or `"Failed"`.

### Idempotent Retries

Include an `Idempotency-Key` header to safely retry without creating duplicates:

```bash
curl -X POST http://localhost:5161/api/v1/documents \
  -H "X-Api-Key: dev-api-key-change-in-prod" \
  -H "Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000" \
  -F ...
```

A retry with the same key returns the original response with `X-Idempotent-Replayed: true`. Keys are cached for 24 hours.

---

## Document Status Flow

```
Received → Stored → Queued → Processing → Processed
                                 ↓
                           (retry, up to 3x)
                                 ↓
                               Failed
```

---

## Running Tests

```powershell
.\run.ps1 test
```

Or directly:

```bash
dotnet test --configuration Release
```

---

## Configuration Reference

| Key | Default (dev) | Description |
|---|---|---|
| `Security:ApiKey` | `dev-api-key-change-in-prod` | API authentication key |
| `AWS:ServiceURL` | `http://localhost:4566` | S3 endpoint (LocalStack in dev) |
| `AWS:BucketName` | `documents-local` | S3 bucket name |
| `AWS:ForcePathStyle` | `true` | Required for LocalStack |
| `RateLimiting:PermitLimit` | `200` | Max requests per window per IP |
| `RateLimiting:WindowMinutes` | `1` | Rate limit window duration |
| `Processing:MaxPreviewLength` | `500` | Max preview text length (chars) |
| `Processing:WorkerDelayMs` | `500` | Queue polling interval (ms) |
| `Processing:MaxRetryAttempts` | `3` | Worker retry attempts before dead letter |

In production, override via environment variables using double-underscore notation:
`Security__ApiKey`, `AWS__BucketName`, etc.

---

## Project Structure

```
LexisNexis.DocumentIntake/
├── LexisNexis.DocumentIntake.Api/           # HTTP layer — endpoints, middleware, health checks, Swagger
├── LexisNexis.DocumentIntake.BusinessLogic/ # Domain — commands, queries, validation, state machine
├── LexisNexis.DocumentIntake.Infrastructure/# S3 storage, queue, background worker, metrics, persistence
├── LexisNexis.DocumentIntake.Reporting/     # Business report generation
├── LexisNexis.DocumentIntake.Tests/         # Unit tests
├── docker-compose.yml                       # LocalStack + API containers
└── run.ps1                                  # Developer task runner
```
