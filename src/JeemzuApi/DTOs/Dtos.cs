using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace JeemzuApi.DTOs;

// ── Scores ──────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/scores.
/// Username is NOT accepted from the client — it is taken from the authenticated
/// user's JWT claim to prevent score spoofing.
/// </summary>
public class SubmitScoreRequest
{
    [Required]
    [MaxLength(100)]
    public string GameId { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Score { get; set; }

    /// <summary>Unix timestamp in milliseconds — supplied by the client.</summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Returned from GET /api/scores/{gameId}.
/// Must match the TypeScript GameHighScore type:
///   { gameId: string, username: string, score: number, timestamp: number }
/// </summary>
public class ScoreResponse
{
    public string GameId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Score { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// Returned from GET /api/scores/{gameId}/summary.
/// Provides all-time record and the requesting user's personal best in one call.
/// PersonalBest is null when the request is unauthenticated.
/// </summary>
public class GameSummaryResponse
{
    public ScoreResponse? AllTimeRecord { get; set; }
    public int? PersonalBest { get; set; }
}

// ── Users ────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/users and POST /api/users/me/preferences.
/// Username is taken from the JWT claim. Omitted fields are left unchanged.
/// </summary>
public class UpdateUserRequest
{
    public bool? OptedIn { get; set; }

    /// <summary>Opt in/out of the notification mailing list.</summary>
    public bool? EmailListSubscribed { get; set; }
}

/// <summary>Request body for POST /api/users/register.</summary>
public class RegisterRequest
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Recovery address. Must be verified before it can reset the password.</summary>
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    public bool OptedIn { get; set; }

    public bool EmailListSubscribed { get; set; }
}

/// <summary>Request body for POST /api/users/me/username.</summary>
public class ChangeUsernameRequest
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/users/me/password.</summary>
public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/users/forgot-password.</summary>
public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/users/reset-password.</summary>
public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/users/verify-email.</summary>
public class VerifyEmailRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Returned from the authenticated /api/users/me endpoints. Unlike UserResponse
/// this includes private account settings, so it is never served for another user.
/// </summary>
public class ProfileResponse
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public bool OptedIn { get; set; }
    public bool EmailListSubscribed { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Response shape for GET /api/users/{username}.
/// Must match the TypeScript UserGameData type:
///   { userId?, username, optedIn, highScores: Record&lt;string, number&gt; }
/// </summary>
public class UserResponse
{
    public string? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool OptedIn { get; set; }

    /// <summary>
    /// Maps gameId → that user's highest score for the game.
    /// Populated by joining the Scores table on Username.
    /// </summary>
    public Dictionary<string, int> HighScores { get; set; } = [];
}

// ── Auth ──────────────────────────────────────────────────────────────────────

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Returned from POST /api/auth/login and POST /api/auth/refresh.
/// The refresh token itself is set as an httpOnly cookie, not in this body.
/// </summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Seconds until the access token expires.</summary>
    public int ExpiresIn { get; set; }
    public string Role { get; set; } = string.Empty;
}

// ── Chat ──────────────────────────────────────────────────────────────────────

/// <summary>
/// A single message in a conversation. Role must be "user" or "assistant",
/// mirroring the OpenAI chat message convention so the client payload is
/// immediately familiar to anyone who has used the OpenAI API.
/// </summary>
public class ConversationMessage
{
    [Required]
    [AllowedValues("user", "assistant")]
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/chat.</summary>
public class ChatRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(2000)]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Prior conversation turns, oldest first.
    /// Omit or send an empty array to start a fresh conversation.
    /// The server is stateless — the client owns the history.
    /// </summary>
    public List<ConversationMessage> History { get; set; } = [];
}

/// <summary>Response from POST /api/chat.</summary>
public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
}

/// <summary>Response from POST /api/admin/knowledge/ingest.</summary>
public class IngestResponse
{
    public int ChunksUpserted { get; set; }
}

// ── Knowledge Search ─────────────────────────────────────────────────────────

/// <summary>
/// A single knowledge chunk returned from GET /api/knowledge/search.
/// Contains raw content retrieved via vector similarity — no LLM processing.
/// </summary>
public class KnowledgeSearchResult
{
    public string SourceKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>Response from GET /api/knowledge/search.</summary>
public class KnowledgeSearchResponse
{
    public List<KnowledgeSearchResult> Results { get; set; } = [];
    public int TotalResults { get; set; }
}

// ── Admin ─────────────────────────────────────────────────────────────────────

/// <summary>Returned from GET /api/admin/users — one entry per user.</summary>
public class AdminUserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool OptedIn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Request body for PATCH /api/admin/users/{username}/role.</summary>
public class UpdateRoleRequest
{
    [Required]
    [AllowedValues("User", "Admin")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>A knowledge chunk summary for the admin viewer.</summary>
public class AdminKnowledgeChunkResponse
{
    public Guid Id { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Response from GET /api/admin/knowledge/chunks.</summary>
public class AdminKnowledgeListResponse
{
    public List<AdminKnowledgeChunkResponse> Chunks { get; set; } = [];
    public int TotalChunks { get; set; }
}

/// <summary>Health status of an external service.</summary>
public class ServiceHealthStatus
{
    public string Service { get; set; } = string.Empty;
    public bool Healthy { get; set; }
    public int? ResponseTimeMs { get; set; }
    public string? Error { get; set; }
}

// ── Budget (admin-only personal budget snapshot) ──────────────────────────────

/// <summary>Which account a bill or debt payment is drafted from: "shared" or "autopay".</summary>
public static class BudgetAccountSource
{
    public const string Pattern = "^(shared|autopay)$";

    /// <summary>One-off entries may also land in a person's own checking account.</summary>
    public const string OneOffPattern = "^(shared|autopay|personal)$";
}

/// <summary>How often a bill or debt repeats. Anything but "monthly" needs an anchor date.</summary>
public static class BudgetRecurrence
{
    public const string FrequencyPattern = "^(monthly|weekly|biweekly|quarterly|annual)$";
    public const string DatePattern = @"^\d{4}-\d{2}-\d{2}$";
}

/// <summary>One month of gross pay for one person. Months with no entry have no known income.</summary>
public class BudgetMonthlyIncomeDto
{
    [Range(1900, 2999)]
    public int Year { get; set; }

    /// <summary>0-based month index.</summary>
    [Range(0, 11)]
    public int Month { get; set; }

    [Range(1, 6)]
    public int PaycheckCount { get; set; }

    [Range(0, int.MaxValue)]
    public int PerPaycheckCents { get; set; }
}

public class BudgetPersonDto
{
    [Required]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gross pay per month; months absent from this list are left blank in the projection.</summary>
    [MaxLength(600)]
    public List<BudgetMonthlyIncomeDto> Schedule { get; set; } = new();

    /// <summary>Signed — a checking account can be overdrawn.</summary>
    public int PersonalBalanceCents { get; set; }

    /// <summary>Fixed monthly auto-pay contribution; null means proportional funding.</summary>
    [Range(0, int.MaxValue)]
    public int? AutopayLockedMonthlyCents { get; set; }

    /// <summary>Fixed monthly essentials contribution; null means proportional funding.</summary>
    [Range(0, int.MaxValue)]
    public int? EssentialsLockedMonthlyCents { get; set; }
}

public class BudgetBillDto
{
    [Required]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Frontend section assignment only; subscriptions are budgeted and stored as bills.</summary>
    public bool IsSubscription { get; set; }

    [Range(0, int.MaxValue)]
    public int AmountCents { get; set; }

    [Range(1, 31)]
    public int DueDay { get; set; }

    [Required]
    [RegularExpression(BudgetAccountSource.Pattern)]
    public string PaidFrom { get; set; } = "shared";

    [Required]
    [RegularExpression(BudgetRecurrence.FrequencyPattern)]
    public string Frequency { get; set; } = "monthly";

    /// <summary>First occurrence; required for every frequency except monthly.</summary>
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? AnchorISO { get; set; }

    /// <summary>Inclusive bounds on when the bill is active at all; null means unbounded.</summary>
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? StartISO { get; set; }

    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? EndISO { get; set; }
}

public class BudgetDebtDto
{
    [Required]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int BalanceCents { get; set; }

    [Range(0, int.MaxValue)]
    public int MinPaymentCents { get; set; }

    /// <summary>Promo payoff amount; null when the account has no promotion.</summary>
    [Range(0, int.MaxValue)]
    public int? SuggestedPaymentCents { get; set; }

    public bool HasPromotion { get; set; }

    /// <summary>Reference only — no interest math is done with these.</summary>
    [Range(0, int.MaxValue)]
    public int? InterestRateBps { get; set; }

    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? PromoEndISO { get; set; }

    [Range(0, int.MaxValue)]
    public int? PostPromoRateBps { get; set; }

    [Range(1, 31)]
    public int DueDay { get; set; }

    [Required]
    [RegularExpression(BudgetAccountSource.Pattern)]
    public string PaidFrom { get; set; } = "autopay";

    [Required]
    [RegularExpression(BudgetRecurrence.FrequencyPattern)]
    public string Frequency { get; set; } = "monthly";

    /// <summary>First occurrence; required for every frequency except monthly.</summary>
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? AnchorISO { get; set; }

    /// <summary>Inclusive bounds on when the debt is active at all; null means unbounded.</summary>
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? StartISO { get; set; }

    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? EndISO { get; set; }
}

/// <summary>
/// A temporary change to one bill or debt over an inclusive date range, leaving
/// the underlying item untouched so it resumes once the range ends.
/// </summary>
public class BudgetOverrideDto
{
    [Required]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(bill|debt)$")]
    public string TargetKind { get; set; } = "bill";

    [Required]
    [MaxLength(64)]
    public string TargetId { get; set; } = string.Empty;

    [Required]
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string FromISO { get; set; } = string.Empty;

    [Required]
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string ToISO { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(skip|amount)$")]
    public string Mode { get; set; } = "skip";

    /// <summary>Replacement amount; null when the mode is "skip".</summary>
    [Range(0, int.MaxValue)]
    public int? AmountCents { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = string.Empty;
}

/// <summary>A single dated expense or deposit that does not repeat.</summary>
public class BudgetOneOffDto
{
    [Required]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(expense|income)$")]
    public string Kind { get; set; } = "expense";

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int AmountCents { get; set; }

    [Required]
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string DateISO { get; set; } = string.Empty;

    [Required]
    [RegularExpression(BudgetAccountSource.OneOffPattern)]
    public string Account { get; set; } = "shared";

    /// <summary>Required when Account is "personal"; null otherwise.</summary>
    [MaxLength(64)]
    public string? PersonId { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Mirrors the client-side BudgetData type. Collection caps bound the payload so a
/// single save cannot store an unbounded document.
/// </summary>
public class BudgetDataDto
{
    [Required]
    [MaxLength(200)]
    public List<BudgetPersonDto> People { get; set; } = [];

    [Required]
    [MaxLength(1000)]
    public List<BudgetBillDto> Bills { get; set; } = [];

    [Required]
    [MaxLength(1000)]
    public List<BudgetDebtDto> Debts { get; set; } = [];

    [Required]
    [MaxLength(2000)]
    public List<BudgetOverrideDto> Overrides { get; set; } = [];

    [Required]
    [MaxLength(2000)]
    public List<BudgetOneOffDto> OneOffs { get; set; } = [];

    /// <summary>Signed — a checking account can be overdrawn.</summary>
    public int EssentialsBalanceCents { get; set; }

    /// <summary>Signed — a checking account can be overdrawn.</summary>
    public int AutopayBalanceCents { get; set; }

    /// <summary>yyyy-mm-dd the balances are as of; null means the client's today.</summary>
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string? ProjectionStartISO { get; set; }

    [RegularExpression("^(suggested|minimum)$")]
    public string DebtStrategy { get; set; } = "suggested";
}

/// <summary>Request body for PUT /api/budget.</summary>
public class SaveBudgetRequest
{
    [Required]
    public BudgetDataDto Data { get; set; } = new();

    /// <summary>
    /// Revision returned by the last load or save. Null only when creating the
    /// first budget; a mismatch means another device saved first.
    /// </summary>
    public Guid? Revision { get; set; }
}

/// <summary>Returned from GET and PUT /api/budget.</summary>
public class BudgetSnapshotResponse
{
    public BudgetDataDto Data { get; set; } = new();
    public Guid Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ── Budget assistant ──────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/budget/chat.</summary>
public class BudgetChatRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(2000)]
    public string Question { get; set; } = string.Empty;

    [MaxLength(40)]
    public List<ConversationMessage> History { get; set; } = [];

    /// <summary>
    /// The budget as it currently sits in the browser, including unsaved edits,
    /// alongside the projection the client already computed from it.
    /// </summary>
    [Required]
    public BudgetDataDto Budget { get; set; } = new();

    /// <summary>Opaque to this API — forwarded to the assistant as the client produced it.</summary>
    [Required]
    public JsonElement Projection { get; set; }

    [Required]
    [RegularExpression(BudgetRecurrence.DatePattern)]
    public string Today { get; set; } = string.Empty;

    [RegularExpression("^(suggested|minimum)$")]
    public string Strategy { get; set; } = "suggested";
}

/// <summary>Payload forwarded to the Python assistant service.</summary>
public class BudgetChatPayload
{
    public string Question { get; set; } = string.Empty;
    public List<ConversationMessage> History { get; set; } = [];
    public BudgetDataDto Budget { get; set; } = new();
    public JsonElement Projection { get; set; }
    public string Today { get; set; } = string.Empty;
    public string Strategy { get; set; } = "suggested";
}

/// <summary>
/// The assistant's reply. Proposal and gap stay as raw JSON: the assistant already
/// emits the client's own shapes, so re-modelling them here would only add drift.
/// </summary>
public class BudgetChatResult
{
    public string Answer { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public JsonElement? Proposal { get; set; }
    public JsonElement? CapabilityGap { get; set; }
}

/// <summary>Request body for POST /api/budget/gaps.</summary>
public class LogBudgetGapRequest
{
    [Required]
    [MaxLength(2000)]
    public string Request { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string SuggestedFeature { get; set; } = string.Empty;
}

/// <summary>One row of GET /api/admin/budget/gaps — requests grouped by feature.</summary>
public class BudgetGapSummaryResponse
{
    public string SuggestedFeature { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public int UserCount { get; set; }
    public DateTimeOffset LastRequestedAt { get; set; }
    /// <summary>A few recent verbatim requests, for context.</summary>
    public List<string> Examples { get; set; } = [];
}

// ── Contact ───────────────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/contact.</summary>
public class ContactRequest
{
    [Required]
    [MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;
}
