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

### GDD assistant - `/api/gdd-assistant`

`POST` requires a JWT. Returns `200` with `Cache-Control: no-store`; it creates no server-side document and does not approve changes.

- Request: `{ mode, checkpoint, prompt, document, history, references }`. Mode: `scratch` (default when omitted) or `fill`; other values return `400`. Checkpoints: `concept`, `gameplay`, `world`, `presentation`, `technical`, `release`.
- `prompt`: 1–4,000 characters; `document`: 1–100,000 characters, including the current draft and any pending proposal; `history`: up to 12 `{ role: "user" | "assistant", content }` turns, each up to 8,000 characters and 32,000 combined.
- References: up to 12 `{ name, text, url, imageBase64 }` objects; names up to 200 characters, text up to 20,000 each / 60,000 combined, HTTP(S) URL up to 2,000, and base64 JPEG up to 256 KiB decoded. Empty text/URL/image strings are allowed. At most four images per request. Links are never fetched; no remote image URLs or arbitrary model IDs are accepted.
- Response: `{ message, questions, referenceRequests, proposal: { title, introduction, sections: [{ title, items: [{ heading, body }] }] } }`. Only current-checkpoint sections may be proposed; cover fields are reserved for `concept`. In `scratch` mode proposed text replaces text in listed sections after client-side approval. In `fill` mode items replace matching subsection headings or append new subsections; omitted items remain unchanged. Subsection headings must be unique within each proposed section. Image categories, prototype controls and other sections are preserved. Markdown body headings and image markup are rejected.
- Limits: 2 MiB request, 12 requests per account per five minutes, 90-second deadline, 10,000 output tokens, no automatic retries at the workflow level. Model configuration: `OpenAI:GddModel` (default `gpt-6-luna`), independent from `OpenAI:ChatModel`.
- Credentials: GDD requests use only `OpenAI:GddApiKey` (`OpenAI__GddApiKey` in Render), separate from the profile chatbot/embedding `OpenAI:ApiKey`. A missing or blank GDD key disables only the assistant with `503`; there is no fallback to the chatbot key. Set the key as a server secret and restart/redeploy; never put it in frontend variables or source control.
- Errors: `400` invalid input, `401` invalid session, `413` oversized request, `429` account request limit, `502` provider failure/refusal/invalid proposal, `503` missing dedicated key, `504` timeout. Handled completion errors return ProblemDetails without changing the draft.
- Mode, checkpoint prompts, recent history, references and pending proposals persist inside optional `draft.assistant` version-1 editor metadata. Legacy workflows without a mode load as `scratch`. Fill mode supports independent checkpoint selection and approvals; scratch mode retains ordered approvals. Existing account saves store the ZIP unchanged under the same quota; no database migration is needed. Provider access and output quality require a separate live smoke check.

### GDD library - `/api/gdd-documents`

All routes require a JWT and resolve ownership from the authenticated username. Another user's document returns `404`. Responses use `Cache-Control: no-store`.

| Method | Path | Result |
| ------ | ---- | ------ |
| `GET` | `/api/gdd-documents` | `200 { documents, usedBytes, limitBytes }` |
| `GET` | `/api/gdd-documents/{id}` | `200` document metadata |
| `POST` | `/api/gdd-documents` | Multipart create; `201` metadata and Location |
| `PUT` | `/api/gdd-documents/{id}` | Multipart replace; `200` metadata with a new revision |
| `GET` | `/api/gdd-documents/{id}/pdf?revision={revision}` | Download PDF |
| `GET` | `/api/gdd-documents/{id}/draft?revision={revision}` | Download editable ZIP and original images |
| `DELETE` | `/api/gdd-documents/{id}?revision={revision}` | `204`, releasing storage |

- Metadata: `id` and `revision` UUIDs, `title`, `pdfBytes`, `draftBytes`, `createdAt`, and `updatedAt` (UTC timestamps). Listing does not load binaries.
- Save fields: `title` (1-100 characters, single line), `pdf` (file), `draft` (file), and `revision` (required on update, omitted on create). The ZIP must contain version-1 `jeemzu-gdd-editor` metadata whose game title matches `title`. Download both files using the same revision to restore a consistent editable document.
- Storage: 100 MiB per account, including PDFs and compressed draft ZIPs. PDF maximum: 20 MiB; ZIP: 25 MiB; complete multipart request: 46 MiB. Updates subtract the replaced version before charging the new bytes. ZIP validation also bounds paths, types, entries, and expanded sizes.
- Errors: `400` invalid upload/revision, `401` invalid session, `404` missing/foreign document, `409` stale revision, `413` file/account limit, `429` account-user rate limit (shared 10 requests per 5 minutes). Validation, conflict, and quota responses include ProblemDetails `detail`. A quota race cannot allow simultaneous uploads to exceed the account limit.
- Deleting an account cascade-deletes its documents. There is no automatic upload, PDF-only import, or version history; updates replace the PDF and editable ZIP together.

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

