# JeemzuAPI Reference

ASP.NET Core 8 REST API backing [jeemzu.me](https://jeemzu.me). Handles user accounts, authentication, game score submission, and leaderboards for the Jeemzu portfolio site.

- **Production base URL:** `https://jeemzu-dpafd8b9dbezf6gx.canadaeast-01.azurewebsites.net`
- **Dev base URL:** `https://<jeemzu-dev>.westus3-01.azurewebsites.net`
- **Swagger UI:** `{baseUrl}/swagger`
- **Health check:** `GET {baseUrl}/health`
- **Source:** [github.com/Jeemzu/jeemzu.api](https://github.com/Jeemzu/jeemzu.api)

---

## Authentication

JeemzuAPI uses JWT Bearer tokens for authentication with httpOnly cookie-based refresh tokens.

### Token flow

1. Register or log in → receive an `accessToken` (1 hour TTL) in the response body and a `refreshToken` set as an httpOnly cookie (30-day TTL, path `/api/auth`).
2. Attach the access token to protected requests: `Authorization: Bearer <accessToken>`
3. When the access token expires, call `POST /api/auth/refresh` — the browser sends the cookie automatically. A new access token and rotated refresh cookie are returned.
4. On logout, call `POST /api/auth/logout` to revoke the refresh token server-side.

### Roles

| Role | Description |
|---|---|
| `User` | Default role for all registered users |
| `Admin` | Elevated access. Granted by setting `Role = 'Admin'` in the `Users` table directly. |

---

## Endpoints

### Auth — `/api/auth`

#### `POST /api/auth/refresh`
Silently exchanges a valid refresh token (httpOnly cookie) for a new access token. Rotates the refresh cookie.

**Auth required:** No (reads cookie automatically)

**Response `200`:**
```json
{
  "accessToken": "eyJ...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "role": "User"
}
```

**Response `401`:** Cookie missing, invalid, expired, or revoked.

---

#### `POST /api/auth/logout`
Revokes the refresh token and clears the cookie.

**Auth required:** No

**Response `204`:** No content.

---

### Users — `/api/users`

#### `POST /api/users/register`
Creates a new user account and returns a JWT. Username must be unique.

**Auth required:** No

**Request body:**
```json
{
  "username": "string (required, max 50)",
  "password": "string (required, min 8, max 100)",
  "optedIn": true
}
```

**Response `201`:**
```json
{
  "accessToken": "eyJ...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "role": "User"
}
```

**Response `409`:** Username already taken.

---

#### `POST /api/users/login`
Authenticates an existing user and returns a JWT. The role in the token reflects whatever `Role` is set on the user in the database.

**Auth required:** No

**Request body:**
```json
{
  "username": "string (required)",
  "password": "string (required)"
}
```

**Response `200`:**
```json
{
  "accessToken": "eyJ...",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "role": "User"
}
```

**Response `401`:** Invalid username or password.

---

#### `POST /api/users`
Updates the authenticated user's leaderboard opt-in preference. Username is taken from the JWT — not accepted from the request body.

**Auth required:** Yes — `Authorization: Bearer <token>`

**Request body:**
```json
{
  "optedIn": true
}
```

**Response `200`:** Updated `UserResponse` (see below).

**Response `401`:** Missing or invalid token.

---

#### `GET /api/users/{username}`
Fetches a user's profile and their personal best score for each game they've played.

**Auth required:** No

**Response `200`:**
```json
{
  "userId": "guid",
  "username": "string",
  "optedIn": true,
  "highScores": {
    "snake": 4200,
    "tetris": 8800
  }
}
```

**Response `404`:** User not found.

---

### Scores — `/api/scores`

#### `POST /api/scores`
Submits a score for the authenticated user. Username is taken from the JWT — never trusted from the client. Enforces one score per user per game: if a score already exists for this `(user, gameId)` pair, it is updated only if the new score is higher. Lower or equal scores are silently ignored and the existing best is returned.

**Auth required:** Yes — `Authorization: Bearer <token>`

**Request body:**
```json
{
  "gameId": "string (required, max 100, e.g. 'snake')",
  "score": 4200,
  "timestamp": 1750000000000
}
```

`gameId` is normalized to lowercase. `timestamp` is a Unix millisecond value supplied by the client.

**Response `201`:** The stored (or existing best) `ScoreResponse`:
```json
{
  "gameId": "snake",
  "username": "alice",
  "score": 4200,
  "timestamp": 1750000000000
}
```

**Response `401`:** Missing or invalid token.

---

#### `GET /api/scores/{gameId}?limit=10`
Returns the leaderboard for a game — top N scores across all users, sorted descending by score value. `limit` is clamped to 1–100, defaults to 10.

**Auth required:** No

**Response `200`:** Array of `ScoreResponse`:
```json
[
  { "gameId": "snake", "username": "alice", "score": 9800, "timestamp": 1750000000000 },
  { "gameId": "snake", "username": "bob",   "score": 7200, "timestamp": 1749000000000 }
]
```

---

#### `GET /api/scores/{gameId}/summary`
Returns the all-time record for a game and, if the request is authenticated, the requesting user's personal best. Designed for populating game modal pre-game screens in a single call.

**Auth required:** No (but include Bearer token to get `personalBest`)

**Response `200`:**
```json
{
  "allTimeRecord": {
    "gameId": "snake",
    "username": "alice",
    "score": 9800,
    "timestamp": 1750000000000
  },
  "personalBest": 4200
}
```

`allTimeRecord` is `null` if no scores exist for the game yet.
`personalBest` is `null` when unauthenticated or when the user has no score for this game.

---

## Data shapes

### `TokenResponse`
Returned by register, login, and refresh endpoints.
```json
{
  "accessToken": "string",
  "tokenType": "Bearer",
  "expiresIn": 3600,
  "role": "User | Admin"
}
```

### `UserResponse`
Returned by user profile and preference update endpoints.
```json
{
  "userId": "guid",
  "username": "string",
  "optedIn": true,
  "highScores": { "gameId": "bestScore" }
}
```

### `ScoreResponse`
Returned by score submission and leaderboard endpoints.
```json
{
  "gameId": "string",
  "username": "string",
  "score": 0,
  "timestamp": 0
}
```

### `GameSummaryResponse`
Returned by the summary endpoint.
```json
{
  "allTimeRecord": "ScoreResponse | null",
  "personalBest": "number | null"
}
```

---

## Database schema (summary)

| Table | Key columns |
|---|---|
| `Users` | `Id` (guid PK), `Username` (unique), `PasswordHash`, `Role`, `OptedIn` |
| `Scores` | `Id` (guid PK), `GameId`, `Username`, `UserId` (FK → Users, nullable), `ScoreValue`, `Timestamp` — unique index on `(UserId, GameId)` where `UserId IS NOT NULL` |
| `RefreshTokens` | `Id`, `Token` (unique), `Username`, `ExpiresAt`, `IsRevoked` |

---

## Environment variables (Azure App Service)

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string |
| `Jwt__Secret` | HMAC-SHA256 signing key (256-bit random) |
| `Jwt__Issuer` | Token issuer claim (default: `jeemzu-api`) |
| `Jwt__Audience` | Token audience claim (default: `jeemzu-frontend`) |
| `WEBSITES_PORT` | Must be `8080` to match the container's listening port |

---

## CORS

Allowed origins:
- `https://jeemzu.me`
- `https://www.jeemzu.me`
- `http://localhost:5173` (Vite dev server)

`AllowCredentials()` is enabled — required for the httpOnly refresh token cookie to be sent cross-origin.
