using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public sealed class FileStorageService(AppDbContext db) : IFileStorageService
{
    public const int MaxFileBytes = 25 * 1024 * 1024;
    public const int MaxRequestBytes = MaxFileBytes + 64 * 1024;

    public Task<Guid?> FindAdminIdAsync(string username, CancellationToken ct) =>
        db.Users.Where(u => u.Username == username && u.Role == "Admin")
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);

    public Task<List<StoredFileResponse>> ListAsync(Guid userId, CancellationToken ct) =>
        db.StoredFiles.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new StoredFileResponse(f.Id, f.Name, f.SizeBytes, f.CreatedAt))
            .ToListAsync(ct);

    public async Task<StoredFileResponse> UploadAsync(
        Guid userId, string name, Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > MaxFileBytes)
                throw new BadHttpRequestException("Files must be 25 MB or smaller.", StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        var file = new StoredFile
        {
            UserId = userId,
            Name = name,
            SizeBytes = buffer.Length,
            Content = buffer.ToArray(),
        };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync(ct);
        return new StoredFileResponse(file.Id, file.Name, file.SizeBytes, file.CreatedAt);
    }

    public async Task<(string Name, byte[] Content)?> DownloadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var file = await db.StoredFiles.AsNoTracking()
            .Where(f => f.UserId == userId && f.Id == id)
            .Select(f => new { f.Name, f.Content })
            .SingleOrDefaultAsync(ct);
        return file is null ? null : (file.Name, file.Content);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.StoredFiles.Where(f => f.UserId == userId && f.Id == id)
            .ExecuteDeleteAsync(ct) != 0;
}
