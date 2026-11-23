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

    private static byte[] CompressedProject(string title = "Game")
    {
        var project = JsonSerializer.Serialize(new
        {
            format = "jeemzu-gdd-canvas",
            version = 1,
            blocks = new
            {
                orderMode = "spatial",
                blocks = new[]
                {
                    new
                    {
                        subjectId = "game-title-description",
                        fields = new { title },
                    },
                },
            },
            assets = new { },
        });
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false), 1024, true))
            writer.Write(project);
        return output.ToArray();
    }

    [Fact]
    public void AcceptsMatchingPdfAndEditableDraft() =>
        Assert.Null(GddUploadValidator.Validate("Game", Pdf, Draft()));

    [Fact]
    public void AcceptsCompressedProjectJsonAndRetainsLegacyZipSupport()
    {
        Assert.Null(GddUploadValidator.Validate("Game", [], CompressedProject()));
        Assert.Null(GddUploadValidator.Validate("Game", [], Draft()));
    }

    [Fact]
    public void RejectsCompressedProjectWithMismatchedTitleOrInvalidJson()
    {
        Assert.NotNull(GddUploadValidator.Validate("Other", [], CompressedProject()));
        Assert.NotNull(GddUploadValidator.Validate("Game", [], "not gzip"u8.ToArray()));
    }

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
