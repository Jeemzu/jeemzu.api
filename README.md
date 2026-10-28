# jeemzu.api

ASP.NET Core 8 Web API — backend for [jeemzu.me](https://jeemzu.me). Handles leaderboards, accounts, JWT auth, RAG profile chat, and native GDD/Budgetize AI workflows.

## Stack

| Concern          | Technology                                                           |
| ---------------- | -------------------------------------------------------------------- |
| Framework        | ASP.NET Core 8 (controller-based)                                    |
| ORM              | Entity Framework Core 8 (Npgsql)                                     |
| Database         | PostgreSQL 16 + pgvector (HNSW cosine index)                         |
| AI/LLM           | Semantic Kernel 1.77 — GPT-6 Luna + text-embedding-3-small          |
| Auth             | JWT Bearer (HMAC-SHA256, 60 min) + httpOnly refresh cookie (30 days) |
| Password hashing | BCrypt (work factor 12)                                              |
| API docs         | Swagger/OpenAPI (Swashbuckle)                                        |
| Containerization | Docker multi-stage (runtime: port 8080)                              |
| Hosting          | Render (Docker web service)                                          |
| Database (prod)  | Render Managed PostgreSQL 16 + pgvector                              |
| Database (dev)   | Docker Compose (`pgvector/pgvector:pg16`, port 5432)                 |
| CI/CD            | Render auto-deploy from `main` (see `render.yaml`)                   |

## Third-Party Services

| Service           | Responsibility                                                                                                                                                                                       |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Render            | Hosts the ASP.NET Core API at `api.jeemzu.me` and managed PostgreSQL, defined in `render.yaml`. |
| Render PostgreSQL | Production database for users, scores, refresh tokens, budgets, and knowledge chunks. The `vector` extension supports semantic search over knowledge embeddings.                                     |
| OpenAI            | Supplies completions and embeddings to .NET. Profile, GDD and Budgetize keys are configured independently. |
| Resend            | Email provider used by `POST /api/contact` and by account email verification / password reset when `Resend:ApiKey` is configured.                                                                    |
| GitHub Actions    | Dispatches `api-types-update` to the frontend repo so it regenerates OpenAPI types. Render handles building and deploying, so CI does not build images.                                              |

All AI workflows run inside the .NET API; the browser never receives provider keys.

### Local-Only Infrastructure

Docker Compose starts PostgreSQL for local development.

## API Endpoints

### GDD assistant - `/api/gdd-assistant`

- Authenticated, stateless checkpoint collaboration, separate from the profile RAG chatbot. Text and compressed image references go to OpenAI only on an explicit proposal request; URLs are context only, never fetched.
- Requests accept `mode: "scratch" | "fill"` (omitted means `scratch` for older clients). Scratch proposals replace section text and follow ordered checkpoints. Fill proposals use the existing GDD and blank subsection headings to suggest additions/revisions at any checkpoint; approval replaces matching subsection headings or appends new ones, preserving omitted content. Deploy this API update before the two-mode frontend; no migration is needed.
- `OpenAI:GddModel`, `OpenAI:ChatModel` and `OpenAI:BudgetModel` independently default to `gpt-6-luna`; Render uses double underscores instead of colons. User-secrets/environment overrides take precedence.
- GDD completions use the separate `OpenAI:GddApiKey` secret (`OpenAI__GddApiKey` in Render), allowing usage attribution by key. Missing/blank disables only GDD AI with `503`, without falling back to `OpenAI:ApiKey`. Set the local secret with `dotnet user-secrets set "OpenAI:GddApiKey" "<your-gdd-key>" --project src\JeemzuApi`; set the production value in Render, then restart/redeploy. Do not commit the key or expose it through `VITE_` variables.
- The assistant returns structured, checkpoint-scoped proposals; approval and resumable workflow state live in the editable ZIP. No database migration or provider-side conversation/file storage is added. The profile chatbot retains its RAG grounding with a 1,200-token output cap; GDD requests have a 10,000-token cap. GPT-6 models use `reasoning_effort: none` for both workflows.
- Deploy the API and update model overrides before deploying the assistant UI. Confirm the selected models are available to the deployment's OpenAI project. See [API.md](API.md#gdd-assistant---apigdd-assistant) and [gdd-assistant.http](gdd-assistant.http).
- Provider-wire and authenticated endpoint tests are offline and need no PostgreSQL or real API key:
  `dotnet test tests\JeemzuApi.Tests\JeemzuApi.Tests.csproj --configuration Release --filter FullyQualifiedName~GddAssistant`

### GDD library - `/api/gdd-documents`

- Authenticated users can store multiple named documents as an editable draft ZIP containing original images, with an optional current PDF. Draft-only saves need no generated PDF; omitting the PDF on update clears stale output (`pdfBytes: 0`, PDF download `404`). Binary data stays in PostgreSQL; this change needs no database migration or new storage service.
- The account quota is 100 MiB (104,857,600 bytes), counting PDF bytes plus compressed ZIP bytes. Uploads allow a 20 MiB PDF, 25 MiB ZIP, and 46 MiB multipart request. Saves reuse the `account-user` rate limit. Owner-scoped queries, per-account transactional locking, revision checks, and user-deletion cascade protect consistency.
- Deploy this API before the GDD account-library frontend. Startup applies the `AddGddDocuments` migration; no production migration has been run as part of implementation. Database backups must now include the document binaries.
- See [API.md](API.md#gdd-library---apigdd-documents) for routes and [gdd-documents.http](gdd-documents.http) for local requests.

#### Focused GDD tests

Use a disposable local PostgreSQL 16 database with pgvector; never use a production connection.

```powershell
docker run --rm -d --name jeemzu-gdd-tests -p 127.0.0.1:55439:5432 -e POSTGRES_DB=gdd_test -e POSTGRES_USER=gdd_test -e POSTGRES_PASSWORD=local-test-only pgvector/pgvector:pg16
# Wait for: docker exec jeemzu-gdd-tests pg_isready -U gdd_test
$env:GDD_TEST_CONNECTION = 'Host=127.0.0.1;Port=55439;Database=gdd_test;Username=gdd_test;Password=local-test-only'
dotnet test tests\JeemzuApi.Tests\JeemzuApi.Tests.csproj --configuration Release --filter 'FullyQualifiedName~Gdd'
docker stop jeemzu-gdd-tests
```

Tests apply real migrations and exercise multipart endpoints, stored bytes, ownership, revisions, concurrent quota enforcement, and cascade deletion. The test host uses a test-only identity handler; production continues to use JWT authentication.

Budgetize subscriptions are stored in the existing budget `bills` array. The optional
`isSubscription` boolean on each bill preserves its frontend section assignment;
omitted values default to `false`. Billing validation and storage are unchanged,
and no database migration is required.

Budget people also accept nullable `autopayLockedPerPaycheckCents` and
`essentialsLockedPerPaycheckCents`: nonnegative integer cents fixed every paycheck.
Null/omitted means proportional funding; zero locks the contribution at $0.
These fields round-trip in saved budget JSON without a database migration.
Earlier monthly-named fields are read as the same entered per-paycheck amounts;
explicit new fields (including null) take precedence, and responses use only the new names.
Deploy this contract before the frontend contribution-lock controls.

Budget snapshots also accept `personalExpenses` (up to 2,000 items), each with
`id`, `name`, `amountCents`, `dateISO`, `frequency`, nullable `endISO`, and nullable
`personId`. Frequencies are `once`, `weekly`, `biweekly`, `monthly`, `quarterly`,
and `annual`. A null person targets the combined personal spending plan;
otherwise it targets that person's account. The frontend computes occurrence-month
paycheck-remainder splits. Omitted collections default to empty for older snapshots.
Deploy this DTO before the personal-planning frontend so saves preserve the plans.
No database migration is needed.

### Auth — `/api/auth`

| Method | Path                | Auth | Description                                                           |
| ------ | ------------------- | ---- | --------------------------------------------------------------------- |
| `POST` | `/api/auth/refresh` | None | Reads httpOnly cookie → validates & rotates → returns `TokenResponse` |
| `POST` | `/api/auth/logout`  | None | Revokes token, deletes cookie → `204`                                 |

### Users — `/api/users`

| Method | Path                    | Auth   | Description                                                 |
| ------ | ----------------------- | ------ | ----------------------------------------------------------- |
| `POST` | `/api/users/register`   | None   | Creates account → `201 TokenResponse` (sets refresh cookie) |
| `POST` | `/api/users/login`      | None   | Authenticates → `TokenResponse` (sets refresh cookie)       |
| `POST` | `/api/users`            | Bearer | Updates `OptedIn` for authenticated user → `UserResponse`   |
| `GET`  | `/api/users/{username}` | None   | User profile + per-game high scores                         |

### Scores — `/api/scores`

| Method | Path                            | Auth     | Description                                                           |
| ------ | ------------------------------- | -------- | --------------------------------------------------------------------- |
| `POST` | `/api/scores`                   | Bearer   | Submit score (upserts — only updates if higher) → `201 ScoreResponse` |
| `GET`  | `/api/scores/{gameId}?limit=10` | None     | Top-N leaderboard (limit clamped 1–100)                               |
| `GET`  | `/api/scores/{gameId}/summary`  | Optional | All-time record + personal best (when authenticated)                  |

### Chat — `/api/chat`

| Method | Path        | Auth | Description                                                        |
| ------ | ----------- | ---- | ------------------------------------------------------------------ |
| `POST` | `/api/chat` | None | RAG chat — accepts `{ question, history[] }`, returns `{ answer }` |

### Admin — `/api/admin`

| Method | Path                          | Auth  | Description                                                          |
| ------ | ----------------------------- | ----- | -------------------------------------------------------------------- |
| `POST` | `/api/admin/knowledge/ingest` | Admin | Re-reads `about-me.json`, regenerates embeddings, upserts all chunks |
| `GET`  | `/api/admin/users`            | Admin | List all users                                                       |
| `PUT`  | `/api/admin/users/{id}/role`  | Admin | Update user role                                                     |

### Knowledge — `/api/knowledge`

| Method | Path                          | Auth | Description                                     |
| ------ | ----------------------------- | ---- | ----------------------------------------------- |
| `GET`  | `/api/knowledge/search?q=...` | None | Vector similarity search against knowledge base |

### Health

| Method | Path      | Description                   |
| ------ | --------- | ----------------------------- |
| `GET`  | `/health` | PostgreSQL connectivity check |

## Budgetize Assistant (.NET)

- `POST /api/budget/chat` remains authenticated and stateless: the browser sends its current unsaved budget and authoritative projection.
- Semantic Kernel runs router → analyst, planner or capability-gap reporter → deterministic proposal validation. Every request makes at most two model calls; all stages use `OpenAI:BudgetModel` (default `gpt-6-luna`).
- Set `OpenAI:BudgetApiKey` locally with user-secrets, or `OpenAI__BudgetApiKey` on Render's **jeemzu-api** service. Missing/blank returns `503` for Budgetize only; there is no fallback to another key.
- The last six conversation turns and weekly projection (without duplicated daily ledgers) are included. Context is capped at 150,000 characters, output at 500 routing / 2,000 analysis / 6,000 planning / 1,500 gap tokens.
- Only the existing 11 operations are supported. IDs, dates, recurrence, account ownership, cents and ranges are validated; rejected operations are explained. Nullable operation fields are omitted on the wire so partial updates cannot erase existing values.
- Nothing is applied server-side. The existing browser projection preview and explicit Apply action remain authoritative; capability gaps still use `POST /api/budget/gaps`.
- Limits: 12 requests/account/5 minutes, four concurrent workflows/process with no queue, 90-second deadline. Admin health reports local configuration only, not provider availability, without billed probes.
- No Python runtime, agent service, Tavily integration or database migration is needed. The profile chatbot remains the separate .NET RAG workflow.
- Offline focused checks: `dotnet test tests\JeemzuApi.Tests\JeemzuApi.Tests.csproj -c Release --filter FullyQualifiedName~BudgetAssistant`.
- Manual authenticated request: [budget-assistant.http](budget-assistant.http). Requesting a proposal makes billed provider calls but does not apply or save changes.

## Database Schema

### Entities

**User** — `Id`, `Username` (unique, max 50), `PasswordHash` (BCrypt), `Role` (`"User"` | `"Admin"`), `OptedIn`, `CreatedAt`, `UpdatedAt`

**Score** — `Id`, `GameId`, `Username`, `UserId` (nullable FK → User, on delete SetNull), `ScoreValue`, `Timestamp` (Unix ms), `CreatedAt`

- Index: `(GameId, ScoreValue)`
- Unique filtered: `(UserId, GameId) WHERE UserId IS NOT NULL`

**RefreshToken** — `Id`, `Token` (unique, opaque base64), `Username`, `ExpiresAt`, `IsRevoked`, `CreatedAt`

**KnowledgeChunk** — `Id`, `SourceKey` (unique, max 200), `Content`, `Embedding` (vector(1536), HNSW cosine index), `CreatedAt`, `UpdatedAt`

### Migrations

1. `InitialCreate` — Users, Scores
2. `AddRefreshTokens` — RefreshToken table
3. `AddUserAuthAndScoreFK` — Score.UserId FK
4. `AddUserRole` — User.Role column
5. `AddUniqueScorePerUserPerGame` — Unique score constraint
6. `AddKnowledgeChunks` — KnowledgeChunk + pgvector

Migrations auto-apply on startup via `db.Database.Migrate()`.

## Project Structure

```
src/JeemzuApi/
├── Controllers/             # Auth, Users, Scores, Chat, Admin, Knowledge
├── Data/
│   ├── AppDbContext.cs      # EF Core context + fluent config
│   ├── AppDbContextFactory.cs
│   └── about-me.json        # RAG knowledge base
├── DTOs/
│   └── Dtos.cs              # All request/response records
├── Models/                  # User, Score, RefreshToken, KnowledgeChunk
├── Services/                # 7 interface + implementation pairs
├── Migrations/              # EF Core migration history
├── Program.cs               # DI + middleware pipeline
└── appsettings.json

docker-compose.yml           # PostgreSQL
Dockerfile                   # .NET API multi-stage build
.github/workflows/deploy.yml # Notifies the frontend to regenerate API types
```

## Service Layer

| Service              | Responsibility                                                  |
| -------------------- | --------------------------------------------------------------- |
| `AuthService`        | JWT issuance, BCrypt hashing, token rotation, cookie management |
| `ScoreService`       | Score upsert (only if higher), leaderboard queries              |
| `UserService`        | Profile management, preferences, high score aggregation         |
| `ChatService`        | RAG pipeline — embed → retrieve → build prompt → LLM            |
| `EmbeddingService`   | Generate 1536-dim vectors via Semantic Kernel                   |
| `VectorStoreService` | pgvector cosine similarity queries                              |
| `IngestionService`   | Parse `about-me.json` → chunk → embed → upsert by SourceKey     |

All business services are **scoped**. AI services (`Kernel`, `IChatCompletionService`, `IEmbeddingGenerator`) are **singleton**.

## Local Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL)

### 1. Start the database

```bash
docker compose up -d
```

Starts:

- PostgreSQL 16 on port **5432** (`jeemzu` / `jeemzu_dev_password` / `jeemzu_db`)

### 2. Configure secrets

```bash
cd src/JeemzuApi
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=jeemzu_db;Username=jeemzu;Password=jeemzu_dev_password"
dotnet user-secrets set "Jwt:Secret" "<256-bit-key>"
dotnet user-secrets set "Jwt:Issuer" "jeemzu-api"
dotnet user-secrets set "Jwt:Audience" "jeemzu-frontend"
dotnet user-secrets set "OpenAI:ApiKey" "<your-key>"
dotnet user-secrets set "OpenAI:BudgetApiKey" "<your-budget-key>"
dotnet user-secrets set "OpenAI:GddApiKey" "<your-gdd-key>"

# Optional — required only to actually deliver verification / password reset emails
dotnet user-secrets set "Resend:ApiKey" "<your-key>"
dotnet user-secrets set "Resend:AccountFrom" "Jeemzu <no-reply@your-verified-domain>"
```

Verification and reset links point at `Frontend:BaseUrl` (`http://localhost:5173` in Development,
`https://jeemzu.me` otherwise). Without `Resend:ApiKey` registration still succeeds — the email is
skipped and logged, so the address stays unverified and can't be used for password recovery.

#### Reset a local admin password without email

In an interactive PowerShell terminal, select the local Docker database and run:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=::1;Port=5432;Database=jeemzu_db;Username=jeemzu;Password=jeemzu_dev_password'
dotnet run --project C:\dev\jeemzu.api\src\JeemzuApi --configuration Debug --no-launch-profile -- --reset-local-admin-password jeemzu
```

The command prompts twice without echoing the new password. It only accepts a
loopback connection to `jeemzu_db` and an existing Admin account. It uses the
application's BCrypt work factor, verifies the persisted password, revokes refresh
sessions and outstanding password-reset links, and exits without starting the API,
running migrations, sending email, or seeding data. Existing access JWTs remain
valid until expiry. The IPv6 host avoids the competing IPv4 PostgreSQL listener
on this development machine. Debug output is separate from the running Release
API; stop a running Debug API first if it locks the build output.

### 3. Run the API

```bash
dotnet run --project src/JeemzuApi
```

- Swagger UI: http://localhost:5050/swagger
- Health check: http://localhost:5050/health
- Migrations apply automatically on startup

## EF Migrations

```bash
# Add new migration
dotnet ef migrations add <Name> --project src/JeemzuApi

# Apply manually (also runs on startup)
dotnet ef database update --project src/JeemzuApi

# Install tool if needed
dotnet tool install --global dotnet-ef
```

## Deployment

### Render

`render.yaml` defines the public `jeemzu-api` service and managed PostgreSQL in the same region.
Pushing to `main` triggers the API auto-deploy; Blueprint synchronization is separate.

Secrets are marked `sync: false` in the Blueprint, so their values live in the Render dashboard and
are never committed.

#### Retiring the former agents service

1. Sync the Blueprint and set **jeemzu-api**'s dedicated `OpenAI__BudgetApiKey` (and `OpenAI__GddApiKey` for GDD). Check `OpenAI__BudgetModel=gpt-6-luna`; verify model access in the key's OpenAI project.
2. Deploy the API. Confirm Budgetize can answer a question and preview/apply a temporary override, and inspect local configuration at `/api/admin/health`.
3. Manually delete the existing **jeemzu-agents** service in Render after verification. Removing it from the Blueprint does not delete a deployed resource or stop its billing.
4. Remove stale `Agents__BaseUrl` from the API dashboard if retained. The retired service's `OPENAI_MODEL*`, `OPENAI_API_KEY`, `DOTNET_API_URL` and `TAVILY_API_KEY` no longer configure any current workflow. Revoke its provider credentials when no longer used elsewhere.

`InternalApiKey` remains for the existing `/api/knowledge/search` contract, not Budgetize; its authentication is unchanged. Ignored local files such as `agents/.env` are not deleted by the source migration; they are excluded from the Docker context.

### CI/CD (GitHub Actions)

On push to `main`, the workflow only dispatches `api-types-update` to the `jeemzu.me` repo so the
frontend regenerates its OpenAPI types. Render builds and deploys the API container itself.

### Render Configuration

Service env vars:

- `ConnectionStrings__DefaultConnection` — Npgsql keyword form; Render's `postgres://` URI will not parse
- `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience`
- `OpenAI__ApiKey`
- `OpenAI__BudgetApiKey`, `OpenAI__GddApiKey` — dedicated keys, with no fallback
- `OpenAI__BudgetModel`, `OpenAI__GddModel`, `OpenAI__ChatModel` — independently configurable, default `gpt-6-luna`
- `Resend__ApiKey`, `Resend__AccountFrom` — account verification and password reset email
- `Frontend__BaseUrl` — base URL used to build verification and reset links
- `Seed__AdminUsername`, `Seed__AdminEmail`, `Seed__AdminPassword` — one-time admin bootstrap
- `PORT=8080`
- `ASPNETCORE_ENVIRONMENT=Production`

### CORS

Allowed origins: `jeemzu.me`, `www.jeemzu.me`, `localhost:5173`
Credentials enabled (required for httpOnly refresh cookie).
