using JeemzuApi.Data;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JeemzuApi.Services;

public class PartyService : IPartyService
{
    // Excludes 0/O and 1/I to avoid ambiguity when players read codes aloud or type them.
    private static readonly char[] CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private readonly AppDbContext _db;

    public PartyService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Party> CreatePartyAsync(Guid hostUserId, string hostUsername, string characterName, string characterClass, string connectionId)
    {
        var code = await GenerateUniqueCodeAsync();

        var party = new Party
        {
            Code = code,
            HostUserId = hostUserId,
            Status = "Lobby",
        };

        party.Members.Add(new PartyMember
        {
            PartyId = party.Id,
            UserId = hostUserId,
            Username = hostUsername,
            CharacterName = characterName,
            CharacterClass = characterClass,
            IsHost = true,
            ConnectionId = connectionId,
        });

        _db.Parties.Add(party);
        await _db.SaveChangesAsync();

        return party;
    }

    public async Task<Party?> JoinPartyAsync(string code, Guid userId, string username, string characterName, string characterClass, string connectionId)
    {
        var party = await _db.Parties
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Code == code);

        if (party is null) return null;

        // Reconnecting members can rejoin a party they already started.
        var existingMember = party.Members.FirstOrDefault(m => m.UserId == userId);
        if (existingMember is not null)
        {
            existingMember.ConnectionId = connectionId;
            existingMember.ControlledByUsername = null; // Player is back — remove host control

            // If party was paused and all members are now connected (or controlled), unpause
            if (party.Status == "Paused")
            {
                var anyUnhandled = party.Members.Any(m =>
                    m.Id != existingMember.Id
                    && string.IsNullOrEmpty(m.ConnectionId)
                    && string.IsNullOrEmpty(m.ControlledByUsername));
                if (!anyUnhandled) party.Status = "InGame";
            }

            await _db.SaveChangesAsync();
            return party;
        }

        if (party.Status != "Lobby")
            throw new InvalidOperationException("PARTY_ALREADY_STARTED");

        if (party.Members.Count >= party.MaxPlayers)
            throw new InvalidOperationException("PARTY_FULL");

        var newMember = new PartyMember
        {
            PartyId = party.Id,
            UserId = userId,
            Username = username,
            CharacterName = characterName,
            CharacterClass = characterClass,
            IsHost = false,
            ConnectionId = connectionId,
        };

        // party is already tracked (loaded via query above), so adding to its Members
        // navigation alone isn't enough for EF to detect this as a new row — the Guid
        // key is pre-populated by the property initializer, which otherwise makes EF's
        // change detection treat it as an existing (Modified) row instead of Added.
        // EF's relationship fixup automatically adds newMember to party.Members since
        // PartyId already matches the tracked party's key — no need to add it twice.
        _db.PartyMembers.Add(newMember);

        await _db.SaveChangesAsync();
        return party;
    }

    public async Task<Party?> GetPartyAsync(Guid partyId)
    {
        return await _db.Parties
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == partyId);
    }

    public async Task<Party?> GetPartyByConnectionIdAsync(string connectionId)
    {
        return await _db.Parties
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Members.Any(m => m.ConnectionId == connectionId));
    }

    public async Task<(Party? Party, bool Disbanded)> LeavePartyAsync(string connectionId)
    {
        var party = await _db.Parties
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Members.Any(m => m.ConnectionId == connectionId));

        if (party is null) return (null, false);

        var member = party.Members.First(m => m.ConnectionId == connectionId);
        party.Members.Remove(member);
        _db.PartyMembers.Remove(member);

        if (party.Members.Count == 0)
        {
            party.Status = "Completed";
            await _db.SaveChangesAsync();
            return (party, true);
        }

        if (member.IsHost)
        {
            var newHost = party.Members.OrderBy(m => m.JoinedAt).First();
            newHost.IsHost = true;
            party.HostUserId = newHost.UserId;
        }

        await _db.SaveChangesAsync();
        return (party, false);
    }

    public async Task<Party> StartGameAsync(Guid partyId, string rpgSessionId, string initialGamePhase)
    {
        var party = await _db.Parties
            .Include(p => p.Members)
            .FirstAsync(p => p.Id == partyId);

        party.Status = "InGame";
        party.RpgSessionId = rpgSessionId;
        party.CurrentGamePhase = initialGamePhase;

        // Turn order defaults to join order; combat initiative (Phase 5) will reorder as needed.
        var ordered = party.Members.OrderBy(m => m.JoinedAt).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].TurnOrder = i;

        await _db.SaveChangesAsync();
        return party;
    }

    public async Task UpdatePartyStateAsync(Guid partyId, string gamePhase, string? currentTurnUsername)
    {
        var party = await _db.Parties.FirstAsync(p => p.Id == partyId);
        party.CurrentGamePhase = gamePhase;
        party.CurrentTurnUsername = currentTurnUsername;
        await _db.SaveChangesAsync();
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(Enumerable.Range(0, 6)
                .Select(_ => CodeChars[Random.Shared.Next(CodeChars.Length)])
                .ToArray());

            var exists = await _db.Parties.AnyAsync(p => p.Code == code && p.Status != "Completed");
            if (!exists) return code;
        }

        throw new InvalidOperationException("Failed to generate a unique party code. Please try again.");
    }
}
