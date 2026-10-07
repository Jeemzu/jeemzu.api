# jeemzu.api

ASP.NET Core 8 Web API + Python multi-agent service — backend for [jeemzu.me](https://jeemzu.me). Handles leaderboards, user accounts, JWT auth, and RAG-powered AI chat with intelligent agent routing.

## Stack

| Concern          | Technology                                                           |
| ---------------- | -------------------------------------------------------------------- |
| Framework        | ASP.NET Core 8 (controller-based)                                    |
| ORM              | Entity Framework Core 8 (Npgsql)                                     |
| Database         | PostgreSQL 16 + pgvector (HNSW cosine index)                         |
| AI/LLM           | Semantic Kernel 1.77 — GPT-4o-mini + text-embedding-3-small          |
| Agent service    | Python 3.11, FastAPI, LangGraph, LangChain                           |
| Auth             | JWT Bearer (HMAC-SHA256, 60 min) + httpOnly refresh cookie (30 days) |
| Password hashing | BCrypt (work factor 12)                                              |
| API docs         | Swagger/OpenAPI (Swashbuckle)                                        |
| Containerization | Docker multi-stage (runtime: port 8080)                              |
| Hosting          | Render (Docker web service + private service)                        |
| Database (prod)  | Render Managed PostgreSQL 16 + pgvector                              |
| Database (dev)   | Docker Compose (`pgvector/pgvector:pg16`, port 5432)                 |
| CI/CD            | Render auto-deploy from `main` (see `render.yaml`)                   |

## Third-Party Services

| Service           | Responsibility                                                                                                                                                                                       |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Render            | Hosts everything: the ASP.NET Core API as a public web service at `api.jeemzu.me`, the Python agents as a private service with no public hostname, and managed PostgreSQL. Defined in `render.yaml`. |
| Render PostgreSQL | Production database for users, scores, refresh tokens, budgets, and knowledge chunks. The `vector` extension supports semantic search over knowledge embeddings.                                     |
| OpenAI            | Supplies chat completions and text embeddings to the .NET RAG API and the Python agents. API keys are provided through deployment configuration.                                                     |
| Tavily            | Optional web-search provider used by the chatbot agent for current or general-topic queries; requires `TAVILY_API_KEY`.                                                                              |
| Resend            | Email provider used by `POST /api/contact` and by account email verification / password reset when `Resend:ApiKey` is configured.                                                                    |
| GitHub Actions    | Dispatches `api-types-update` to the frontend repo so it regenerates OpenAPI types. Render handles building and deploying, so CI does not build images.                                              |

The agents service is reachable only over Render's private network; the browser never calls it directly.

### Local-Only Infrastructure

Docker Compose starts PostgreSQL and the Python agents service for local development.

## API Endpoints

Budgetize subscriptions are stored in the existing budget `bills` array. The optional
`isSubscription` boolean on each bill preserves its frontend section assignment;
omitted values default to `false`. Billing validation and storage are unchanged,
and no database migration is required.

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

## Agent Service (Python)

LangGraph-based multi-agent orchestration for intelligent chat routing. Runs as a sidecar in Docker Compose on port 8001.

### Architecture

```
User question → POST /chat
    ↓
Router (classifies query type)
    ↓
Parallel agent execution:
  ├── knowledge — vector search + LLM (personal/skills/projects)
  ├── game_stats — tool calls to .NET API (leaderboards, user profiles)
  ├── web_search — Tavily API (general topics, current events)
  └── chitchat — direct LLM (greetings, small talk)
    ↓
Synthesizer (combines outputs) → final answer
```

### Agent Nodes

| Node          | Purpose                                                       |
| ------------- | ------------------------------------------------------------- |
| `router`      | Classifies query → selects agents to invoke                   |
| `knowledge`   | Answers about James's skills, experience, projects, education |
| `game_stats`  | Live leaderboard queries, game scores, user profiles          |
| `web_search`  | External search for general topics and current events         |
| `synthesizer` | Combines all agent outputs into a coherent response           |

### Tools

| Tool              | Target                                                         |
| ----------------- | -------------------------------------------------------------- |
| `knowledge_tool`  | Vector search against `KnowledgeChunk` table                   |
| `scores_tool`     | `GET /api/scores/{gameId}`, `GET /api/scores/{gameId}/summary` |
| `users_tool`      | `GET /api/users/{username}`                                    |
| `web_search_tool` | Tavily web search API                                          |

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

agents/                      # Python multi-agent service
├── main.py                  # FastAPI server (POST /chat)
├── graph.py                 # LangGraph state machine
├── state.py                 # AgentState TypedDict
├── config.py                # Environment config
├── nodes/                   # Router, Knowledge, GameStats, WebSearch, Synthesizer
├── tools/                   # knowledge_tool, scores_tool, users_tool, web_search_tool
├── pyproject.toml
└── Dockerfile

docker-compose.yml           # PostgreSQL + agents service
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
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL + agents)
- [Python 3.11+](https://www.python.org/) (for agents service, optional)

### 1. Start the database + agents

```bash
docker compose up -d
```

Starts:

- PostgreSQL 16 on port **5432** (`jeemzu` / `jeemzu_dev_password` / `jeemzu_db`)
- Agents service on port **8001** (FastAPI + LangGraph)

### 2. Configure secrets

```bash
cd src/JeemzuApi
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=jeemzu_db;Username=jeemzu;Password=jeemzu_dev_password"
dotnet user-secrets set "Jwt:Secret" "<256-bit-key>"
dotnet user-secrets set "Jwt:Issuer" "jeemzu-api"
dotnet user-secrets set "Jwt:Audience" "jeemzu-frontend"
dotnet user-secrets set "OpenAI:ApiKey" "<your-key>"

# Optional — required only to actually deliver verification / password reset emails
dotnet user-secrets set "Resend:ApiKey" "<your-key>"
dotnet user-secrets set "Resend:AccountFrom" "Jeemzu <no-reply@your-verified-domain>"
```

Verification and reset links point at `Frontend:BaseUrl` (`http://localhost:5173` in Development,
`https://jeemzu.me` otherwise). Without `Resend:ApiKey` registration still succeeds — the email is
skipped and logged, so the address stays unverified and can't be used for password recovery.

### 3. Run the API

```bash
dotnet run --project src/JeemzuApi
```

- Swagger UI: http://localhost:5050/swagger
- Health check: http://localhost:5050/health
- Migrations apply automatically on startup

### 4. Run agents locally (without Docker)

```bash
cd agents
pip install -e .
uvicorn main:app --port 8001
```

Requires `.env` with `OPENAI_API_KEY`, `DOTNET_API_URL`, `TAVILY_API_KEY`.

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

`render.yaml` defines the whole stack: the public `jeemzu-api` web service, the private
`jeemzu-agents` service, and the managed PostgreSQL instance. All three live in the same region so
the private network resolves. Pushing to `main` triggers an auto-deploy of both services.

Secrets are marked `sync: false` in the Blueprint, so their values live in the Render dashboard and
are never committed.

### CI/CD (GitHub Actions)

On push to `main`, the workflow only dispatches `api-types-update` to the `jeemzu.me` repo so the
frontend regenerates its OpenAPI types. Render builds and deploys the containers itself.

### Render Configuration

Service env vars:

- `ConnectionStrings__DefaultConnection` — Npgsql keyword form; Render's `postgres://` URI will not parse
- `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience`
- `OpenAI__ApiKey`
- `Agents__BaseUrl` — private-network address of the agents service
- `Resend__ApiKey`, `Resend__AccountFrom` — account verification and password reset email
- `Frontend__BaseUrl` — base URL used to build verification and reset links
- `Seed__AdminUsername`, `Seed__AdminEmail`, `Seed__AdminPassword` — one-time admin bootstrap
- `PORT=8080`
- `ASPNETCORE_ENVIRONMENT=Production`

### CORS

Allowed origins: `jeemzu.me`, `www.jeemzu.me`, `localhost:5173`, `localhost:8001`
Credentials enabled (required for httpOnly refresh cookie).
