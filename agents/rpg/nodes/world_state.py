"""World state manager — deterministic state mutations based on actions. No LLM calls."""

from __future__ import annotations

import json
from pathlib import Path

from state import GameState, GamePhase, ActionType
from tools.progression import apply_equipment_bonus


def _load_world() -> dict:
    world_path = Path(__file__).parent.parent / "data" / "world.json"
    return json.loads(world_path.read_text())


async def world_state_node(state: GameState) -> dict:
    """Applies deterministic state mutations based on the action type and context.

    This node does NOT use an LLM — it's pure game logic.
    Returns state_mutations and visual_commands based on the action.
    """
    world_data = _load_world()
    action_type = state.get("action_type", "")
    player_action = state.get("player_action", "").lower()
    current_loc = state.get("current_location", "village_square")
    location_data = world_data["locations"].get(current_loc, {})

    mutations: list[dict] = []
    visual_commands: list[dict] = []
    new_state: dict = {}

    # If combat is already in progress, all resolution (attacks, spells, items, flee)
    # is handled by combat_node — skip the exploration/dialogue handlers below so we
    # don't double-process things like consuming an item from both places.
    already_in_combat = state.get("game_phase") == GamePhase.COMBAT.value

    if already_in_combat:
        pass

    elif action_type == ActionType.EXPLORE.value:
        new_state.update(_handle_explore(state, location_data, world_data, player_action, mutations, visual_commands))

    elif action_type == ActionType.EXAMINE.value:
        # Examine doesn't mutate state — just triggers narrative
        visual_commands.append({"type": "camera_focus", "data": {"target": "environment"}})

    elif action_type == ActionType.REST.value:
        new_state.update(_handle_rest(state, mutations, visual_commands))

    elif action_type == ActionType.USE_ITEM.value:
        new_state.update(_handle_use_item(state, world_data, player_action, mutations, visual_commands))

    elif action_type == ActionType.TALK.value:
        new_state.update(_handle_talk(state, location_data, player_action, mutations, visual_commands))

    # Update action log
    action_log = list(state.get("action_log", []))
    action_log.append({
        "player_id": state.get("player_id", ""),
        "action": state.get("player_action", ""),
        "action_type": action_type,
        "location": current_loc,
    })
    # Keep only last 10
    action_log = action_log[-10:]

    new_state["action_log"] = action_log
    new_state["state_mutations"] = mutations
    new_state["visual_commands"] = visual_commands

    return new_state


def _handle_explore(
    state: GameState,
    location_data: dict,
    world_data: dict,
    player_action: str,
    mutations: list[dict],
    visual_commands: list[dict],
) -> dict:
    """Handle exploration — moving between locations."""
    connections = location_data.get("connections", {})
    new_state: dict = {}

    # Try to find which direction/location the player wants to go
    target_direction = None
    for direction in connections:
        if direction in player_action:
            target_direction = direction
            break

    # Also check if they named the destination
    if not target_direction:
        for direction, loc_id in connections.items():
            loc_name = world_data["locations"].get(loc_id, {}).get("name", "").lower()
            if loc_name and any(word in player_action for word in loc_name.split()):
                target_direction = direction
                break

    if target_direction and target_direction in connections:
        new_location = connections[target_direction]
        new_loc_data = world_data["locations"].get(new_location, {})

        # Update location
        new_state["current_location"] = new_location

        # Track visited
        visited = list(state.get("visited_locations", []))
        if new_location not in visited:
            visited.append(new_location)
        new_state["visited_locations"] = visited

        # Visual commands for the transition
        visual_commands.append({
            "type": "transition_map",
            "data": {
                "map": new_loc_data.get("tilemap", new_location),
                "spawn": new_loc_data.get("spawn", {"x": 5, "y": 5}),
                "direction": target_direction,
            },
        })

        # Show NPCs at new location
        for npc_id in new_loc_data.get("npcs", []):
            npc_data = world_data["npcs"].get(npc_id, {})
            visual_commands.append({
                "type": "show_npc",
                "data": {
                    "npc_id": npc_id,
                    "sprite_key": npc_data.get("sprite_key", npc_id),
                    "position": npc_data.get("position", {"x": 5, "y": 5}),
                },
            })

        # Check if entering a location triggers combat
        enemies_here = new_loc_data.get("enemies", [])
        if enemies_here:
            new_state["game_phase"] = GamePhase.COMBAT.value
            mutations.append({"type": "trigger_combat", "enemies": enemies_here})

        # Check quest objective: reaching a location
        active_quests = list(state.get("active_quests", []))
        for quest in active_quests:
            for obj in quest.get("objectives", []):
                if obj["id"] == "reach_crypt" and new_location == "crypt_entrance" and not obj.get("completed", False):
                    obj["completed"] = True
                    mutations.append({"type": "quest_progress", "quest": quest["id"], "objective": obj["id"]})
        new_state["active_quests"] = active_quests

    return new_state


