# Copilot Instructions for JeemzuAPI

## Project Overview
Backend REST API for jeemzu.me — a personal portfolio + browser-games site. ASP.NET Core 8 controller-based Web API with PostgreSQL + pgvector, JWT auth, and a RAG-powered AI chat endpoint.

## Technology Stack

| Concern | Technology |
|---|---|
| Framework | ASP.NET Core 8 (`net8.0`) |
| ORM | Entity Framework Core 8 (Npgsql/PostgreSQL) |
| Vector search | `pgvector` extension — HNSW cosine index |
| AI/LLM | Microsoft Semantic Kernel 1.77 — GPT-4o-mini chat + text-embedding-3-small |
| Auth | JWT Bearer (HMAC-SHA256, 60 min) + httpOnly refresh token cookie (30 days) |
| Password hashing | BCrypt.Net-Next, work factor 12 |
| API docs | Swagger/OpenAPI via Swashbuckle |
| Health check | `AspNetCore.HealthChecks.NpgSql` at `/health` |
| Containerization | Docker multi-stage — runtime image on port 8080 |
| Hosting | Azure App Service + Azure Container Registry |
| Database (prod) | Azure Database for PostgreSQL Flexible Server |
| Database (dev) | Docker Compose (`postgres:16-alpine`, port 5432) |

## Repository Structure

```
src/JeemzuApi/
├── Controllers/         # 5 controllers (Admin, Auth, Chat, Scores, Users)
├── Data/
│   ├── AppDbContext.cs          # EF Core DB context + fluent config
│   ├── AppDbContextFactory.cs   # Design-time factory for EF migrations
│   └── about-me.json            # RAG knowledge base (personal, skills, experience, projects, education)
├── DTOs/
│   └── Dtos.cs          # All request/response record types
├── Migrations/          # EF Core migration history
├── Models/              # Entity classes (Score, User, RefreshToken, KnowledgeChunk)
├── Services/            # Interface + implementation pairs for all business logic
├── Properties/
├── appsettings.json
├── appsettings.Development.json
└── Program.cs           # DI registrations + middleware pipeline
```

## Database Schema

### Entities (`AppDbContext` DbSets)

**`Score`**
- `Guid Id`
- `string GameId` — matches frontend game ID (e.g., `"snake"`, `"tetris"`, `"pong"`)
- `string Username`
- `Guid? UserId` — nullable FK → `User` (null = legacy/guest); on delete `SetNull`
- `int ScoreValue`
- `long Timestamp` — Unix ms
- `DateTimeOffset CreatedAt`
- Indexes: composite `(GameId, ScoreValue)`; unique filtered `(UserId, GameId) WHERE UserId IS NOT NULL`

**`User`**
- `Guid Id`
- `string Username` — unique, max 50
- `bool OptedIn` — global leaderboard opt-in
- `string Role` — `"User"` (default) or `"Admin"` (must be set directly in DB to promote)
- `string? PasswordHash` — BCrypt, null for legacy/guest accounts
- `DateTimeOffset CreatedAt`, `UpdatedAt`

**`RefreshToken`**
- `int Id`
- `string Token` — unique, max 256, opaque base64 (64 random bytes)
- `string Username`
- `DateTimeOffset ExpiresAt`
- `bool IsRevoked`
- `DateTimeOffset CreatedAt`

**`KnowledgeChunk`**
- `Guid Id`
- `string SourceKey` — unique, max 200 (e.g., `"experience.microsoft"`, `"skills.languages"`)
- `string Content` — human-readable text injected into LLM context
- `Vector Embedding` — `vector(1536)`, HNSW index (`vector_cosine_ops`, m=16, ef_construction=64)
- `DateTimeOffset CreatedAt`, `UpdatedAt`

### EF Migrations (chronological)
1. `20260619003110_InitialCreate`
2. `20260619231100_AddRefreshTokens`
3. `20260620042622_AddUserAuthAndScoreFK`
4. `20260620052432_AddUserRole`
5. `20260621173218_AddUniqueScorePerUserPerGame`
6. `20260623015936_AddKnowledgeChunks`

> Migrations are applied automatically on startup via `db.Database.Migrate()` in `Program.cs`.

## API Endpoints

