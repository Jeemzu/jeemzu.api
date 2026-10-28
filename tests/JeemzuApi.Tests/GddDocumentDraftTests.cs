using System.Security.Claims;
using JeemzuApi.Controllers;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Tests;

public sealed class GddDocumentDraftTests
{
    [Fact]
    public async Task CreatesDraftOnlySavesAndClearsPdfOnUpdate()
    {
        var storage = new DraftStorage();
        var controller = Controller(storage);
        var request = new SaveGddDocumentRequest { Title = "Game", Draft = Draft() };
        var created = await controller.Create(request, CancellationToken.None);
        var metadata = Assert.IsType<GddDocumentResponse>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Equal(0, metadata.PdfBytes);
        Assert.Empty(storage.Pdf);
        Assert.Null(GddUploadValidator.Validate("Game", storage.Pdf, storage.Draft));

        storage.Pdf = GddUploadValidatorTests.Pdf;
        var updated = await controller.Update(metadata.Id, request with { Revision = metadata.Revision }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(updated.Result);
        Assert.Empty(storage.Pdf);
        Assert.IsType<NotFoundResult>(await controller.Pdf(metadata.Id, metadata.Revision, CancellationToken.None));
        var file = Assert.IsType<FileContentResult>(await controller.Draft(metadata.Id, metadata.Revision, CancellationToken.None));
        Assert.Equal(storage.Draft, file.FileContents);
    }

    [Fact]
    public async Task DraftOnlySavesStillRejectInvalidZipAndStaleDownloads()
    {
        var storage = new DraftStorage();
        var controller = Controller(storage);
        var request = new SaveGddDocumentRequest { Title = "Other", Draft = Draft() };
        var result = await controller.Create(request, CancellationToken.None);
        Assert.Equal(400, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Equal(0, storage.Saves);
        var stale = await controller.Pdf(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(409, Assert.IsType<ObjectResult>(stale).StatusCode);
    }

    private static IFormFile Draft()
    {
        var bytes = GddUploadValidatorTests.Draft();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "draft", "draft.zip");
    }

    private static GddDocumentsController Controller(DraftStorage storage) => new(storage)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "designer")], "test")),
            },
        },
    };

    private sealed class DraftStorage : IGddDocumentService
    {
        public byte[] Pdf { get; set; } = [];
        public byte[] Draft { get; private set; } = [];
        public int Saves { get; private set; }
        private readonly Guid _revision = Guid.NewGuid();

        public Task<Guid?> ResolveUserAsync(string username, CancellationToken ct) => Task.FromResult<Guid?>(Guid.NewGuid());
        public Task<GddFile?> GetFileAsync(Guid userId, Guid id, bool draft, CancellationToken ct) =>
            Task.FromResult<GddFile?>(new(draft ? Draft : Pdf, "Game", _revision));
        public Task<GddSaveResult> SaveAsync(Guid userId, Guid id, string title, byte[] pdf, byte[] draft, Guid? revision, bool create, CancellationToken ct)
        {
            Saves++;
            Pdf = pdf;
            Draft = draft;
            return Task.FromResult(new GddSaveResult(GddSaveStatus.Saved,
                new(id, title, _revision, pdf.Length, draft.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        }

        public Task<GddLibraryResponse> ListAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<GddDocumentResponse?> GetAsync(Guid userId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid userId, Guid id, Guid revision, CancellationToken ct) => throw new NotSupportedException();
    }
}
