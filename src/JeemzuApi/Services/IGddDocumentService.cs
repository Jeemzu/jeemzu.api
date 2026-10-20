using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public enum GddSaveStatus { Saved, NotFound, Conflict, QuotaExceeded }
public sealed record GddSaveResult(GddSaveStatus Status, GddDocumentResponse? Document = null);
public sealed record GddFile(byte[] Data, string Title, Guid Revision);

public interface IGddDocumentService
{
    Task<Guid?> ResolveUserAsync(string username, CancellationToken ct);
    Task<GddLibraryResponse> ListAsync(Guid userId, CancellationToken ct);
    Task<GddDocumentResponse?> GetAsync(Guid userId, Guid id, CancellationToken ct);
    Task<GddFile?> GetFileAsync(Guid userId, Guid id, bool draft, CancellationToken ct);
    Task<GddSaveResult> SaveAsync(Guid userId, Guid id, string title, byte[] pdf, byte[] draft, Guid? revision, bool create, CancellationToken ct);
    Task<bool> DeleteAsync(Guid userId, Guid id, Guid revision, CancellationToken ct);
}
