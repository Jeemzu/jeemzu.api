"""Combat node — turn-based multiplayer combat resolution.

Handles the full combat lifecycle within a single node: initiative rolling,
resolving the acting player's action, auto-resolving enemy turns until control
returns to a living player, and victory/defeat/flee resolution (XP, loot,
leveling, quest hooks, respawn). All numeric outcomes come from the
deterministic tools in tools/ — no LLM calls are used here, keeping combat
fast and reproducible; narration is built from clear, formulaic templates.
"""

from __future__ import annotations

import json
import random
from pathlib import Path

from state import GameState, GamePhase, ActionType
from tools.dice import roll_initiative, stat_modifier
from tools.combat_calc import (
    calculate_attack_damage,
    calculate_spell_damage,
    calculate_heal,
    resolve_loot_drop,
)
from tools.progression import roll_to_hit, apply_stat_training, check_total_level_up
from tools.skill_check import skill_check, DC_MEDIUM

MAX_TURN_ADVANCE_ITERATIONS = 20

CLASS_WEAPON_DEFAULTS: dict[str, tuple[str, str, int]] = {
    "warrior": ("d8", "strength", 1),
    "mage": ("d6", "intelligence", 1),
    "rogue": ("d6", "dexterity", 2),
}


def _load_world() -> dict:
    world_path = Path(__file__).parent.parent / "data" / "world.json"
    return json.loads(world_path.read_text())


async def combat_node(state: GameState) -> dict:
    """Resolves one step of combat: either starts it or processes the acting player's turn."""
    world_data = _load_world()
    players = dict(state.get("players", {}))
    inventories = dict(state.get("inventories", {}))
    combat_state = dict(state.get("combat_state") or {})
    mutations_in = list(state.get("state_mutations", []))
    visual_commands: list[dict] = []
    narrative_parts: list[str] = []

    trigger = next((m for m in mutations_in if m.get("type") == "trigger_combat"), None)
    just_started = bool(trigger) or not combat_state.get("initiative_order")

    if just_started:
        enemy_ids = trigger["enemies"] if trigger else []
        enemy_entities = _build_combat_entities(enemy_ids, world_data)
        initiative = _roll_initiative_order(players, enemy_entities)
        combat_state = {
            "enemies": enemy_entities,
            "initiative_order": initiative,
            "current_turn_index": -1,
            "round_number": 1,
        }
        enemy_names = ", ".join(e["name"] for e in enemy_entities)
        narrative_parts.append(f"{enemy_names} block your path! Roll for initiative!")
        visual_commands.append({
            "type": "combat_started",
            "data": {
                "enemies": [
                    {
                        "id": e["id"],
                        "sprite_key": e["sprite_key"],
                        "name": e["name"],
                        "hp": e["stats"]["hp"],
                        "max_hp": e["stats"]["max_hp"],
                    }
                    for e in enemy_entities
                ],
                "initiative_order": initiative,
            },
        })
    else:
        acting_id = state.get("player_id", "")
        action_type = state.get("action_type", "")
        action_text = state.get("player_action", "")
        result_text, fled = _resolve_player_action(
            acting_id, action_type, action_text, players, inventories, combat_state, visual_commands
        )
        narrative_parts.append(result_text)
        if fled:
            return _end_combat(
                state, players, inventories, world_data,
                victory=False, fled=True,
                narrative_parts=narrative_parts, visual_commands=visual_commands,
                defeated_types=[],
            )

    # Prune dead enemies, remembering which types died this call for XP/loot.
    defeated_types = [e["enemy_type"] for e in combat_state.get("enemies", []) if e["stats"]["hp"] <= 0]
    combat_state["enemies"] = [e for e in combat_state.get("enemies", []) if e["stats"]["hp"] > 0]

    living_player_ids = {pid for pid, p in players.items() if p["stats"]["hp"] > 0}
    alive_ids = {e["id"] for e in combat_state["enemies"]} | living_player_ids
    combat_state["initiative_order"] = [i for i in combat_state.get("initiative_order", []) if i in alive_ids]

    victory = len(combat_state["enemies"]) == 0
    defeat = len(living_player_ids) == 0

    if victory or defeat:
        return _end_combat(
            state, players, inventories, world_data,
            victory=victory, fled=False,
            narrative_parts=narrative_parts, visual_commands=visual_commands,
            defeated_types=defeated_types,
        )

    # Advance turns, auto-resolving enemy actions until control returns to a living player.
    order = combat_state["initiative_order"]
    idx = combat_state.get("current_turn_index", -1)
    guard = 0
    while guard < MAX_TURN_ADVANCE_ITERATIONS:
        guard += 1
        idx = (idx + 1) % len(order)
        if idx == 0:
            combat_state["round_number"] = combat_state.get("round_number", 1) + 1
        actor = order[idx]

        if actor in players:
            combat_state["current_turn_index"] = idx
            combat_state["current_turn"] = actor
            break

        enemy = next((e for e in combat_state["enemies"] if e["id"] == actor), None)
        if enemy is None:
            continue

        enemy_line = _enemy_turn(enemy, players, combat_state, visual_commands)
        if enemy_line:
            narrative_parts.append(enemy_line)

        living_player_ids = {pid for pid, p in players.items() if p["stats"]["hp"] > 0}
        if not living_player_ids:
            combat_state["current_turn_index"] = idx
            return _end_combat(
                state, players, inventories, world_data,
                victory=False, fled=False,
                narrative_parts=narrative_parts, visual_commands=visual_commands,
                defeated_types=defeated_types,
            )

    return {
        "players": players,
        "inventories": inventories,
        "combat_state": combat_state,
        "narrative_output": " ".join(narrative_parts),
        "visual_commands": visual_commands,
        "state_mutations": [],
        "game_phase": GamePhase.COMBAT.value,
    }


