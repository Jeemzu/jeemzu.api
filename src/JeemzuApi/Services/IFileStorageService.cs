using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public interface IFileStorageService
{
    Task<Guid?> FindAdminIdAsync(string username, CancellationToken ct);
    Task<List<StoredFileResponse>> ListAsync(Guid userId, CancellationToken ct);
    Task<StoredFileResponse> UploadAsync(Guid userId, string name, Stream content, CancellationToken ct);
    Task<(string Name, byte[] Content)?> DownloadAsync(Guid userId, Guid id, CancellationToken ct);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct);
}
