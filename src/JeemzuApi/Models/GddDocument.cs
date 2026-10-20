namespace JeemzuApi.Models;

public sealed class GddDocument
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Title { get; set; } = string.Empty;
    public byte[] PdfData { get; set; } = [];
    public byte[] DraftData { get; set; } = [];
    public long PdfBytes { get; set; }
    public long DraftBytes { get; set; }
    public Guid Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
