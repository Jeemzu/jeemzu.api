using System.Net.Http.Json;
using System.Text.Json;
using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

/// <summary>
/// HTTP proxy to the Python Budgetize assistant service. Unlike the RPG service,
/// this one already speaks the browser's camelCase contract, so proposals pass
/// straight through as raw JSON instead of being remapped twice.
/// </summary>
public class BudgetAgentProxyService : IBudgetAgentProxyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public BudgetAgentProxyService(HttpClient http)
    {
        _http = http;
    }

    public async Task<BudgetChatResult> ChatAsync(BudgetChatPayload payload, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/budget/chat", payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<BudgetChatResult>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Budget assistant returned an empty response.");
    }
}