def _build_combat_entities(enemy_ids: list[str], world_data: dict) -> list[dict]:
    """Instantiates enemy entities from world.json data, disambiguating duplicates."""
    entities: list[dict] = []
    counts: dict[str, int] = {}

    for eid in enemy_ids:
        counts[eid] = counts.get(eid, 0) + 1
        suffix = counts[eid]
        instance_id = eid if suffix == 1 else f"{eid}_{suffix}"
        edata = world_data["enemies"][eid]

        entities.append({
            "id": instance_id,
            "enemy_type": eid,
            "name": edata["name"] if suffix == 1 else f"{edata['name']} {suffix}",
            "sprite_key": edata["sprite_key"],
            "stats": dict(edata["stats"]),
            "xp_reward": edata["xp_reward"],
            "loot_table": edata["loot_table"],
            "behavior": edata.get("behavior", "aggressive"),
            "abilities": edata.get("abilities", ["basic_attack"]),
            "is_boss": edata.get("is_boss", False),
        })

    return entities


def _roll_initiative_order(players: dict, enemy_entities: list[dict]) -> list[str]:
    """d20 + DEX modifier for every combatant; returns IDs sorted highest-first."""
    rolls: list[tuple[str, int]] = []

    for pid, player in players.items():
        if player["stats"]["hp"] <= 0:
            continue
        rolls.append((pid, roll_initiative(stat_modifier(player["stats"]["dexterity"]))))

    for enemy in enemy_entities:
        rolls.append((enemy["id"], roll_initiative(stat_modifier(enemy["stats"]["dexterity"]))))

    rolls.sort(key=lambda entry: entry[1], reverse=True)
    return [entity_id for entity_id, _ in rolls]


