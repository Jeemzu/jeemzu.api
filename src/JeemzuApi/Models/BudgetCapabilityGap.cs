namespace JeemzuApi.Models;

/// <summary>
/// Something a user asked the Budgetize assistant to do that the tool cannot
/// model yet. Logged rather than discarded so unmet demand becomes a ranked
/// feature backlog instead of a one-off chat message.
/// </summary>
public class BudgetCapabilityGap
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>What the user actually asked for, in their own words.</summary>
    public string RequestText { get; set; } = string.Empty;

    /// <summary>The assistant's explanation of why it can't be modelled today.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Short normalized feature name, so repeat requests group together.</summary>
    public string SuggestedFeature { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