### `AuthController` — `/api/auth`
No auth required on any endpoint.

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/refresh` | Reads `refreshToken` httpOnly cookie → validates & rotates → returns `TokenResponse`. `401` if missing/invalid/revoked/expired. |
| `POST` | `/api/auth/logout` | Reads cookie → revokes token in DB → deletes cookie. Returns `204`. |

### `UsersController` — `/api/users`

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/users/register` | None | Creates account, returns `201 TokenResponse`. `409` if username taken. Also sets refresh cookie. |
| `POST` | `/api/users/login` | None | Authenticates, returns `TokenResponse`. Sets refresh cookie. `401` on bad creds. |
| `POST` | `/api/users` | `[Authorize]` | Updates `OptedIn` for authenticated user (username from JWT). Returns `UserResponse`. |
| `GET` | `/api/users/{username}` | None | Returns `UserResponse` with `highScores` dictionary. `404` if not found. |

### `ScoresController` — `/api/scores`

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/scores` | `[Authorize]` | Submit score. Upserts — only updates if new score is higher. Returns `201 ScoreResponse`. |
| `GET` | `/api/scores/{gameId}?limit=10` | None | Top-N leaderboard, descending. `limit` clamped 1–100. Returns `ScoreResponse[]`. |
| `GET` | `/api/scores/{gameId}/summary` | Optional JWT | `GameSummaryResponse` with `AllTimeRecord` + `PersonalBest` (null when unauthenticated). |

### `ChatController` — `/api/chat`

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/chat` | None | RAG chat. Accepts `ChatRequest { Question, History[] }`. Returns `ChatResponse { Answer }`. |

