using JeemzuApi.Data;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

/// <summary>
/// Idempotent startup seeding so a brand-new database is usable immediately: the owner's
/// admin account, then the RAG knowledge base. Both steps no-op once their data exists.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        await SeedAdminAsync(db, config, logger, ct);
        await SeedKnowledgeAsync(db, services, logger, ct);
    }

    private static async Task SeedAdminAsync(
        AppDbContext db, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var username = config["Seed:AdminUsername"];
        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];

        // An unset password means no seeding. There is deliberately no default: a hard-coded
        // credential would be permanent in git history and these repos are public.
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Admin seed skipped — Seed:AdminUsername or Seed:AdminPassword is not configured.");
            return;
        }

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
        {
            logger.LogInformation("Admin seed skipped — user '{Username}' already exists.", username);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        db.Users.Add(new User
        {
            Username = username,
            Email = normalizedEmail,
            // Password reset requires a verified address, and the unique email index only reserves
            // verified ones — so the seeded owner has to start verified or recovery is impossible.
            EmailVerifiedAt = normalizedEmail is null ? null : now,
            Role = "Admin",
            OptedIn = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded admin user '{Username}'. Rotate the temporary password.", username);
    }

    private static async Task SeedKnowledgeAsync(
        AppDbContext db, IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        if (await db.KnowledgeChunks.AnyAsync(ct))
        {
            return;
        }

        try
        {
            var count = await services.GetRequiredService<IIngestionService>().IngestAsync(ct);
            logger.LogInformation("Seeded {Count} knowledge chunks from about-me.json.", count);
        }
        catch (Exception ex)
        {
            // Embedding needs a working OpenAI key; losing the chatbot's context is bad but
            // it must not stop the rest of the API from booting.
            logger.LogError(ex,
                "Knowledge seed failed. Run POST /api/admin/knowledge/ingest once resolved.");
        }
    }
}
