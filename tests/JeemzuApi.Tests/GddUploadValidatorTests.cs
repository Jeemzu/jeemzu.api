using System.IO.Compression;
using System.Text;
using System.Text.Json;
using JeemzuApi.Services;

namespace JeemzuApi.Tests;

public class GddUploadValidatorTests
{
    internal static byte[] Pdf => "%PDF-1.7\nTest generated PDF\n%%EOF"u8.ToArray();

    internal static byte[] Draft(string title = "Game", string? path = null, string? editor = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("editor.json").Open()))
                writer.Write(editor ?? JsonSerializer.Serialize(new
                {
                    format = "jeemzu-gdd-editor", version = 1,
                    draft = new { title, introduction = "", sections = Array.Empty<object>() },
                }));
            if (path is not null)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write("test");
            }
        }
        return output.ToArray();
    }

    [Fact]
    public void AcceptsMatchingPdfAndEditableDraft() =>
        Assert.Null(GddUploadValidator.Validate("Game", Pdf, Draft()));

    [Fact]
    public void AcceptsDraftOnlySavesButStillValidatesTheDraft()
    {
        Assert.Null(GddUploadValidator.Validate("Game", [], Draft()));
        Assert.NotNull(GddUploadValidator.Validate("Other", [], Draft()));
        Assert.NotNull(GddUploadValidator.Validate("Game", [], "not a zip"u8.ToArray()));
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("/absolute.png")]
    [InlineData("images\\unsafe.png")]
    [InlineData("images/a.svg")]
    [InlineData("other.txt")]
    public void RejectsUnsafeOrUnsupportedZipFiles(string path) =>
        Assert.NotNull(GddUploadValidator.Validate("Game", Pdf, Draft(path: path)));

    [Theory]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("{\"format\":\"jeemzu-gdd-editor\",\"version\":\"1\",\"draft\":{\"title\":\"Game\"}}")]
    [InlineData("{\"format\":\"jeemzu-gdd-editor\",\"version\":2,\"draft\":{\"title\":\"Game\"}}")]
    public void RejectsMalformedOrUnknownMetadata(string json) =>
        Assert.NotNull(GddUploadValidator.Validate("Game", Pdf, Draft(editor: json)));

    [Fact]
    public void EnforcesFormatTitleAndSizeLimits()
    {
        Assert.NotNull(GddUploadValidator.Validate("Other", Pdf, Draft()));
        Assert.NotNull(GddUploadValidator.Validate("Bad\nTitle", Pdf, Draft()));
        Assert.NotNull(GddUploadValidator.Validate("Game", "Not a PDF"u8.ToArray(), Draft()));
        Assert.NotNull(GddUploadValidator.Validate("Game", Pdf, new byte[GddUploadValidator.DraftLimit + 1]));
        Assert.NotNull(GddUploadValidator.Validate("Game", new byte[GddUploadValidator.PdfLimit + 1], Draft()));
        Assert.NotNull(GddUploadValidator.Validate("Game", Pdf, Encoding.UTF8.GetBytes("Not a ZIP")));
    }
}
