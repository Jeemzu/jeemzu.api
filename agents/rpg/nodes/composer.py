"""Response composer — assembles final response from agent outputs for the frontend."""

from state import GameState


async def composer_node(state: GameState) -> dict:
    """Composes the final response from narrative output, visual commands, and state mutations.

    Assembles everything into a clean response payload that the .NET SignalR hub
    will broadcast to all party members.
    """
    narrative = state.get("narrative_output", "")
    visual_commands = state.get("visual_commands", [])
    mutations = state.get("state_mutations", [])
    players = state.get("players", {})
    current_location = state.get("current_location", "")
    game_phase = state.get("game_phase", "exploration")
    combat_state = state.get("combat_state", {})

    # Build UI state updates for the frontend
    ui_state: dict = {
        "current_location": current_location,
        "game_phase": game_phase,
        "players": {},
    }

    # Include player HP/MP for UI bars
    for pid, player in players.items():
        stats = player.get("stats", {})
        ui_state["players"][pid] = {
            "name": player.get("name", ""),
            "class": player.get("character_class", ""),
            "level": player.get("level", 1),
            "hp": stats.get("hp", 0),
            "max_hp": stats.get("max_hp", 0),
            "mp": stats.get("mp", 0),
            "max_mp": stats.get("max_mp", 0),
            "xp": player.get("xp", 0),
        }

    # Include combat state if in combat
    if game_phase == "combat" and combat_state:
        ui_state["combat"] = combat_state

    # Include mutations for the frontend to process (quest updates, item changes, etc.)
    ui_state["mutations"] = mutations

    # Update rolling narrative history
    recent_narrative = list(state.get("recent_narrative", []))
    if narrative:
        recent_narrative.append(narrative)
        recent_narrative = recent_narrative[-5:]  # Keep last 5

    # Build the final response string
    response = narrative

    return {
        "response": response,
        "ui_state": ui_state,
        "visual_commands": visual_commands,
        "recent_narrative": recent_narrative,
    }
