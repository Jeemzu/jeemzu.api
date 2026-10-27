using System.Globalization;
using System.Text.Json;
using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public static class BudgetAssistantContext
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const int ContextLimit = 150000;

    internal static bool IsDate(string? value) => value is { Length: 10 } &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static string? Validate(BudgetChatRequest request)
    {
        if (!IsDate(request.Today)) return "Today must be a real yyyy-mm-dd calendar date.";
        if (request.History is null || request.History.Any(message => message is null ||
            message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 8000) ||
            request.History.Sum(message => message.Content.Length) > 64000)
            return "Use user/assistant history of at most 8,000 characters per turn and 64,000 combined.";
        var budget = request.Budget;
        if (budget.People.Any(person => person is null || person.Schedule is null || person.Schedule.Any(entry => entry is null)) ||
            budget.Bills.Any(item => item is null) || budget.Debts.Any(item => item is null) ||
            budget.Overrides.Any(item => item is null) || budget.OneOffs.Any(item => item is null) ||
            budget.PersonalExpenses.Any(item => item is null))
            return "Budget collections cannot contain null entries.";
        IEnumerable<string>[] collections = [
            budget.People.Select(item => item.Id), budget.Bills.Select(item => item.Id),
            budget.Debts.Select(item => item.Id), budget.Overrides.Select(item => item.Id),
            budget.OneOffs.Select(item => item.Id), budget.PersonalExpenses.Select(item => item.Id),
        ];
        if (collections.Any(ids => ids.Distinct(StringComparer.Ordinal).Count() != ids.Count()))
            return "IDs must be unique within each budget collection before requesting assistance.";
        if (request.Projection.ValueKind != JsonValueKind.Object ||
            !request.Projection.TryGetProperty("weeks", out var weeks) || weeks.ValueKind != JsonValueKind.Array ||
            weeks.GetArrayLength() > 32 ||
            !request.Projection.TryGetProperty("people", out var people) || people.ValueKind != JsonValueKind.Array ||
            people.GetArrayLength() != budget.People.Count)
            return "Provide the current client projection with people and at most 32 weeks.";
        var personIds = new HashSet<string>();
        foreach (var person in people.EnumerateArray())
        {
            if (person.ValueKind != JsonValueKind.Object || !person.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String || !personIds.Add(id.GetString()!) ||
                !budget.People.Any(entry => entry.Id == id.GetString()))
                return "Projection people must match the budget's people.";
        }
        foreach (var week in weeks.EnumerateArray())
        {
            if (week.ValueKind != JsonValueKind.Object ||
                !week.TryGetProperty("startISO", out var start) || start.ValueKind != JsonValueKind.String || !IsDate(start.GetString()) ||
                !week.TryGetProperty("endISO", out var end) || end.ValueKind != JsonValueKind.String || !IsDate(end.GetString()) ||
                string.CompareOrdinal(start.GetString(), end.GetString()) > 0 ||
                !week.TryGetProperty("essentials", out var essentials) || essentials.ValueKind != JsonValueKind.Object ||
                !week.TryGetProperty("autopay", out var autopay) || autopay.ValueKind != JsonValueKind.Object ||
                !week.TryGetProperty("personal", out var personal) || personal.ValueKind != JsonValueKind.Array ||
                personal.GetArrayLength() != people.GetArrayLength())
                return "The client projection contains an invalid week.";
        }
        return Build(request).Length > ContextLimit
            ? "Budget assistant context exceeds 150,000 characters. Reduce the forecast range or budget size." : null;
    }

    internal static string Build(BudgetChatRequest request) => JsonSerializer.Serialize(new
    {
        request.Today,
        request.Strategy,
        Budget = request.Budget,
        // Weekly totals retain all flows, balances, contribution/unknown-income flags, without duplicating daily ledgers.
        Projection = new
        {
            People = request.Projection.GetProperty("people"),
            Weeks = request.Projection.GetProperty("weeks").EnumerateArray().Select(week =>
                week.EnumerateObject().Where(property => property.Name != "days")
                    .ToDictionary(property => property.Name, property => property.Value)),
        },
        History = request.History.TakeLast(6),
        request.Question,
    }, JsonOptions);
}
