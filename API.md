# JeemzuAPI Reference

ASP.NET Core 8 REST API backing [jeemzu.me](https://jeemzu.me). Handles user accounts, authentication, and site services.

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

### Private Admin Files — `/api/admin/files`

Requires an Admin JWT and an account that is still an admin in the database. All operations are scoped to that account; another account's file ID returns `404`.

| Method   | Path                    | Success | Request / response                                                                 |
| -------- | ----------------------- | ------- | ---------------------------------------------------------------------------------- |
| `GET`    | `/api/admin/files`      | `200`   | Array of `{ id, name, sizeBytes, createdAt }`, newest first; no content            |
| `POST`   | `/api/admin/files`      | `201`   | `multipart/form-data` field `file`; returns metadata and a `Location` download URL |
| `GET`    | `/api/admin/files/{id}` | `200`   | Binary attachment, `application/octet-stream`, original filename                   |
| `DELETE` | `/api/admin/files/{id}` | `204`   | Permanently removes the file                                                       |

Any file type is allowed, including empty files, up to 25 MB (26,214,400 bytes). Names must have 1-255 characters without control characters; path components are stripped. Duplicate names create separate files. Invalid uploads return `400`; oversized requests may return `413` or multipart validation `400`. Missing authentication returns `401`; non-admin access returns `403`. Downloads are private and non-cacheable; access tokens must never be placed in URLs.

See [admin-files.http](admin-files.http) for manual requests and the [storage notes](README.md#private-admin-files--apiadminfiles) for deployment and database capacity considerations.

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

Renames the account, revokes refresh tokens, and issues a fresh token pair.

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

Fetches a user's profile.

**Auth required:** No

**Response `200`:**

```json
{
  "userId": "guid",
  "username": "string"
}
```

**Response `404`:** User not found.

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
  "username": "string"
}
```

### `ProfileResponse`

Returned by the `/api/users/me` endpoints. Only ever served to the account's owner.

```json
{
  "username": "string",
  "email": "string | null",
  "emailVerified": false,
  "emailListSubscribed": false,
  "role": "User | Admin",
  "createdAt": "2026-01-01T00:00:00Z"
}
```

---

## Database schema (summary)

| Table           | Key columns                                                                                                                                                               |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Users`         | `Id` (guid PK), `Username` (unique), `Email` (unique among verified addresses), `EmailVerifiedAt`, `EmailListSubscribed`, `PasswordHash`, `Role`                          |
| `UserTokens`    | `Id` (guid PK), `UserId` (FK → Users, cascade), `TokenHash` (SHA-256 of the emailed token), `Purpose` (`EmailVerification` \| `PasswordReset`), `ExpiresAt`, `ConsumedAt` |
| `RefreshTokens` | `Id`, `Token` (unique), `Username`, `ExpiresAt`, `IsRevoked`                                                                                                              |

---

## Budgetize assistant - `/api/budget/chat`

- Admin-only `POST` with `{question, history, budget, projection, today, strategy}`. Both the token and current account role must be Admin; the approval-only browser flow is unchanged.
- `question`: 1-2,000 characters; `history`: up to 40 user/assistant messages, 8,000 characters/turn and 64,000 combined. The workflow reads only the last six. `today` is a real `yyyy-mm-dd` date; strategy is `suggested` or `minimum`.
- `budget` is the current unsaved `BudgetDataDto`; `projection` is the client's authoritative weekly forecast. People must match the budget, with at most 32 weeks. Daily ledgers are omitted from prompts; compact context is limited to 150,000 characters.
- Response: `{answer, intent, proposal, capabilityGap}`, where intent is `question`, `change_request` or `unsupported`. A proposal is `{summary, ops}` (up to 25 operations); optional operation fields are omitted, never null. Existing IDs and whole-cent amounts are validated before returning proposals.
- The native .NET router and selected analyst/planner/gap stage each make one structured completion with the shared OpenAI key and Budgetize model. The server never applies operations or saves gaps: preview/apply and `POST /api/budget/gaps` remain separate.
- Errors: `400` invalid input/context, `401` unauthorized, `403` not an admin, `413` body over 2 MB, `429` account/process capacity, `502` provider/structured-output failure, `503` shared key unavailable, `504` 90-second deadline. Controller errors use Problem Details; middleware limits/authentication may return empty bodies. Results are not cached.
- Limit: 12 requests/account/5 minutes and four active workflows/process, no queue. Configuration health is exposed to admins without a billed provider check.
- See [Budgetize workflow and deployment](README.md#budgetize-assistant-net) for supported operations and retiring the former service.

## Environment variables (Render)

| Variable                               | Purpose                                                                          |
| -------------------------------------- | -------------------------------------------------------------------------------- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string, in Npgsql keyword form (not a `postgres://` URI)   |
| `Jwt__Secret`                          | HMAC-SHA256 signing key (256-bit random)                                         |
| `Jwt__Issuer`                          | Token issuer claim (default: `jeemzu-api`)                                       |
| `Jwt__Audience`                        | Token audience claim (default: `jeemzu-frontend`)                                |
| `OpenAI__ApiKey`                       | Shared by profile chat, Budgetize, and embeddings                                |
| `OpenAI__Model`                        | Shared chat/planning model (default `gpt-6-luna`)                                |
| `OpenAI__EmbeddingModel`               | Embedding model for RAG search (default `text-embedding-3-small`)                |
| `InternalApiKey`                       | Existing knowledge-search `X-Internal-Key` contract (not used by Budgetize)      |
| `Resend__ApiKey`                       | Enables contact, verification, and password reset email                          |
| `Resend__AccountFrom`                  | From address for account email (falls back to `Resend__From`)                    |
| `Frontend__BaseUrl`                    | Base URL for verification and reset links (default: `https://jeemzu.me`)         |
| `Seed__AdminUsername`                  | One-time admin bootstrap; the seeder no-ops once the user exists                 |
| `Seed__AdminEmail`                     | Email for the seeded admin account                                               |
| `Seed__AdminPassword`                  | Temporary password; unset means no seeding, and there is no default              |
| `PORT`                                 | Port the container listens on (`8080`)                                           |

---

## CORS

Allowed origins:

- `https://jeemzu.me`
- `https://www.jeemzu.me`
- `http://localhost:5173` (Vite dev server)

`AllowCredentials()` is enabled — required for the httpOnly refresh token cookie. The API is served
from `api.jeemzu.me`, which is same-site with the frontend, so the cookie uses `SameSite=Lax`.
