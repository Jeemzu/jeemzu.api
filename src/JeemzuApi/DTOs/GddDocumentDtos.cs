using System.ComponentModel.DataAnnotations;

namespace JeemzuApi.DTOs;

/// <summary>An editable draft ZIP and an optional current PDF.</summary>
public sealed record SaveGddDocumentRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;
    public IFormFile? Pdf { get; init; }
    [Required]
    public IFormFile Draft { get; init; } = null!;
    public Guid? Revision { get; init; }
}

/// <summary>Metadata for a saved GDD; binary content is downloaded separately.</summary>
public sealed record GddDocumentResponse(
    Guid Id, string Title, Guid Revision, long PdfBytes, long DraftBytes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>The caller's document library and actual PDF-plus-ZIP storage usage.</summary>
public sealed record GddLibraryResponse(
    IReadOnlyList<GddDocumentResponse> Documents, long UsedBytes, long LimitBytes);