def _handle_rest(
    state: GameState,
    mutations: list[dict],
    visual_commands: list[dict],
) -> dict:
    """Handle resting — restore some HP/MP."""
    players = dict(state.get("players", {}))

    for pid, player in players.items():
        stats = player.get("stats", {})
        healed = False
        if stats.get("hp", 0) < stats.get("max_hp", 0):
            restore = min(10, stats["max_hp"] - stats["hp"])
            stats["hp"] += restore
            healed = True
        if stats.get("mp", 0) < stats.get("max_mp", 0):
            restore = min(5, stats["max_mp"] - stats["mp"])
            stats["mp"] += restore
            healed = True
        if healed:
            mutations.append({"type": "heal", "player_id": pid, "hp": stats["hp"], "mp": stats["mp"]})

    visual_commands.append({"type": "rest_animation", "data": {}})

    return {"players": players}


def _handle_use_item(
    state: GameState,
    world_data: dict,
    player_action: str,
    mutations: list[dict],
    visual_commands: list[dict],
) -> dict:
    """Handle using an item from inventory — consuming a potion, or equipping gear."""
    player_id = state.get("player_id", "")
    inventories = dict(state.get("inventories", {}))
    players = dict(state.get("players", {}))
    player_inv = list(inventories.get(player_id, []))
    player = players.get(player_id, {})

    # Find the item being used
    used_item = None
    for item in player_inv:
        if item["name"].lower() in player_action:
            used_item = item
            break

    if not used_item:
        return {}

    item_type = used_item.get("item_type")

    if item_type == "consumable":
        effect = used_item.get("effect", {})
        if "heal_hp" in effect:
            stats = player.get("stats", {})
            heal = effect["heal_hp"]
            stats["hp"] = min(stats.get("max_hp", 50), stats.get("hp", 0) + heal)
            mutations.append({"type": "use_item", "item": used_item["name"], "effect": f"+{heal} HP"})
            visual_commands.append({
                "type": "use_item_animation",
                "data": {"player_id": player_id, "item": used_item["id"], "effect": "heal"},
            })
        # Remove consumed item
        player_inv.remove(used_item)

    elif item_type in ("weapon", "armor"):
        slot = "weapon" if item_type == "weapon" else "armor_slot"
        equipment = dict(player.get("equipment", {}))
        stats = player.get("stats", {})
        previous_item_id = equipment.get(slot)

        # Unequip whatever was already in that slot, reversing its stat bonuses.
        if previous_item_id and previous_item_id != used_item["id"]:
            previous_item = next((i for i in player_inv if i["id"] == previous_item_id), None) \
                or world_data["items"].get(previous_item_id)
            if previous_item:
                apply_equipment_bonus(stats, previous_item.get("effect") or {}, reverse=True)

        equipment[slot] = used_item["id"]
        player["equipment"] = equipment
        apply_equipment_bonus(stats, used_item.get("effect") or {}, reverse=False)

        mutations.append({"type": "equip_item", "item": used_item["name"], "slot": slot})
        visual_commands.append({
            "type": "equip_animation",
            "data": {"player_id": player_id, "item": used_item["id"], "slot": slot},
        })

    inventories[player_id] = player_inv
    players[player_id] = player
    return {"inventories": inventories, "players": players}


def _handle_talk(
    state: GameState,
    location_data: dict,
    player_action: str,
    mutations: list[dict],
    visual_commands: list[dict],
) -> dict:
    """Handle initiating dialogue — sets npc_target and game phase."""
    npcs_here = location_data.get("npcs", [])
    new_state: dict = {}

    # Load world for NPC data
    world_data = _load_world()

    # Find which NPC the player wants to talk to
    target_npc = None
    for npc_id in npcs_here:
        npc = world_data["npcs"].get(npc_id, {})
        npc_name = npc.get("name", "").lower()
        if any(word in player_action for word in npc_name.split()):
            target_npc = npc_id
            break

    # If no specific NPC mentioned and only one here, default to them
    if not target_npc and len(npcs_here) == 1:
        target_npc = npcs_here[0]

    if target_npc:
        new_state["npc_target"] = target_npc
        new_state["game_phase"] = GamePhase.DIALOGUE.value

        npc_data = world_data["npcs"].get(target_npc, {})
        visual_commands.append({
            "type": "show_dialogue",
            "data": {
                "npc_id": target_npc,
                "portrait": npc_data.get("portrait", target_npc),
                "npc_name": npc_data.get("name", target_npc),
            },
        })

    return new_state
