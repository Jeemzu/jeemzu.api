using System.Security.Claims;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/gdd-documents")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class GddDocumentsController(IGddDocumentService documents) : ControllerBase
{
    /// <summary>Lists this account's GDDs and PDF-plus-draft storage usage.</summary>
    [HttpGet]
    [ProducesResponseType<GddLibraryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GddLibraryResponse>> List(CancellationToken ct)
    {
        var user = await UserId(ct);
        return user is null ? Unauthorized() : Ok(await documents.ListAsync(user.Value, ct));
    }

    /// <summary>Gets one owned document's metadata.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<GddDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GddDocumentResponse>> Get(Guid id, CancellationToken ct)
    {
        var user = await UserId(ct);
        if (user is null) return Unauthorized();
        var result = await documents.GetAsync(user.Value, id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Downloads the PDF for an exact saved revision.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Pdf(Guid id, [FromQuery] Guid revision, CancellationToken ct) => Download(id, revision, false, ct);

    /// <summary>Downloads the editable ZIP for an exact saved revision.</summary>
    [HttpGet("{id:guid}/draft")]
    [Produces("application/zip", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Draft(Guid id, [FromQuery] Guid revision, CancellationToken ct) => Download(id, revision, true, ct);

    /// <summary>Creates a named GDD if its PDF and draft fit the account's 100 MB quota.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(GddUploadValidator.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = GddUploadValidator.RequestLimit)]
    [EnableRateLimiting("account-user")]
    [ProducesResponseType<GddDocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<GddDocumentResponse>> Create([FromForm] SaveGddDocumentRequest request, CancellationToken ct) =>
        Save(Guid.NewGuid(), request, true, ct);

    /// <summary>Replaces a GDD only if its revision still matches; stale saves return 409.</summary>
    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(GddUploadValidator.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = GddUploadValidator.RequestLimit)]
    [EnableRateLimiting("account-user")]
    [ProducesResponseType<GddDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<GddDocumentResponse>> Update(Guid id, [FromForm] SaveGddDocumentRequest request, CancellationToken ct) =>
        Save(id, request, false, ct);

    /// <summary>Deletes an owned GDD at the specified revision and releases its storage.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid revision, CancellationToken ct)
    {
        var user = await UserId(ct);
        if (user is null) return Unauthorized();
        if (revision == Guid.Empty) return Problem(statusCode: 400, detail: "A saved revision is required.");
        if (await documents.DeleteAsync(user.Value, id, revision, ct)) return NoContent();
        return await documents.GetAsync(user.Value, id, ct) is null
            ? NotFound() : Problem(statusCode: 409, detail: "This document changed elsewhere. Refresh the library before deleting.");
    }

    private async Task<ActionResult<GddDocumentResponse>> Save(Guid id, SaveGddDocumentRequest request, bool create, CancellationToken ct)
    {
        var user = await UserId(ct);
        if (user is null) return Unauthorized();
        if ((!create && (request.Revision is null || request.Revision == Guid.Empty)) || (create && request.Revision is not null))
            return Problem(statusCode: 400, detail: "Updates require the saved revision; new documents must omit it.");
        if (request.Pdf.Length > GddUploadValidator.PdfLimit || request.Draft.Length > GddUploadValidator.DraftLimit)
            return Problem(statusCode: 413, detail: "PDFs support up to 20 MB and draft ZIPs up to 25 MB.");
        using var pdf = new MemoryStream();
        using var draft = new MemoryStream();
        await request.Pdf.CopyToAsync(pdf, ct);
        await request.Draft.CopyToAsync(draft, ct);
        var pdfBytes = pdf.ToArray();
        var draftBytes = draft.ToArray();
        var error = GddUploadValidator.Validate(request.Title, pdfBytes, draftBytes);
        if (error is not null) return Problem(statusCode: 400, detail: error);
        var result = await documents.SaveAsync(user.Value, id, request.Title.Trim(), pdfBytes, draftBytes, request.Revision, create, ct);
        return result.Status switch
        {
            GddSaveStatus.Saved when create => CreatedAtAction(nameof(Get), new { id }, result.Document),
            GddSaveStatus.Saved => Ok(result.Document),
            GddSaveStatus.NotFound => NotFound(),
            GddSaveStatus.Conflict => Problem(statusCode: 409, detail: "This document changed elsewhere. Load its latest version or save as new."),
            GddSaveStatus.QuotaExceeded => Problem(statusCode: 413, detail: "This save exceeds your 100 MB storage limit. Delete a saved document or reduce its images."),
            _ => throw new InvalidOperationException("Unexpected GDD save status."),
        };
    }

    private async Task<IActionResult> Download(Guid id, Guid revision, bool draft, CancellationToken ct)
    {
        var user = await UserId(ct);
        if (user is null) return Unauthorized();
        if (revision == Guid.Empty) return Problem(statusCode: 400, detail: "A saved revision is required.");
        var file = await documents.GetFileAsync(user.Value, id, draft, ct);
        if (file is null) return NotFound();
        if (file.Revision != revision) return Problem(statusCode: 409, detail: "This document changed elsewhere. Refresh the library and try again.");
        var safeTitle = string.Concat(file.Title.Select(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? '-' : character));
        return File(file.Data, draft ? "application/zip" : "application/pdf",
            $"{safeTitle} - {(draft ? "GDD Draft.zip" : "Game Design Document.pdf")}");
    }

    private Task<Guid?> UserId(CancellationToken ct)
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        return username is null ? Task.FromResult<Guid?>(null) : documents.ResolveUserAsync(username, ct);
    }
}
