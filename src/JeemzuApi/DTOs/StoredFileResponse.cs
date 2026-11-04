namespace JeemzuApi.DTOs;

/// <summary>Metadata for a private file; file content is returned only by the download endpoint.</summary>
public sealed record StoredFileResponse(Guid Id, string Name, long SizeBytes, DateTimeOffset CreatedAt);