## Budgetize assistant - `/api/budget/chat`

- Authenticated `POST` with `{question, history, budget, projection, today, strategy}`; the endpoint and approval-only browser flow are unchanged.
- `question`: 1-2,000 characters; `history`: up to 40 user/assistant messages, 8,000 characters/turn and 64,000 combined. The workflow reads only the last six. `today` is a real `yyyy-mm-dd` date; strategy is `suggested` or `minimum`.
- `budget` is the current unsaved `BudgetDataDto`; `projection` is the client's authoritative weekly forecast. People must match the budget, with at most 32 weeks. Daily ledgers are omitted from prompts; compact context is limited to 150,000 characters.
- Response: `{answer, intent, proposal, capabilityGap}`, where intent is `question`, `change_request` or `unsupported`. A proposal is `{summary, ops}` (up to 25 operations); optional operation fields are omitted, never null. Existing IDs and whole-cent amounts are validated before returning proposals.
- The native .NET router and selected analyst/planner/gap stage each make one structured completion with the dedicated Budgetize key/model. The server never applies operations or saves gaps: preview/apply and `POST /api/budget/gaps` remain separate.
- Errors: `400` invalid input/context, `401` unauthorized, `413` body over 2 MB, `429` account/process capacity, `502` provider/structured-output failure, `503` missing dedicated key, `504` 90-second deadline. Controller errors use Problem Details; middleware limits/authentication may return empty bodies. Results are not cached.
- Limit: 12 requests/account/5 minutes and four active workflows/process, no queue. Configuration health is exposed to admins without a billed provider check.
- See [Budgetize workflow and deployment](README.md#budgetize-assistant-net) for supported operations and retiring the former service.

## Environment variables (Render)

| Variable                               | Purpose                                                                        |
| -------------------------------------- | ------------------------------------------------------------------------------ |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string, in Npgsql keyword form (not a `postgres://` URI) |
| `Jwt__Secret`                          | HMAC-SHA256 signing key (256-bit random)                                       |
| `Jwt__Issuer`                          | Token issuer claim (default: `jeemzu-api`)                                     |
| `Jwt__Audience`                        | Token audience claim (default: `jeemzu-frontend`)                              |
| `OpenAI__ApiKey`                       | Profile chatbot completions and embeddings                                     |
| `OpenAI__GddApiKey`                    | Dedicated GDD assistant key; missing/blank disables only GDD AI (`503`)          |
| `OpenAI__BudgetApiKey`                 | Dedicated native Budgetize key; missing/blank disables only Budgetize AI (`503`) |
| `OpenAI__BudgetModel`                  | Model for every Budgetize stage (default `gpt-6-luna`)                         |
| `InternalApiKey`                       | Existing knowledge-search `X-Internal-Key` contract (not used by Budgetize)     |
| `Resend__ApiKey`                       | Enables contact, verification, and password reset email                        |
| `Resend__AccountFrom`                  | From address for account email (falls back to `Resend__From`)                  |
| `Frontend__BaseUrl`                    | Base URL for verification and reset links (default: `https://jeemzu.me`)       |
| `Seed__AdminUsername`                  | One-time admin bootstrap; the seeder no-ops once the user exists               |
| `Seed__AdminEmail`                     | Email for the seeded admin account                                             |
| `Seed__AdminPassword`                  | Temporary password; unset means no seeding, and there is no default            |
| `PORT`                                 | Port the container listens on (`8080`)                                         |

---

## CORS

Allowed origins:

- `https://jeemzu.me`
- `https://www.jeemzu.me`
- `http://localhost:5173` (Vite dev server)

`AllowCredentials()` is enabled — required for the httpOnly refresh token cookie. The API is served
from `api.jeemzu.me`, which is same-site with the frontend, so the cookie uses `SameSite=Lax`.