### `AdminController` — `/api/admin`
Entire controller requires `[Authorize(Roles = "Admin")]`.

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/admin/knowledge/ingest` | Re-reads `about-me.json`, regenerates embeddings, upserts all chunks. Returns `IngestResponse { ChunksUpserted }`. |

## DTOs (all in `JeemzuApi.DTOs`)

**Auth/Users:**
- `RegisterRequest` — `string Username` (Required, max 50), `string Password` (Required, min 8, max 100), `bool OptedIn`
- `LoginRequest` — `string Username` (Required), `string Password` (Required)
- `TokenResponse` — `string AccessToken`, `string TokenType = "Bearer"`, `int ExpiresIn`, `string Role`
- `UpdateUserRequest` — `bool OptedIn`
- `UserResponse` — `string? UserId`, `string Username`, `bool OptedIn`, `Dictionary<string, int> HighScores`

**Scores:**
- `SubmitScoreRequest` — `string GameId` (Required, max 100), `int Score` (Range 0–MaxInt), `long Timestamp`
- `ScoreResponse` — `string GameId`, `string Username`, `int Score`, `long Timestamp`
- `GameSummaryResponse` — `ScoreResponse? AllTimeRecord`, `int? PersonalBest`

**Chat:**
- `ConversationMessage` — `string Role` (allowed: `"user"`, `"assistant"`), `string Content`
- `ChatRequest` — `string Question` (Required, min 1, max 2000), `List<ConversationMessage> History`
- `ChatResponse` — `string Answer`
- `IngestResponse` — `int ChunksUpserted`

## Service Layer

All services use the **interface + scoped implementation** pattern. Services inject `AppDbContext` directly (no repository abstraction).

### `IAuthService` / `AuthService`
- `RegisterUserAsync(RegisterRequest, HttpResponse)` → BCrypt-hashes password, creates `User`, issues tokens
- `LoginUserAsync(LoginRequest, HttpResponse)` → verifies BCrypt hash, issues tokens
- `RefreshAsync(string refreshToken, HttpResponse)` → token rotation (revoke old, issue new)
- `LogoutAsync(string refreshToken, HttpResponse)` → marks revoked, deletes cookie
- **JWT claims:** `ClaimTypes.Name` (username), `ClaimTypes.Role`, `Jti` (new Guid). **Note:** `ClaimTypes.NameIdentifier` (userId) is NOT currently in the JWT.
- **Cookie config:** dev — `SameSite=Lax, Secure=false`; prod — `SameSite=None, Secure=true`, path `/api/auth`

### `IScoreService` / `ScoreService`
- `SaveScoreAsync(SubmitScoreRequest, string username)` — normalizes `gameId` to lowercase, upserts only if higher score
- `GetLeaderboardAsync(string gameId, int limit)` — top-N `ScoreValue DESC`, limit clamped 1–100
- `GetGameSummaryAsync(string gameId, Guid? userId)` — all-time record + optional personal best

### `IUserService` / `UserService`
- `UpdatePreferencesAsync(string username, UpdateUserRequest)` — updates `OptedIn + UpdatedAt`
- `GetUserAsync(string username)` — returns null if not found
- `BuildUserResponseAsync(User)` (private) — groups scores by `GameId`, returns max per game as `HighScores` dict

### `IChatService` / `ChatService`
Full RAG pipeline per request:
1. Embed `question` via `IEmbeddingService`
2. Retrieve top-5 chunks via `IVectorStoreService` (cosine similarity)
3. Build system prompt with injected context
4. Replay `History` into `ChatHistory`
5. Append current question
6. Call `IChatCompletionService.GetChatMessageContentAsync`

Server is stateless — full conversation history is client-owned and sent with every request.

### `IEmbeddingService` / `EmbeddingService`
- `GenerateEmbeddingAsync(string text)` — calls Semantic Kernel `IEmbeddingGenerator`, returns 1536-dim vector

### `IVectorStoreService` / `VectorStoreService`
- `SearchAsync(ReadOnlyMemory<float> queryEmbedding, int topK)` — pgvector `<=>` (cosine distance) via `k.Embedding.CosineDistance(queryVector)`, orders ascending, takes `topK`

### `IIngestionService` / `IngestionService`
- `IngestAsync()` — reads `about-me.json`, builds chunks, generates embeddings, upserts by `SourceKey`
- **Chunk keys produced:** `personal`, `skills.languages`, `skills.frontend`, `skills.backend`, `skills.cloud`, `skills.testing`, `skills.ai`, `experience.{company}`, `projects.{name}`, `education.{institution}`

### AI Services (Singleton)
- `Microsoft.SemanticKernel.Kernel` — built with OpenAI chat + embedding plugins
- `IChatCompletionService` — resolved from kernel
- `IEmbeddingGenerator<string, Embedding<float>>` — resolved from kernel

## Authentication & Authorization

- **Access token:** JWT Bearer, HMAC-SHA256, 60-minute TTL
- **Refresh token:** httpOnly cookie (`refreshToken`), 30-day TTL, path-scoped to `/api/auth`, token rotation on every use
- **Roles:** `"User"` (default on register), `"Admin"` (must be promoted directly in DB — no API for this)
- **Promoting an admin:** `UPDATE "Users" SET "Role" = 'Admin' WHERE "Username" = 'target';`
- **CORS:** allowed origins `jeemzu.me`, `www.jeemzu.me`, `localhost:5173`; `AllowCredentials` (required for cookie)

## Configuration

### `appsettings.json` (committed, non-secret)
```json
{
  "OpenAI": { "ChatModel": "gpt-4o-mini", "EmbeddingModel": "text-embedding-3-small" },
  "Rag": { "TopK": 5 }
}
```

### Secrets (never commit)

| Key | Source (dev) | Source (prod) |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `dotnet user-secrets` | Azure App Setting (`ConnectionStrings__DefaultConnection`) |
| `Jwt:Secret` | `dotnet user-secrets` | Azure App Setting (`Jwt__Secret`) |
| `Jwt:Issuer` | `dotnet user-secrets` | Azure App Setting (`Jwt__Issuer`) |
| `Jwt:Audience` | `dotnet user-secrets` | Azure App Setting (`Jwt__Audience`) |
| `OpenAI:ApiKey` | `dotnet user-secrets` | Azure App Setting (`OpenAI__ApiKey`) |
| — | — | `WEBSITES_PORT=8080` |

**User secrets ID:** `d3d87c5e-a19e-4dfe-a656-a366e5e4ce36`

## Program.cs — DI Registration Order & Middleware Pipeline

**DI (in order):**
1. `AddControllers()`
2. `AddEndpointsApiExplorer()` + `AddSwaggerGen()` (with Bearer security def)
3. `AddDbContext<AppDbContext>()` — Npgsql with `UseVector()`
4. `AddScoped<IScoreService, ScoreService>()`
5. `AddScoped<IUserService, UserService>()`
6. `AddScoped<IAuthService, AuthService>()`
7. `AddSingleton(kernel)` — Semantic Kernel `Kernel` instance
8. `AddSingleton<IChatCompletionService>()` — from kernel
9. `AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>()` — from kernel
10. `AddScoped<IEmbeddingService, EmbeddingService>()`
11. `AddScoped<IVectorStoreService, VectorStoreService>()`
12. `AddScoped<IIngestionService, IngestionService>()`
13. `AddScoped<IChatService, ChatService>()`
14. `AddAuthentication(JwtBearerDefaults).AddJwtBearer(...)`
15. `AddAuthorization()`
16. `AddHealthChecks().AddNpgSql(connectionString)`
17. `AddCors("JeemzuFrontend")`

**Middleware pipeline (in order):**
1. `db.Database.Migrate()` — auto-apply pending migrations on startup
2. `UseSwagger()` + `UseSwaggerUI()` — enabled in all environments
3. `UseCors("JeemzuFrontend")`
4. `UseAuthentication()`
5. `UseAuthorization()`
6. `MapHealthChecks("/health")`
7. `MapControllers()`

## Docker & Deployment

**Dev local DB (`docker-compose.yml`):**
```
Image:    postgres:16-alpine
Port:     5432:5432
DB:       jeemzu_db
User:     jeemzu
Password: jeemzu_dev_password
Volume:   jeemzu_pgdata
```

**Production (`Dockerfile` multi-stage):**
- Build: `mcr.microsoft.com/dotnet/sdk:8.0` → publish Release
- Runtime: `mcr.microsoft.com/dotnet/aspnet:8.0` → port 8080 (`ASPNETCORE_URLS=http://+:8080`)

