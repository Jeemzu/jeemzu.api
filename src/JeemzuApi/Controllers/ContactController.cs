using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace JeemzuApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContactController : ControllerBase
{
    private readonly IEmailService _email;

    public ContactController(IEmailService email)
    {
        _email = email;
    }

    /// <summary>
    /// Send a contact email to James. No authentication required.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SendContact(
        [FromBody] ContactRequest request,
        CancellationToken ct)
    {
        var success = await _email.SendContactEmailAsync(request.Subject, request.Content, ct);

        if (!success)
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Failed to send email. Please try again later." });

        return NoContent();
    }
}