def _weapon_profile(player: dict, inventory: list[dict]) -> tuple[str, str, int]:
    """Returns (damage_die, stat_key, hits) for the player's equipped weapon or class default."""
    equipped_weapon_id = player.get("equipment", {}).get("weapon")
    if equipped_weapon_id:
        weapon = next((item for item in inventory if item["id"] == equipped_weapon_id), None)
        if weapon and weapon.get("effect"):
            effect = weapon["effect"]
            return effect.get("damage_die", "d6"), effect.get("stat", "strength"), effect.get("hits", 1)
    return CLASS_WEAPON_DEFAULTS.get(player["character_class"], ("d6", "strength", 1))


def _pick_target(action_text: str, living_enemies: list[dict]) -> dict | None:
    if not living_enemies:
        return None
    lowered = action_text.lower()
    for enemy in living_enemies:
        if enemy["name"].lower() in lowered or enemy["enemy_type"].replace("_", " ") in lowered:
            return enemy
    return living_enemies[0]


def _resolve_player_action(
    acting_id: str,
    action_type: str,
    action_text: str,
    players: dict,
    inventories: dict,
    combat_state: dict,
    visual_commands: list[dict],
) -> tuple[str, bool]:
    """Resolves the acting player's combat action. Returns (narrative, fled)."""
    player = players.get(acting_id)
    if player is None:
        return "", False

    stats = player["stats"]
    if stats["hp"] <= 0:
        return f"{player['name']} is downed and can't act!", False

    living_enemies = [e for e in combat_state.get("enemies", []) if e["stats"]["hp"] > 0]

    # The router maps "flee"/"run"/"escape" to EXPLORE while in combat as a flee signal.
    if action_type == ActionType.EXPLORE.value:
        passed, _roll, _total = skill_check(stats["dexterity"], DC_MEDIUM)
        if passed:
            visual_commands.append({"type": "flee_success", "data": {"player_id": acting_id}})
            return f"{player['name']} flees from combat!", True
        return f"{player['name']} tries to flee but fails to escape!", False

    if action_type == ActionType.DEFEND.value:
        combat_state.setdefault("defending", {})[acting_id] = True
        visual_commands.append({"type": "combat_defend", "data": {"player_id": acting_id}})
        return f"{player['name']} braces for the next attack, raising their guard!", False

    target = _pick_target(action_text, living_enemies)
    if target is None:
        return "There's nothing left to fight!", False

    if action_type == ActionType.CAST_SPELL.value:
        mp_cost = 5
        if stats["mp"] < mp_cost:
            return f"{player['name']} doesn't have enough MP to cast a spell!", False
        stats["mp"] -= mp_cost

        if "heal" in action_text.lower():
            heal_amount = calculate_heal(15, stats["intelligence"])
            stats["hp"] = min(stats["max_hp"], stats["hp"] + heal_amount)
            visual_commands.append({
                "type": "combat_spell",
                "data": {"caster": acting_id, "spell": "heal", "target": acting_id, "heal": heal_amount},
            })
            return f"{player['name']} casts Heal, restoring {heal_amount} HP!", False

        if not roll_to_hit(stats["perception"], target["stats"].get("agility", 0)):
            visual_commands.append({"type": "combat_miss", "data": {"actor": acting_id, "target": target["id"]}})
            return f"{player['name']} hurls a fireball at {target['name']}, but it fizzles wide!", False

        damage = calculate_spell_damage("d8", stats["intelligence"], target["stats"]["armor"])
        target["stats"]["hp"] = max(0, target["stats"]["hp"] - damage)
        visual_commands.append({
            "type": "combat_spell",
            "data": {"caster": acting_id, "spell": "fireball", "target": target["id"], "damage": damage},
        })
        result = f"{player['name']} hurls a fireball at {target['name']} for {damage} damage!"
        if target["stats"]["hp"] <= 0:
            result += f" {target['name']} is defeated!"
            visual_commands.append({"type": "enemy_death", "data": {"entity": target["id"]}})
        return result, False

    if action_type == ActionType.USE_ITEM.value:
        inventory = inventories.get(acting_id, [])
        potion = next((item for item in inventory if item.get("item_type") == "consumable"), None)
        if not potion:
            return f"{player['name']} has no usable items!", False

        heal_amount = potion.get("effect", {}).get("heal_hp", 20)
        stats["hp"] = min(stats["max_hp"], stats["hp"] + heal_amount)
        inventory.remove(potion)
        inventories[acting_id] = inventory
        visual_commands.append({
            "type": "use_item_animation",
            "data": {"player_id": acting_id, "item": potion["id"], "effect": "heal"},
        })
        return f"{player['name']} drinks a {potion['name']}, restoring {heal_amount} HP!", False

    # Default: FIGHT — basic weapon attack.
    if not roll_to_hit(stats["perception"], target["stats"].get("agility", 0)):
        visual_commands.append({"type": "combat_miss", "data": {"actor": acting_id, "target": target["id"]}})
        return f"{player['name']} attacks {target['name']} but misses!", False

    weapon_die, stat_key, hits = _weapon_profile(player, inventories.get(acting_id, []))
    damage = calculate_attack_damage(weapon_die, stats[stat_key], target["stats"]["armor"], hits=hits)
    target["stats"]["hp"] = max(0, target["stats"]["hp"] - damage)
    visual_commands.append({
        "type": "combat_attack",
        "data": {"actor": acting_id, "target": target["id"], "damage": damage},
    })
    result = f"{player['name']} attacks {target['name']} for {damage} damage!"
    if target["stats"]["hp"] <= 0:
        result += f" {target['name']} is defeated!"
        visual_commands.append({"type": "enemy_death", "data": {"entity": target["id"]}})
    return result, False