**CI/CD (`.github/workflows/deploy.yml`):**
- Trigger: push to `main` or `workflow_dispatch`
- Builds & pushes Docker image to Azure Container Registry (`jeemzuregistry.azurecr.io/jeemzu-api`)
- Tags: `:{git-sha}` + `:latest` (on main) or `:dev`
- After deploy: dispatches `api-types-update` event to `Jeemzu/jeemzu.me` so the frontend can regenerate its OpenAPI types

## Frontend Integration (jeemzu.me)

The frontend (`jeemzu.me`) is a React 19 + TypeScript + Vite + Wouter SPA deployed on Netlify.

**API base URL:** `VITE_API_URL` env var (defaults to `http://localhost:5000/api`)  
**Production URL:** `https://jeemzu-prod-eza9c8edcdhhbqhw.canadaeast-01.azurewebsites.net`

**Token handling:**
- Access token stored in-memory via Zustand (`authStore.ts`) — lost on page refresh
- Refresh token is an httpOnly cookie — `POST /api/auth/refresh` called once on app load to restore session
- Bearer token injected via `authHeader()` utility that reads `useAuthStore.getState().accessToken`
- All auth requests include `credentials: 'include'`

**Generated types:** `src/types/api.generated.ts` is auto-generated from the live OpenAPI spec via `openapi-typescript`. Regenerated via the `api-types-update` GitHub Actions dispatch after every backend deploy.

**Frontend utility modules:**
- `src/utils/authApi.ts` — register, login, logout, refresh
- `src/utils/gameApi.ts` — save/get scores, get game summary, get user data, update preferences
- `src/utils/chatApi.ts` — post chat question with history
- `src/components/ChatBot.tsx` — floating RAG chat UI (GPT-4o-mini, stateless, history sent per request)

## Key Patterns & Conventions

- **No repository pattern** — services use `AppDbContext` directly
- **No CQRS** — one service method per operation
- **Scoped services, Singleton AI** — all business services are `Scoped`; `Kernel`, `IChatCompletionService`, and `IEmbeddingGenerator` are `Singleton` (stateless, shared HTTP clients)
- **Upsert by `SourceKey`** — ingestion is idempotent; re-running `POST /api/admin/knowledge/ingest` is always safe
- **Score upsert** — `POST /api/scores` only updates an existing record if the new score is strictly higher
- **Stateless chat** — full conversation history is owned by the client and replayed on every request
- **Token rotation** — every refresh call revokes the old token and issues a new one
- **`ClaimTypes.NameIdentifier` (userId) is NOT in the JWT** — `ScoresController.GetGameSummary` falls back to a DB lookup by username when resolving `PersonalBest`

## Development Commands

```bash
# Start local PostgreSQL
docker compose up -d

# Apply secrets (first time)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=jeemzu_db;Username=jeemzu;Password=jeemzu_dev_password"
dotnet user-secrets set "Jwt:Secret" "<your-secret>"
dotnet user-secrets set "OpenAI:ApiKey" "<your-key>"

# Run API (auto-migrates on startup)
dotnet run --project src/JeemzuApi

# Add a new EF migration
dotnet ef migrations add <MigrationName> --project src/JeemzuApi

# Apply migrations manually
dotnet ef database update --project src/JeemzuApi
```

## Adding New Features — Checklist

**New endpoint:**
1. Add DTO(s) to `DTOs/Dtos.cs`
2. Add service method to `IXxxService` interface + `XxxService` implementation
3. Add controller action with appropriate `[Authorize]` / `[AllowAnonymous]`
4. If new DB column/table: add migration with `dotnet ef migrations add`
5. Update OpenAPI types in frontend: push to main → CI dispatches `api-types-update` to `jeemzu.me`

**New knowledge chunk source:**
1. Add entry to `Data/about-me.json`
2. Update `IngestionService.BuildChunks()` to handle the new JSON shape
3. Call `POST /api/admin/knowledge/ingest` (requires Admin JWT)

**Promote a user to Admin:**
```sql
UPDATE "Users" SET "Role" = 'Admin' WHERE "Username" = 'target_username';
```
