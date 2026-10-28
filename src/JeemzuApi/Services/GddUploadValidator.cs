using System.IO.Compression;
using System.Text.Json;

namespace JeemzuApi.Services;

public static class GddUploadValidator
{
    public const long AccountLimit = 100L * 1024 * 1024;
    public const int PdfLimit = 20 * 1024 * 1024;
    public const int DraftLimit = 25 * 1024 * 1024;
    public const int RequestLimit = PdfLimit + DraftLimit + 1024 * 1024;

    public static string? Validate(string title, byte[] pdf, byte[] draft)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100 || title.Any(char.IsControl))
            return "Use a document title between 1 and 100 characters on a single line.";
        if (pdf.Length != 0 && (pdf.Length < 8 || pdf.Length > PdfLimit || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8)))
            return "Provide a PDF no larger than 20 MB.";
        if (draft.Length == 0 || draft.Length > DraftLimit)
            return "Provide an editable draft ZIP no larger than 25 MB.";
        try
        {
            using var archive = new ZipArchive(new MemoryStream(draft), ZipArchiveMode.Read);
            if (archive.Entries.Count > 100) return "The draft ZIP contains more than 100 entries.";
            long expanded = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var hasEditor = false;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (!names.Add(name) || name.StartsWith('/') || name.Contains('\\') ||
                    name.Contains(':') || name.Any(char.IsControl) ||
                    name.Split('/').Any(part => part is "." or ".."))
                    return "The draft ZIP contains duplicate or unsafe paths.";
                expanded += entry.Length;
                if (expanded > 60L * 1024 * 1024) return "The expanded draft exceeds 60 MB.";
                if (name.EndsWith('/')) continue;
                var metadata = name is "editor.json" or "gdd.md";
                var extension = Path.GetExtension(name).ToLowerInvariant();
                var image = name.StartsWith("images/", StringComparison.Ordinal) &&
                    name.Count(character => character == '/') == 1 && extension is ".png" or ".jpg" or ".jpeg";
                if (!metadata && !image) return "The draft ZIP contains an unsupported file.";
                var limit = metadata ? 1024 * 1024 : 10 * 1024 * 1024;
                if (entry.Length > limit) return "A file in the draft ZIP exceeds its size limit.";
                if (name != "editor.json") continue;
                hasEditor = true;
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = stream.Read(chunk)) != 0)
                {
                    if (buffer.Length + read > limit) return "The editor metadata exceeds 1 MB.";
                    buffer.Write(chunk, 0, read);
                }
                using var json = JsonDocument.Parse(buffer.ToArray());
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
                    format.GetString() != "jeemzu-gdd-editor" ||
                    !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
                    !root.TryGetProperty("draft", out var content) || content.ValueKind != JsonValueKind.Object ||
                    !content.TryGetProperty("title", out var storedTitle) || storedTitle.ValueKind != JsonValueKind.String ||
                    storedTitle.GetString()?.Trim() != title.Trim())
                    return "The ZIP must contain an editor draft matching the document title.";
            }
            return hasEditor ? null : "The ZIP is missing editor.json. Save an editable draft from the GDD form.";
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException)
        {
            return "The editable draft ZIP is invalid or damaged.";
        }
    }
}
