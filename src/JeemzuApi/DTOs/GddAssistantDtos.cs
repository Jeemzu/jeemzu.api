using System.ComponentModel.DataAnnotations;

namespace JeemzuApi.DTOs;

/// <summary>A bounded, stateless request for one GDD checkpoint.</summary>
public sealed record GddAssistantRequest
{
    /// <summary>Scratch builds a new design; fill expands or revises an existing GDD.</summary>
    [RegularExpression("scratch|fill")]
    public string Mode { get; init; } = "scratch";
    [Required, StringLength(40)]
    public required string Checkpoint { get; init; }
    [Required, StringLength(4000)]
    public required string Prompt { get; init; }
    [Required, StringLength(100000)]
    public required string Document { get; init; }
    [Required, MaxLength(12)]
    public required GddAssistantMessage[] History { get; init; }
    [Required, MaxLength(12)]
    public required GddAssistantReference[] References { get; init; }
}

/// <summary>A previous user or assistant turn at the current checkpoint.</summary>
public sealed record GddAssistantMessage(
    [Required, RegularExpression("user|assistant")] string Role,
    [Required, StringLength(8000)] string Content);

/// <summary>User-selected reference text, a labeled URL, or a compressed JPEG.</summary>
public sealed record GddAssistantReference(
    [Required, StringLength(200)] string Name,
    [StringLength(20000)] string Text,
    [StringLength(2000)] string Url,
    [StringLength(350000)] string ImageBase64);

/// <summary>A proposed subsection; body contains Markdown without headings or images.</summary>
public sealed record GddAssistantItem(string Heading, string Body);

/// <summary>Section text in scratch mode; subsection additions/replacements by heading in fill mode.</summary>
public sealed record GddAssistantSection(string Title, GddAssistantItem[] Items);

/// <summary>Proposed text only; the user must approve it before changing the editor.</summary>
public sealed record GddAssistantProposal(string Title, string Introduction, GddAssistantSection[] Sections);

/// <summary>Focused guidance and a reviewable proposal for the current checkpoint.</summary>
public sealed record GddAssistantResponse(
    string Message, string[] Questions, string[] ReferenceRequests, GddAssistantProposal Proposal);
