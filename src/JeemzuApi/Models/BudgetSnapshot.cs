namespace JeemzuApi.Models;

/// <summary>
/// One saved budget per user, stored as validated JSON so the budget schema can
/// evolve on the client without a migration. Revision guards against a stale
/// browser tab overwriting a newer save from another machine.
/// </summary>
public class BudgetSnapshot
{
    /// <summary>Owning user — also the primary key, so a user has at most one budget.</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string DataJson { get; set; } = string.Empty;

    /// <summary>Changes on every successful save; clients must echo it back to update.</summary>
    public Guid Revision { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
