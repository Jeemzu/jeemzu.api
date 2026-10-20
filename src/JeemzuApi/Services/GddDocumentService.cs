using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public sealed class GddDocumentService(AppDbContext db) : IGddDocumentService
{
    public Task<Guid?> ResolveUserAsync(string username, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(user => user.Username == username).Select(user => (Guid?)user.Id).SingleOrDefaultAsync(ct);

    public async Task<GddLibraryResponse> ListAsync(Guid userId, CancellationToken ct)
    {
        var documents = await db.GddDocuments.AsNoTracking().Where(document => document.UserId == userId)
            .OrderByDescending(document => document.UpdatedAt)
            .Select(document => new GddDocumentResponse(document.Id, document.Title, document.Revision,
                document.PdfBytes, document.DraftBytes, document.CreatedAt, document.UpdatedAt)).ToListAsync(ct);
        return new(documents, documents.Sum(document => document.PdfBytes + document.DraftBytes), GddUploadValidator.AccountLimit);
    }

    public Task<GddDocumentResponse?> GetAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.GddDocuments.AsNoTracking().Where(document => document.UserId == userId && document.Id == id)
            .Select(document => new GddDocumentResponse(document.Id, document.Title, document.Revision,
                document.PdfBytes, document.DraftBytes, document.CreatedAt, document.UpdatedAt)).SingleOrDefaultAsync(ct);

    public Task<GddFile?> GetFileAsync(Guid userId, Guid id, bool draft, CancellationToken ct) =>
        db.GddDocuments.AsNoTracking().Where(document => document.UserId == userId && document.Id == id)
            .Select(document => new GddFile(draft ? document.DraftData : document.PdfData, document.Title, document.Revision))
            .SingleOrDefaultAsync(ct);

    public async Task<GddSaveResult> SaveAsync(
        Guid userId, Guid id, string title, byte[] pdf, byte[] draft, Guid? revision, bool create, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize writes per account so parallel uploads cannot both spend the same free space.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE", ct);
        var existing = await db.GddDocuments.Where(document => document.UserId == userId && document.Id == id)
            .Select(document => new { document.Revision, document.PdfBytes, document.DraftBytes, document.CreatedAt })
            .SingleOrDefaultAsync(ct);
        if (!create && existing is null) return new(GddSaveStatus.NotFound);
        if ((create && existing is not null) || (!create && existing!.Revision != revision))
            return new(GddSaveStatus.Conflict);
        var used = await db.GddDocuments.Where(document => document.UserId == userId)
            .SumAsync(document => document.PdfBytes + document.DraftBytes, ct);
        if (used - (existing?.PdfBytes ?? 0) - (existing?.DraftBytes ?? 0) + pdf.LongLength + draft.LongLength > GddUploadValidator.AccountLimit)
            return new(GddSaveStatus.QuotaExceeded);
        var now = DateTimeOffset.UtcNow;
        var nextRevision = Guid.NewGuid();
        if (create)
        {
            db.GddDocuments.Add(new GddDocument
            {
                Id = id, UserId = userId, Title = title, PdfData = pdf, DraftData = draft,
                PdfBytes = pdf.LongLength, DraftBytes = draft.LongLength, Revision = nextRevision,
                CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            var rows = await db.GddDocuments
                .Where(document => document.UserId == userId && document.Id == id && document.Revision == revision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(document => document.Title, title)
                    .SetProperty(document => document.PdfData, pdf).SetProperty(document => document.DraftData, draft)
                    .SetProperty(document => document.PdfBytes, pdf.LongLength).SetProperty(document => document.DraftBytes, draft.LongLength)
                    .SetProperty(document => document.Revision, nextRevision).SetProperty(document => document.UpdatedAt, now), ct);
            if (rows == 0) return new(GddSaveStatus.Conflict);
        }
        await transaction.CommitAsync(ct);
        return new(GddSaveStatus.Saved, new(id, title, nextRevision, pdf.LongLength, draft.LongLength, existing?.CreatedAt ?? now, now));
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, Guid revision, CancellationToken ct) =>
        await db.GddDocuments.Where(document => document.UserId == userId && document.Id == id && document.Revision == revision)
            .ExecuteDeleteAsync(ct) > 0;
}
