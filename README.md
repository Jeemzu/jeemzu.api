# jeemzu.api

ASP.NET Core 8 Web API — Leaderboard & User backend for [jeemzu.com](https://jeemzu.com).

## Stack
| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 8, controller-based |
| ORM | Entity Framework Core 8 |
| Database | PostgreSQL (Npgsql provider) |
| Local DB | Docker Compose |
| Deployment | Azure App Service + Azure Database for PostgreSQL |
| CI/CD | GitHub Actions |

## API Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/scores` | Submit a score `{gameId, username, score, timestamp}` |
| `GET` | `/api/scores/{gameId}?limit=10` | Get leaderboard for a game |
| `POST` | `/api/users` | Create or update a user `{username, optedIn}` |
| `GET` | `/api/users/{username}` | Get user + their per-game high scores |
| `GET` | `/health` | DB connectivity health check |

These endpoints match the stubs already defined in the React frontend's `src/utils/gameApi.ts`. Point `VITE_API_URL` at this service to activate them.

## Local Development

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for local PostgreSQL)

### 1. Start the database
```bash
docker compose up -d
```

This starts a PostgreSQL 16 container on port 5432 with:
- **User**: `jeemzu`
- **Password**: `jeemzu_dev_password`
- **DB**: `jeemzu_db`

### 2. Configure the connection string via user-secrets
```bash
cd src/JeemzuApi
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=jeemzu_db;Username=jeemzu;Password=jeemzu_dev_password"
```

User secrets are stored outside the repo — credentials are never committed.

### 3. Run migrations
```bash
dotnet ef database update
```

On first run this creates the `Scores` and `Users` tables. The app also runs `Database.Migrate()` automatically on startup.

### 4. Start the API
```bash
dotnet run
```

- Swagger UI: http://localhost:5000/swagger
- Health check: http://localhost:5000/health

## Running EF Migrations

```bash
# Add a new migration after changing a Model or DbContext
dotnet ef migrations add <MigrationName>

# Apply pending migrations to the DB
dotnet ef database update

# If dotnet-ef is not installed globally
dotnet tool install --global dotnet-ef
```

## Deployment (Azure)

1. Create an Azure App Service (runtime: .NET 8) and an Azure Database for PostgreSQL Flexible Server.
2. In App Service → Configuration → Application settings, add:
   - `ConnectionStrings__DefaultConnection` — Azure PostgreSQL connection string
   - `ASPNETCORE_ENVIRONMENT` → `Production`
3. Download the Publish Profile from the Azure portal and save it as a GitHub Actions secret named `AZURE_WEBAPP_PUBLISH_PROFILE`.
4. Push to `main` — the workflow in `.github/workflows/deploy.yml` builds and deploys automatically.

EF migrations run automatically at startup via `db.Database.Migrate()` in `Program.cs`.

## Project Structure

```
src/JeemzuApi/
├── Controllers/
│   ├── ScoresController.cs    # POST /api/scores, GET /api/scores/{gameId}
│   └── UsersController.cs     # POST /api/users, GET /api/users/{username}
├── Data/
│   ├── AppDbContext.cs        # EF Core DbContext, Fluent API config
│   └── Migrations/            # Auto-generated EF migrations
├── DTOs/
│   └── Dtos.cs                # Request/response shapes matching the TS frontend types
├── Models/
│   ├── Score.cs               # Score entity
│   └── User.cs                # User entity
├── Services/
│   ├── IScoreService.cs       # Score service interface
│   ├── ScoreService.cs        # Score service implementation
│   ├── IUserService.cs        # User service interface
│   └── UserService.cs         # User service implementation
├── appsettings.json
├── appsettings.Development.json
└── Program.cs                 # DI, middleware pipeline, CORS, health checks
docker-compose.yml             # Local PostgreSQL
.github/workflows/deploy.yml   # GitHub Actions → Azure
```

## Roadmap

- [ ] Phase 1: Leaderboard API (this) ← *current*
- [ ] Phase 2: JWT authentication & user accounts (ASP.NET Core Identity)
- [ ] Phase 3: RPG cloud save sync (MongoDB / Azure Cosmos DB)
- [ ] Phase 4: Community level sharing (Blob Storage for level JSON)
- [ ] Phase 5: SignalR real-time leaderboard updates
- [ ] Phase 6: Redis caching on leaderboard reads + rate limiting
