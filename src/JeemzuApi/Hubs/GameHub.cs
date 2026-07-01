using System.Security.Claims;
using System.Text.Json;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Hubs;

/// <summary>
/// Real-time gateway for the multiplayer RPG. Manages party lobbies and forwards validated
/// player actions to the Python RPG orchestration service, broadcasting the AI's response
/// (narrative + visual commands + state) to every member of the party.
/// </summary>
[Authorize]
public class GameHub : Hub
{
    private static readonly string[] ValidClasses = ["warrior", "mage", "rogue"];

    private readonly AppDbContext _db;
    private readonly IPartyService _partyService;
    private readonly IRpgProxyService _rpgProxy;
    private readonly ITurnService _turnService;
    private readonly ICampaignService _campaignService;

    public GameHub(AppDbContext db, IPartyService partyService, IRpgProxyService rpgProxy, ITurnService turnService, ICampaignService campaignService)
    {
        _db = db;
        _partyService = partyService;
        _rpgProxy = rpgProxy;
        _turnService = turnService;
        _campaignService = campaignService;
    }

    private string Username => Context.User!.FindFirstValue(ClaimTypes.Name)!;

    // ── Party lobby ────────────────────────────────────────────────────────────

    public async Task<PartyResponse> CreateParty(string characterName, string characterClass)
    {
        var normalizedClass = ValidateClass(characterClass);
        var userId = await ResolveUserIdAsync();

        var party = await _partyService.CreatePartyAsync(userId, Username, characterName, normalizedClass, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, party.Code);

        return ToPartyResponse(party);
    }

    public async Task<PartyResponse> JoinParty(string code, string characterName, string characterClass)
    {
        var normalizedClass = ValidateClass(characterClass);
        var userId = await ResolveUserIdAsync();

        Party party;
        try
        {
            party = await _partyService.JoinPartyAsync(code.ToUpperInvariant(), userId, Username, characterName, normalizedClass, Context.ConnectionId)
                ?? throw new HubException("Party not found. Check the code and try again.");
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(FriendlyError(ex.Message));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, party.Code);

        var response = ToPartyResponse(party);
        await Clients.OthersInGroup(party.Code).SendAsync("PartyUpdated", response);
        return response;
    }

    public async Task LeaveParty()
    {
        var (party, disbanded) = await _partyService.LeavePartyAsync(Context.ConnectionId);
        if (party is null) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, party.Code);
        _turnService.CancelTurnTimeout(party.Id);