def _enemy_turn(enemy: dict, players: dict, combat_state: dict, visual_commands: list[dict]) -> str:
    """Simple enemy AI: targets the lowest-HP living player; bosses occasionally use abilities."""
    living_players = {pid: p for pid, p in players.items() if p["stats"]["hp"] > 0}
    if not living_players:
        return ""

    target_id = min(living_players, key=lambda pid: living_players[pid]["stats"]["hp"])
    target = living_players[target_id]
    defending = combat_state.setdefault("defending", {})
    is_defending = defending.pop(target_id, False)

    if not roll_to_hit(enemy["stats"].get("perception", 0), target["stats"].get("agility", 0)):
        visual_commands.append({"type": "combat_miss", "data": {"actor": enemy["id"], "target": target_id}})
        return f"{enemy['name']} attacks {target['name']} but misses!"

    if enemy.get("is_boss") and "shadow_bolt" in enemy.get("abilities", []) and _rare_roll():
        damage = calculate_spell_damage("d8", enemy["stats"]["intelligence"], target["stats"]["armor"])
        if is_defending:
            damage = max(1, damage // 2)
        target["stats"]["hp"] = max(0, target["stats"]["hp"] - damage)
        visual_commands.append({
            "type": "combat_spell",
            "data": {"caster": enemy["id"], "spell": "shadow_bolt", "target": target_id, "damage": damage},
        })
        line = f"{enemy['name']} unleashes a shadow bolt at {target['name']} for {damage} damage!"
        return line + " (guarded)" if is_defending else line

    damage = calculate_attack_damage("d6", enemy["stats"]["strength"], target["stats"]["armor"])
    if is_defending:
        damage = max(1, damage // 2)
    target["stats"]["hp"] = max(0, target["stats"]["hp"] - damage)
    visual_commands.append({
        "type": "combat_attack",
        "data": {"actor": enemy["id"], "target": target_id, "damage": damage},
    })
    line = f"{enemy['name']} attacks {target['name']} for {damage} damage!"
    if is_defending:
        line += " Their guard softened the blow!"
    if target["stats"]["hp"] <= 0:
        line += f" {target['name']} is downed!"
    return line


def _rare_roll(chance: float = 0.3) -> bool:
    return random.random() < chance


def _end_combat(
    state: GameState,
    players: dict,
    inventories: dict,
    world_data: dict,
    victory: bool,
    fled: bool,
    narrative_parts: list[str],
    visual_commands: list[dict],
    defeated_types: list[str],
) -> dict:
    """Resolves the end of combat: XP/loot/leveling on victory, respawn on defeat."""
    active_quests = list(state.get("active_quests", []))
    completed_quests = list(state.get("completed_quests", []))
    current_location = state.get("current_location", "village_square")
    new_location = current_location

    if victory:
        looted_items: list[str] = []
        for enemy_type in defeated_types:
            looted_items.extend(resolve_loot_drop(world_data["enemies"][enemy_type]["loot_table"]))

        # Per-stat training (Progenitors-inspired): every living party member gets the
        # full "trains" progress from each defeated enemy — not split/divided, since the
        # whole party shares in the win, same convention as loot distribution below.
        for player in players.values():
            if player["stats"]["hp"] <= 0:
                continue

            xp_gained_this_fight = 0
            for enemy_type in defeated_types:
                enemy_data = world_data["enemies"][enemy_type]
                trains = enemy_data.get("trains", {})
                xp_gained_this_fight += sum(trains.values())

                leveled_stats = apply_stat_training(player, trains)
                for stat_name in leveled_stats:
                    narrative_parts.append(f"{player['name']}'s {stat_name.capitalize()} grows stronger!")

            player["xp"] = player.get("xp", 0) + xp_gained_this_fight

            levels_gained = check_total_level_up(player)
            if levels_gained:
                narrative_parts.append(f"{player['name']} reached level {player['level']}!")
                if player["level"] % 5 == 0:
                    narrative_parts.append(f"{player['name']}'s training focus resets — time to grow anew!")
                # Full heal on a total level-up, consistent with previous behavior.
                player["stats"]["hp"] = player["stats"]["max_hp"]
                player["stats"]["mp"] = player["stats"]["max_mp"]

        loot_recipient = state.get("player_id") or next(iter(players), None)
        if loot_recipient and looted_items:
            inventory = inventories.setdefault(loot_recipient, [])
            for item_id in looted_items:
                item_data = world_data["items"].get(item_id)
                if item_data:
                    inventory.append({"id": item_id, **item_data})

        if looted_items:
            item_names = ", ".join(world_data["items"].get(i, {}).get("name", i) for i in looted_items)
            narrative_parts.append(f"Victory! The party found: {item_names}.")
        else:
            narrative_parts.append("Victory!")

        if "crypt_wraith" in defeated_types:
            for quest in active_quests:
                for objective in quest.get("objectives", []):
                    if objective["id"] == "defeat_wraith" and not objective.get("completed", False):
                        objective["completed"] = True

        visual_commands.append({"type": "combat_ended", "data": {"result": "victory", "loot": looted_items}})

    elif fled:
        narrative_parts.append("The party successfully flees from combat!")
        visual_commands.append({"type": "combat_ended", "data": {"result": "fled"}})

    else:
        narrative_parts.append(
            "The party has been defeated! Everything fades to black... "
            "You awaken back in the village square, battered but alive."
        )
        for player in players.values():
            player["stats"]["hp"] = max(1, player["stats"]["max_hp"] // 2)
            player["stats"]["mp"] = player["stats"]["max_mp"]

        village = world_data["locations"]["village_square"]
        visual_commands.append({"type": "combat_ended", "data": {"result": "defeat"}})
        visual_commands.append({
            "type": "transition_map",
            "data": {"map": village["tilemap"], "spawn": village["spawn"]},
        })
        new_location = "village_square"

    return {
        "players": players,
        "inventories": inventories,
        "combat_state": {},
        "current_location": new_location,
        "active_quests": active_quests,
        "completed_quests": completed_quests,
        "narrative_output": " ".join(narrative_parts),
        "visual_commands": visual_commands,
        "state_mutations": [],
        "game_phase": GamePhase.EXPLORATION.value,
    }
