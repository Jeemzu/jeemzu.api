using System.Text;
using System.Text.Json;
using JeemzuApi.Data;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Pgvector;

namespace JeemzuApi.Services;

public class IngestionService : IIngestionService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly IHostEnvironment _env;

    public IngestionService(AppDbContext db, IEmbeddingService embedding, IHostEnvironment env)
    {
        _db = db;
        _embedding = embedding;
        _env = env;
    }

    public async Task<int> IngestAsync(CancellationToken ct = default)
    {
        var filePath = Path.Combine(_env.ContentRootPath, "Data", "about-me.json");
        var json = await File.ReadAllTextAsync(filePath, ct);

        using var doc = JsonDocument.Parse(json);
        var chunks = BuildChunks(doc.RootElement).ToList();

        int upserted = 0;
        foreach (var (sourceKey, content) in chunks)
        {
            var embedding = await _embedding.GenerateEmbeddingAsync(content, ct);
            var vector = new Vector(embedding.ToArray());

            var existing = await _db.KnowledgeChunks
                .FirstOrDefaultAsync(k => k.SourceKey == sourceKey, ct);

            if (existing is null)
            {
                _db.KnowledgeChunks.Add(new KnowledgeChunk
                {
                    SourceKey = sourceKey,
                    Content = content,
                    Embedding = vector,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.Content = content;
                existing.Embedding = vector;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            upserted++;
        }

        await _db.SaveChangesAsync(ct);
        return upserted;
    }

    /// <summary>
    /// Converts each section of about-me.json into a (sourceKey, humanReadableText) pair.
    /// One chunk per logical unit: one for personal info, one per skill category,
    /// one per job, one per project, one per education entry.
    /// </summary>
    private static IEnumerable<(string SourceKey, string Content)> BuildChunks(JsonElement root)
    {
        // ── Personal ─────────────────────────────────────────────────────────
        if (root.TryGetProperty("personal", out var personal))
        {
            var sb = new StringBuilder();
            AppendIfPresent(sb, personal, "name", v => $"Name: {v}. ");
            AppendIfPresent(sb, personal, "title", v => $"Title: {v}. ");
            AppendIfPresent(sb, personal, "location", v => $"Located in: {v}. ");
            AppendIfPresent(sb, personal, "summary", v => $"{v} ");
            if (personal.TryGetProperty("contact", out var contact))
            {
                var links = new List<string>();
                AppendContact(links, contact, "linkedin", "LinkedIn");
                AppendContact(links, contact, "github", "GitHub");
                AppendContact(links, contact, "website", "Website");
                if (links.Count > 0)
                    sb.Append($"Contact: {string.Join(", ", links)}.");
            }
            yield return ("personal", sb.ToString().Trim());
        }

        // ── Skills ────────────────────────────────────────────────────────────
        if (root.TryGetProperty("skills", out var skills))
        {
            foreach (var category in skills.EnumerateObject())
            {
                var items = category.Value.EnumerateArray()
                    .Select(s => s.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                yield return (
                    $"skills.{SlugKey(category.Name)}",
                    $"{Capitalize(category.Name)} skills: {string.Join(", ", items)}."
                );
            }
        }

        // ── Experience ────────────────────────────────────────────────────────
        if (root.TryGetProperty("experience", out var experience))
        {
            int idx = 0;
            foreach (var job in experience.EnumerateArray())
            {
                var sb = new StringBuilder();
                AppendIfPresent(sb, job, "company", v => $"Company: {v}. ");
                AppendIfPresent(sb, job, "role", v => $"Role: {v}. ");
                AppendIfPresent(sb, job, "period", v => $"Period: {v}. ");
                AppendIfPresent(sb, job, "description", v => $"{v} ");
                if (job.TryGetProperty("achievements", out var achievements))
                {
                    var items = achievements.EnumerateArray().Select(a => a.GetString()).Where(s => s != null);
                    sb.Append($"Key achievements: {string.Join("; ", items)}. ");
                }
                if (job.TryGetProperty("technologies", out var tech))
                {
                    var items = tech.EnumerateArray().Select(t => t.GetString()).Where(s => s != null);
                    sb.Append($"Technologies used: {string.Join(", ", items)}.");
                }

                var key = job.TryGetProperty("company", out var co) && co.GetString() is string company
                    ? $"experience.{SlugKey(company)}"
                    : $"experience.{idx}";
                yield return (key, sb.ToString().Trim());
                idx++;
            }
        }

        // ── Projects ──────────────────────────────────────────────────────────
        if (root.TryGetProperty("projects", out var projects))
        {
            int idx = 0;
            foreach (var project in projects.EnumerateArray())
            {
                var sb = new StringBuilder();
                AppendIfPresent(sb, project, "name", v => $"Project: {v}. ");
                AppendIfPresent(sb, project, "description", v => $"{v} ");
                AppendIfPresent(sb, project, "url", v => $"URL: {v}. ");
                if (project.TryGetProperty("technologies", out var tech))
                {
                    var items = tech.EnumerateArray().Select(t => t.GetString()).Where(s => s != null);
                    sb.Append($"Technologies: {string.Join(", ", items)}. ");
                }
                if (project.TryGetProperty("highlights", out var highlights))
                {
                    var items = highlights.EnumerateArray().Select(h => h.GetString()).Where(s => s != null);
                    sb.Append($"Highlights: {string.Join("; ", items)}.");
                }

                var key = project.TryGetProperty("name", out var n) && n.GetString() is string name
                    ? $"projects.{SlugKey(name)}"
                    : $"projects.{idx}";
                yield return (key, sb.ToString().Trim());
                idx++;
            }
        }

        // ── Education ─────────────────────────────────────────────────────────
        if (root.TryGetProperty("education", out var education))
        {
            int idx = 0;
            foreach (var edu in education.EnumerateArray())
            {
                var sb = new StringBuilder();
                AppendIfPresent(sb, edu, "institution", v => $"Institution: {v}. ");
                AppendIfPresent(sb, edu, "degree", v => $"Degree: {v}. ");
                AppendIfPresent(sb, edu, "period", v => $"Period: {v}. ");
                if (edu.TryGetProperty("highlights", out var highlights))
                {
                    var items = highlights.EnumerateArray().Select(h => h.GetString()).Where(s => s != null);
                    sb.Append($"Highlights: {string.Join("; ", items)}.");
                }

                var key = edu.TryGetProperty("institution", out var inst) && inst.GetString() is string institution
                    ? $"education.{SlugKey(institution)}"
                    : $"education.{idx}";
                yield return (key, sb.ToString().Trim());
                idx++;
            }
        }

        // ── Strengths ─────────────────────────────────────────────────────────
        if (root.TryGetProperty("strengths", out var strengths))
        {
            foreach (var strength in strengths.EnumerateObject())
            {
                var content = strength.Value.GetString();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    yield return ($"strengths.{SlugKey(strength.Name)}", content);
                }
            }
        }
    }

    private static void AppendIfPresent(StringBuilder sb, JsonElement element, string property, Func<string, string> format)
    {
        if (element.TryGetProperty(property, out var value) && value.GetString() is string str && !string.IsNullOrWhiteSpace(str))
            sb.Append(format(str));
    }

    private static void AppendContact(List<string> list, JsonElement contact, string property, string label)
    {
        if (contact.TryGetProperty(property, out var value) && value.GetString() is string str && !string.IsNullOrWhiteSpace(str))
            list.Add($"{label}: {str}");
    }

    private static string SlugKey(string value)
        => value.ToLowerInvariant().Replace(' ', '_').Replace('.', '_');

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