        if (disbanded)
        {
            await Clients.Group(party.Code).SendAsync("PartyDisbanded");
        }
        else
        {
            await Clients.Group(party.Code).SendAsync("PartyUpdated", ToPartyResponse(party));
        }
    }

    // ── Gameplay ───────────────────────────────────────────────────────────────

    public async Task StartGame()
    {
        var party = await _partyService.GetPartyByConnectionIdAsync(Context.ConnectionId)
            ?? throw new HubException("You are not in a party.");

        var member = party.Members.FirstOrDefault(m => m.ConnectionId == Context.ConnectionId);
        if (member is null || !member.IsHost)
            throw new HubException("Only the party host can start the game.");

        var players = party.Members
            .Select(m => new RpgPlayerCreate { PlayerId = m.Username, Name = m.CharacterName, CharacterClass = m.CharacterClass })
            .ToList();

        RpgNewGameResult result;
        try
        {
            result = await _rpgProxy.CreateGameAsync(players);
        }
        catch (HttpRequestException)
        {
            throw new HubException("The game service is currently unavailable. Please try again shortly.");
        }

        var gamePhase = ExtractGamePhase(result.UiState);
        await _partyService.StartGameAsync(party.Id, result.SessionId, gamePhase);

        await Clients.Group(party.Code).SendAsync("GameStarted", new
        {
            narrative = result.Narrative,
            visualCommands = result.VisualCommands,
            uiState = result.UiState,
        });
    }

    public async Task SendAction(string actionText, string? forCharacter = null)
    {
        var party = await _partyService.GetPartyByConnectionIdAsync(Context.ConnectionId)
            ?? throw new HubException("You are not in a party.");

        if (party.Status == "Paused")
            throw new HubException("Session is paused — take over disconnected characters to continue.");

        if (party.Status != "InGame" || party.RpgSessionId is null)
            throw new HubException("The game hasn't started yet.");

        // Determine which player_id this action is for
        var actingPlayerId = Username;
        if (forCharacter is not null)
        {
            var caller = party.Members.FirstOrDefault(m => m.ConnectionId == Context.ConnectionId);
            if (caller is null || !caller.IsHost)
                throw new HubException("Only the host can act for other characters.");

            // Host can act for characters they've taken over, or characters
            // with no party member (solo-loaded campaign)
            var target = party.Members.FirstOrDefault(m => m.Username == forCharacter);
            if (target is not null && target.ControlledByUsername != Username)
                throw new HubException("You don't control that character.");

            actingPlayerId = forCharacter;
        }

        if (!_turnService.IsPlayerTurn(party, actingPlayerId))
            throw new HubException("It's not your turn.");

        _turnService.CancelTurnTimeout(party.Id);

        RpgActionResult result;
        try
        {
            result = await _rpgProxy.SubmitActionAsync(party.RpgSessionId, actingPlayerId, actionText);
        }
        catch (HttpRequestException)
        {
            throw new HubException("The game service is currently unavailable. Please try again shortly.");
        }

        await BroadcastGameUpdateAsync(party, result);
    }

    /// <summary>Invoked by TurnService when a player doesn't act within the turn timeout — auto-defends on their behalf.</summary>
    private async Task AutoAdvanceTurnAsync(Guid partyId)
    {
        var party = await _partyService.GetPartyAsync(partyId);
        if (party?.RpgSessionId is null || party.CurrentTurnUsername is null) return;

        RpgActionResult result;
        try
        {
            result = await _rpgProxy.SubmitActionAsync(party.RpgSessionId, party.CurrentTurnUsername, "defend");
        }
        catch (HttpRequestException)
        {
            await Clients.Group(party.Code).SendAsync("Error", new { message = "The game service is currently unavailable." });
            return;
        }

        await BroadcastGameUpdateAsync(party, result, timedOut: true);
    }

    private async Task BroadcastGameUpdateAsync(Party party, RpgActionResult result, bool timedOut = false)
    {
        var gamePhase = ExtractGamePhase(result.UiState);
        var currentTurn = ExtractCurrentTurn(result.UiState);
        await _partyService.UpdatePartyStateAsync(party.Id, gamePhase, currentTurn);

        await Clients.Group(party.Code).SendAsync("GameUpdate", new
        {
            narrative = timedOut ? $"(Turn timed out) {result.Narrative}" : result.Narrative,
            visualCommands = result.VisualCommands,
            uiState = result.UiState,
            actionType = result.ActionType,
        });

        if (gamePhase == "combat" && currentTurn is not null)
        {
            _turnService.ScheduleTurnTimeout(party.Id, currentTurn, AutoAdvanceTurnAsync);
        }
    }

    // ── Campaign save/load ──────────────────────────────────────────────────────

    /// <summary>Saves the current game state to a campaign. Host-only. Creates a new campaign if none exists.</summary>
    public async Task<SaveCampaignResponse> SaveCampaign(string? campaignName)
    {
        var party = await _partyService.GetPartyByConnectionIdAsync(Context.ConnectionId)
            ?? throw new HubException("You are not in a party.");

        var member = party.Members.FirstOrDefault(m => m.ConnectionId == Context.ConnectionId);
        if (member is null || !member.IsHost)
            throw new HubException("Only the host can save the campaign.");

        if (party.RpgSessionId is null)
            throw new HubException("No active game to save.");

        Campaign campaign;
        try
        {
            campaign = await _campaignService.SaveAsync(
                party.HostUserId, party.CampaignId, campaignName ?? "", party.RpgSessionId);
        }
        catch (HttpRequestException)
        {
            throw new HubException("The game service is currently unavailable.");
        }

        // Link the party to the campaign on first save
        if (party.CampaignId is null)
        {
            var partyEntity = await _db.Parties.FirstAsync(p => p.Id == party.Id);
            partyEntity.CampaignId = campaign.Id;
            await _db.SaveChangesAsync();
        }

        await Clients.Group(party.Code).SendAsync("CampaignSaved", new
        {
            campaignId = campaign.Id,
            name = campaign.Name,
            savedAt = campaign.UpdatedAt,
        });

        return new SaveCampaignResponse
        {
            CampaignId = campaign.Id,
            Name = campaign.Name,
            SavedAt = campaign.UpdatedAt,
        };
    }

    /// <summary>Loads a saved campaign, creating a new party and restoring the game state.</summary>
    public async Task LoadCampaign(Guid campaignId)
    {
        var userId = await ResolveUserIdAsync();

        // Load campaign metadata to get the host's character info
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId && c.HostUserId == userId)
            ?? throw new HubException("Campaign not found.");

        var stateJson = JsonSerializer.Deserialize<JsonElement>(campaign.GameStateJson);

        // Find the host's character from the saved state
        var playersJson = stateJson.GetProperty("players");
        string characterName = "Unknown", characterClass = "warrior";
        foreach (var prop in playersJson.EnumerateObject())
        {
            // Use the first player as the host's character — in future, track host player_id
            characterName = prop.Value.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
            characterClass = prop.Value.TryGetProperty("character_class", out var c) ? c.GetString() ?? "warrior" : "warrior";
            break;
        }

        // Create a party for this campaign session
        var party = await _partyService.CreatePartyAsync(userId, Username, characterName, characterClass, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, party.Code);

        // Import the state into the Python RPG service
        RpgNewGameResult result;
        try
        {
            result = await _campaignService.LoadAsync(campaignId, userId);
        }
        catch (InvalidOperationException)
        {
            throw new HubException("Campaign not found.");
        }
        catch (HttpRequestException)
        {
            throw new HubException("The game service is currently unavailable.");
        }

        var gamePhase = ExtractGamePhase(result.UiState);
        await _partyService.StartGameAsync(party.Id, result.SessionId, gamePhase);

        // Link party to the campaign
        var partyEntity = await _db.Parties.FirstAsync(p => p.Id == party.Id);
        partyEntity.CampaignId = campaignId;
        await _db.SaveChangesAsync();

        // Reload party with members for the response
        party = (await _partyService.GetPartyAsync(party.Id))!;
        await Clients.Caller.SendAsync("PartyUpdated", ToPartyResponse(party));

        await Clients.Group(party.Code).SendAsync("GameStarted", new
        {
            narrative = result.Narrative,
            visualCommands = result.VisualCommands,
            uiState = result.UiState,
        });
    }

    // ── Disconnect / takeover ─────────────────────────────────────────────────

    /// <summary>Host takes control of a disconnected player's character (Divinity OS2 style).</summary>
    public async Task TakeOverCharacter(string targetUsername)
    {
        var party = await _partyService.GetPartyByConnectionIdAsync(Context.ConnectionId)
            ?? throw new HubException("You are not in a party.");

        var caller = party.Members.FirstOrDefault(m => m.ConnectionId == Context.ConnectionId);
        if (caller is null || !caller.IsHost)
            throw new HubException("Only the host can take over characters.");

        var target = party.Members.FirstOrDefault(m => m.Username == targetUsername);
        if (target is null)
            throw new HubException("Character not found in party.");

        if (!string.IsNullOrEmpty(target.ConnectionId))
            throw new HubException("That player is still connected.");

        target.ControlledByUsername = Username;

        // If all disconnected members are now controlled, unpause
        var anyUncontrolled = party.Members.Any(m =>
            string.IsNullOrEmpty(m.ConnectionId) && string.IsNullOrEmpty(m.ControlledByUsername));

        if (!anyUncontrolled && party.Status == "Paused")
            party.Status = "InGame";

        await _db.SaveChangesAsync();

        await Clients.Group(party.Code).SendAsync("PartyUpdated", ToPartyResponse(party));
        await Clients.Group(party.Code).SendAsync("CharacterTakenOver", new
        {
            targetUsername,
            controlledBy = Username,
            sessionResumed = party.Status == "InGame",
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var party = await _partyService.GetPartyByConnectionIdAsync(Context.ConnectionId);
        if (party is null)
        {
            await base.OnDisconnectedAsync(exception);
            return;
        }

        var member = party.Members.First(m => m.ConnectionId == Context.ConnectionId);

        if (party.Status is "InGame" or "Paused")
        {
            // Mark as disconnected but keep in party
            member.ConnectionId = "";

            if (!member.IsHost && string.IsNullOrEmpty(member.ControlledByUsername))
            {
                // Non-host player disconnected without host control — pause
                party.Status = "Paused";
                _turnService.CancelTurnTimeout(party.Id);
            }
            else if (member.IsHost)
            {
                // Host disconnected — auto-save if campaign exists, then pause
                if (party.RpgSessionId is not null && party.CampaignId is not null)
                {
                    try { await _campaignService.SaveAsync(party.HostUserId, party.CampaignId, "", party.RpgSessionId); }
                    catch { /* best-effort auto-save */ }
                }
                party.Status = "Paused";
                _turnService.CancelTurnTimeout(party.Id);
            }

            await _db.SaveChangesAsync();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, party.Code);
            await Clients.Group(party.Code).SendAsync("PlayerDisconnected", new
            {
                username = member.Username,
                isHost = member.IsHost,
                partyPaused = party.Status == "Paused",
            });
        }
        else
        {
            // In Lobby — remove the member entirely (existing behavior)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, party.Code);
            var (leftParty, disbanded) = await _partyService.LeavePartyAsync(Context.ConnectionId);
            if (leftParty is not null)
            {
                if (disbanded)
                    await Clients.Group(leftParty.Code).SendAsync("PartyDisbanded");
                else
                    await Clients.Group(leftParty.Code).SendAsync("PartyUpdated", ToPartyResponse(leftParty));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<Guid> ResolveUserIdAsync()
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == Username);
        return user?.Id ?? throw new HubException("User account not found.");
    }

    private static string ValidateClass(string characterClass)
    {
        var normalized = characterClass.ToLowerInvariant();
        if (!ValidClasses.Contains(normalized))
            throw new HubException($"Invalid class '{characterClass}'. Choose warrior, mage, or rogue.");
        return normalized;
    }

    private static string ExtractGamePhase(JsonElement uiState)
    {
        if (uiState.ValueKind == JsonValueKind.Object && uiState.TryGetProperty("game_phase", out var phase))
            return phase.GetString() ?? "exploration";
        return "exploration";
    }

    private static string? ExtractCurrentTurn(JsonElement uiState)
    {
        if (uiState.ValueKind == JsonValueKind.Object
            && uiState.TryGetProperty("combat", out var combat)
            && combat.ValueKind == JsonValueKind.Object
            && combat.TryGetProperty("current_turn", out var turn))
        {
            return turn.GetString();
        }
        return null;
    }

    private static PartyResponse ToPartyResponse(Party party) => new()
    {
        PartyId = party.Id,
        Code = party.Code,
        Status = party.Status,
        CampaignId = party.CampaignId,
        Members = party.Members.Select(m => new PartyMemberResponse
        {
            Username = m.Username,
            CharacterName = m.CharacterName,
            CharacterClass = m.CharacterClass,
            IsHost = m.IsHost,
            IsConnected = !string.IsNullOrEmpty(m.ConnectionId),
            ControlledBy = m.ControlledByUsername,
        }).ToList(),
    };

    private static string FriendlyError(string code) => code switch
    {
        "PARTY_ALREADY_STARTED" => "This party has already started its adventure.",
        "PARTY_FULL" => "This party is full.",
        _ => "Unable to join the party.",
    };
}
