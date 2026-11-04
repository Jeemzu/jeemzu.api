using System.Security.Claims;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/admin/files")]
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminFilesController(IFileStorageService files) : ControllerBase
{
    /// <summary>List the signed-in admin's private files without loading their contents.</summary>
    [HttpGet]
    [ProducesResponseType<List<StoredFileResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StoredFileResponse>>> List(CancellationToken ct)
    {
        var owner = await FindOwnerAsync(ct);
        if (owner is null) return Forbid();
        return Ok(await files.ListAsync(owner.Value, ct));
    }

    /// <summary>Upload any file type, up to 25 MB, into PostgreSQL.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(FileStorageService.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileStorageService.MaxFileBytes)]
    [ProducesResponseType<StoredFileResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<StoredFileResponse>> Upload(IFormFile file, CancellationToken ct)
    {
        var owner = await FindOwnerAsync(ct);
        if (owner is null) return Forbid();
        if (file.Length > FileStorageService.MaxFileBytes)
            return Problem(statusCode: StatusCodes.Status413PayloadTooLarge, detail: "Files must be 25 MB or smaller.");

        var name = file.FileName.Replace('\\', '/').Split('/')[^1];
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "File names must contain 1-255 characters without control characters.");

        await using var content = file.OpenReadStream();
        var result = await files.UploadAsync(owner.Value, name, content, ct);
        return CreatedAtAction(nameof(Download), new { id = result.Id }, result);
    }

    /// <summary>Download an owned file as an attachment, never as executable inline content.</summary>
    [HttpGet("{id:guid}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var owner = await FindOwnerAsync(ct);
        if (owner is null) return Forbid();
        var file = await files.DownloadAsync(owner.Value, id, ct);
        if (file is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: "File not found.");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Value.Content, "application/octet-stream", file.Value.Name);
    }

    /// <summary>Delete an owned file to make its database space available for reuse.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var owner = await FindOwnerAsync(ct);
        if (owner is null) return Forbid();
        return await files.DeleteAsync(owner.Value, id, ct)
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status404NotFound, detail: "File not found.");
    }

    private Task<Guid?> FindOwnerAsync(CancellationToken ct) =>
        files.FindAdminIdAsync(User.FindFirstValue(ClaimTypes.Name) ?? string.Empty, ct);
}
