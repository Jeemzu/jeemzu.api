# JeemzuAPI Reference

ASP.NET Core 8 REST API backing [jeemzu.me](https://jeemzu.me). Handles user accounts, authentication, game score submission, and leaderboards for the Jeemzu portfolio site.

- **Production base URL:** `https://api.jeemzu.me`
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

Username and password remain the sign-in credentials. Registration also collects an email address, which becomes a recovery address once verified. Changing a username or password reissues tokens and revokes every other session for that account.

### Roles

| Role    | Description                                                                         |
| ------- | ----------------------------------------------------------------------------------- |
| `User`  | Default role for all registered users                                               |
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

Creates a new user account and returns a JWT. Username must be unique. A verification link is emailed to the supplied address; registration still succeeds if delivery fails, but the address stays unverified and cannot be used for password recovery.

**Auth required:** No

**Request body:**

```json
{
  "username": "string (required, max 50)",
  "email": "string (required, valid email, max 256)",
  "password": "string (required, min 8, max 100)",
  "optedIn": true,
  "emailListSubscribed": false
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

Updates the authenticated user's preferences. Username is taken from the JWT — not accepted from the request body. Omitted fields are left unchanged.

**Auth required:** Yes — `Authorization: Bearer <token>`

**Request body:**

```json
{
  "optedIn": true,
  "emailListSubscribed": false
}
```

**Response `200`:** Updated `UserResponse` (see below).

**Response `401`:** Missing or invalid token.

---

#### `GET /api/users/me`

Returns the signed-in user's own account settings, including private fields that `GET /api/users/{username}` never exposes.

**Auth required:** Yes

**Response `200`:** `ProfileResponse` (see below).

---

#### `POST /api/users/me/preferences`

Same semantics as `POST /api/users`, but returns the full `ProfileResponse`.

**Auth required:** Yes

**Response `200`:** `ProfileResponse`.

---

#### `POST /api/users/me/username`

Renames the account. Existing scores are renamed in the same transaction, all refresh tokens are revoked, and a fresh token pair is issued.

**Auth required:** Yes

**Request body:**

```json
{ "username": "string (required, max 50)" }
```

**Response `200`:** `TokenResponse`.

**Response `409`:** Username already taken.

---

#### `POST /api/users/me/password`

Changes the password while signed in. Revokes all refresh tokens and returns a fresh token pair. Rate limited.

**Auth required:** Yes

**Request body:**

```json
{
  "currentPassword": "string (required)",
  "newPassword": "string (required, min 8, max 100)"
}
```

**Response `200`:** `TokenResponse`.

**Response `401`:** Current password is incorrect.

---

#### `POST /api/users/me/resend-verification`

Issues a new verification link, invalidating any earlier one. No-op when the account has no address or is already verified. Rate limited.

**Auth required:** Yes

**Response `202`:** Accepted.

---

#### `POST /api/users/verify-email`

Confirms an address using the token from the verification email. Tokens are single-use and expire after 24 hours. Rate limited.

**Auth required:** No

**Request body:**

```json
{ "token": "string (required)" }
```

**Response `204`:** Verified.

**Response `400`:** Token invalid, expired, or already used.

**Response `409`:** Address already verified on another account.

---

#### `POST /api/users/forgot-password`

Sends a reset link, but only to an address that has already been verified. Rate limited.

**Auth required:** No

**Request body:**

```json
{ "email": "string (required, valid email)" }
```

**Response `202`:** Always returned — identical for unknown, unverified, and known addresses so the endpoint can't be used to discover registered emails.

---

#### `POST /api/users/reset-password`

Sets a new password using the token from the reset email. Tokens are single-use and expire after 1 hour. All refresh tokens for the account are revoked. Rate limited.

**Auth required:** No

**Request body:**

```json
{
  "token": "string (required)",
  "newPassword": "string (required, min 8, max 100)"
}
```

**Response `204`:** Password changed.

**Response `400`:** Token invalid, expired, or already used.

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
  {
    "gameId": "snake",
    "username": "alice",
    "score": 9800,
    "timestamp": 1750000000000
  },
  {
    "gameId": "snake",
    "username": "bob",
    "score": 7200,
    "timestamp": 1749000000000
  }
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

### `ProfileResponse`

Returned by the `/api/users/me` endpoints. Only ever served to the account's owner.

```json
{
  "username": "string",
  "email": "string | null",
  "emailVerified": false,
  "optedIn": true,
  "emailListSubscribed": false,
  "role": "User | Admin",
  "createdAt": "2026-01-01T00:00:00Z"
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

| Table           | Key columns                                                                                                                                                               |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Users`         | `Id` (guid PK), `Username` (unique), `Email` (unique among verified addresses), `EmailVerifiedAt`, `EmailListSubscribed`, `PasswordHash`, `Role`, `OptedIn`               |
| `UserTokens`    | `Id` (guid PK), `UserId` (FK → Users, cascade), `TokenHash` (SHA-256 of the emailed token), `Purpose` (`EmailVerification` \| `PasswordReset`), `ExpiresAt`, `ConsumedAt` |
| `Scores`        | `Id` (guid PK), `GameId`, `Username`, `UserId` (FK → Users, nullable), `ScoreValue`, `Timestamp` — unique index on `(UserId, GameId)` where `UserId IS NOT NULL`          |
| `RefreshTokens` | `Id`, `Token` (unique), `Username`, `ExpiresAt`, `IsRevoked`                                                                                                              |

---

## Environment variables (Render)

| Variable                               | Purpose                                                                  |
| -------------------------------------- | ------------------------------------------------------------------------ |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string, in Npgsql keyword form (not a `postgres://` URI) |
| `Jwt__Secret`                          | HMAC-SHA256 signing key (256-bit random)                                 |
| `Jwt__Issuer`                          | Token issuer claim (default: `jeemzu-api`)                               |
| `Jwt__Audience`                        | Token audience claim (default: `jeemzu-frontend`)                        |
| `OpenAI__ApiKey`                       | Chat completions and embeddings                                          |
| `Agents__BaseUrl`                      | Private-network address of the Python agents service                     |
| `InternalApiKey`                       | Shared secret the agents service sends as `X-Internal-Key`               |
| `Resend__ApiKey`                       | Enables contact, verification, and password reset email                  |
| `Resend__AccountFrom`                  | From address for account email (falls back to `Resend__From`)            |
| `Frontend__BaseUrl`                    | Base URL for verification and reset links (default: `https://jeemzu.me`) |
| `Seed__AdminUsername`                  | One-time admin bootstrap; the seeder no-ops once the user exists         |
| `Seed__AdminEmail`                     | Email for the seeded admin account                                       |
| `Seed__AdminPassword`                  | Temporary password; unset means no seeding, and there is no default      |
| `PORT`                                 | Port the container listens on (`8080`)                                   |

---

## CORS

Allowed origins:

- `https://jeemzu.me`
- `https://www.jeemzu.me`
- `http://localhost:5173` (Vite dev server)

`AllowCredentials()` is enabled — required for the httpOnly refresh token cookie. The API is served
from `api.jeemzu.me`, which is same-site with the frontend, so the cookie uses `SameSite=Lax`.
