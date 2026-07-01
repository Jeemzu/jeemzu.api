using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace JeemzuApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Score> Scores => Set<Score>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PartyMember> PartyMembers => Set<PartyMember>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ensure the pgvector extension exists in the database.
        // EF will emit "CREATE EXTENSION IF NOT EXISTS vector" in the migration.
        modelBuilder.HasPostgresExtension("vector");

        // KnowledgeChunk — stores text chunks with their vector embeddings for RAG retrieval
        modelBuilder.Entity<KnowledgeChunk>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.SourceKey).IsRequired().HasMaxLength(200);
            entity.Property(k => k.Content).IsRequired();
            entity.Property(k => k.Embedding).HasColumnType("vector(1536)");

            // Unique constraint so ingestion can reliably upsert by source key
            entity.HasIndex(k => k.SourceKey).IsUnique();

            // HNSW index for fast approximate cosine similarity search.
            // m=16 (connections per layer) and ef_construction=64 (build-time search depth)
            // are sensible defaults for a small-to-medium knowledge base.
            entity.HasIndex(k => k.Embedding)
                  .HasMethod("hnsw")
                  .HasOperators("vector_cosine_ops")
                  .HasStorageParameter("m", 16)
                  .HasStorageParameter("ef_construction", 64);
        });

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

        // Party configuration
        modelBuilder.Entity<Party>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Code).IsRequired().HasMaxLength(10);
            entity.Property(p => p.Status).IsRequired().HasMaxLength(20);
            entity.Property(p => p.RpgSessionId).HasMaxLength(100);
            entity.Property(p => p.CurrentGamePhase).IsRequired().HasMaxLength(20);
            entity.Property(p => p.CurrentTurnUsername).HasMaxLength(50);
            // Not unique globally — completed parties may share a historical code — but
            // PartyService only checks uniqueness against non-completed parties.
            entity.HasIndex(p => p.Code);
            entity.HasOne<Campaign>()
                  .WithMany()
                  .HasForeignKey(p => p.CampaignId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // PartyMember configuration
        modelBuilder.Entity<PartyMember>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Username).IsRequired().HasMaxLength(50);
            entity.Property(m => m.CharacterName).IsRequired().HasMaxLength(50);
            entity.Property(m => m.CharacterClass).IsRequired().HasMaxLength(20);
            entity.Property(m => m.ConnectionId).HasMaxLength(100);
            entity.Property(m => m.ControlledByUsername).HasMaxLength(50);
            entity.HasIndex(m => m.ConnectionId);
            entity.HasOne(m => m.Party)
                  .WithMany(p => p.Members)
                  .HasForeignKey(m => m.PartyId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Campaign — host-owned RPG save file
        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
            entity.Property(c => c.GameStateJson).IsRequired();
            entity.Property(c => c.CharacterSummaryJson).IsRequired();
            entity.Property(c => c.CurrentLocation).HasMaxLength(100);
            entity.Property(c => c.Status).IsRequired().HasMaxLength(20);
            entity.HasIndex(c => c.HostUserId);
            entity.HasOne(c => c.HostUser)
                  .WithMany()
                  .HasForeignKey(c => c.HostUserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
