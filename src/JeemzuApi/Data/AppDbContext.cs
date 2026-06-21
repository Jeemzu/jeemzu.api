using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Score> Scores => Set<Score>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Score configuration
        modelBuilder.Entity<Score>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.GameId).IsRequired().HasMaxLength(100);
            entity.Property(s => s.Username).IsRequired().HasMaxLength(50);
            entity.Property(s => s.ScoreValue).IsRequired();
            // Index for fast leaderboard queries: filter by GameId, order by ScoreValue DESC
            entity.HasIndex(s => new { s.GameId, s.ScoreValue });
            // One score per authenticated user per game (NULLs are distinct so guest scores are unaffected)
            entity.HasIndex(s => new { s.UserId, s.GameId })
                  .IsUnique()
                  .HasFilter("\"UserId\" IS NOT NULL");
            // Optional FK to User — nullable to support guest/legacy scores
            entity.HasOne(s => s.User)
                  .WithMany()
                  .HasForeignKey(s => s.UserId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).IsRequired().HasMaxLength(50);
            // Unique index so no two players can share a username
            entity.HasIndex(u => u.Username).IsUnique();
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Token).IsRequired().HasMaxLength(256);
            entity.Property(r => r.Username).IsRequired().HasMaxLength(50);
            entity.HasIndex(r => r.Token).IsUnique();
        });
    }
}
