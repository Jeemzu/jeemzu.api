"""Character progression tools — per-stat training system inspired by the
"Progenitors" GDD's design: instead of one flat XP pool, each stat has its own
training progress. Defeating enemies grants "trains" progress to specific
stats based on what fighting them teaches the player (e.g. a tough melee
brute trains Strength/Vitality; an evasive spellcaster trains
Intelligence/Agility). Every couple of stat points gained (across any stats)
raises the player's overall level, which grants a small universal bump to
every stat. Every 5th level, training progress resets to make room for
further growth on a longer campaign.

Corruption (the GDD's "kills reduce future XP" stat) is deliberately not
implemented — it needs dedicated narrative content (specific morally-gray
enemies/choices) that doesn't exist in this MVP quest.
"""

from __future__ import annotations

import random

STAT_TRAINING_THRESHOLD = 100
STAT_POINTS_PER_LEVEL = 2
STAT_RESET_LEVEL_INTERVAL = 5

VITALITY_HP_PER_POINT = 3
INTELLIGENCE_MP_PER_POINT = 2

AGILITY_DODGE_PER_POINT = 0.02
PERCEPTION_HIT_PER_POINT = 0.02
BASE_HIT_CHANCE = 0.85
MIN_HIT_CHANCE = 0.5
MAX_HIT_CHANCE = 0.98

# Stats that get a small universal bump on every total level-up.
UNIVERSAL_LEVEL_STATS = ("strength", "dexterity", "intelligence", "vitality", "agility", "perception")


def roll_to_hit(attacker_perception: int, defender_agility: int) -> bool:
    """To-hit roll: base chance + attacker Perception - defender Agility, clamped."""
    chance = BASE_HIT_CHANCE + attacker_perception * PERCEPTION_HIT_PER_POINT - defender_agility * AGILITY_DODGE_PER_POINT
    chance = max(MIN_HIT_CHANCE, min(MAX_HIT_CHANCE, chance))
    return random.random() < chance


def apply_equipment_bonus(stats: dict, effect: dict, reverse: bool = False) -> None:
    """Applies (or reverses, on unequip) an item's stat bonuses directly onto a stats dict."""
    sign = -1 if reverse else 1
    if "armor_bonus" in effect:
        stats["armor"] = stats.get("armor", 0) + sign * effect["armor_bonus"]
    if "dexterity_bonus" in effect:
        stats["dexterity"] = stats.get("dexterity", 0) + sign * effect["dexterity_bonus"]


def _apply_single_stat_gain(player: dict, stat_name: str) -> None:
    """Applies the side effects of a stat's value increasing by 1 (e.g. Vitality -> more max HP)."""
    player["stats"][stat_name] = player["stats"].get(stat_name, 0) + 1
    if stat_name == "vitality":
        player["stats"]["max_hp"] += VITALITY_HP_PER_POINT
        player["stats"]["hp"] += VITALITY_HP_PER_POINT
    elif stat_name == "intelligence":
        player["stats"]["max_mp"] += INTELLIGENCE_MP_PER_POINT
        player["stats"]["mp"] += INTELLIGENCE_MP_PER_POINT


def apply_stat_training(player: dict, trains: dict[str, int]) -> list[str]:
    """Applies training progress from a defeated enemy. Returns the stats that gained a point this call."""
    progress = player.setdefault("training_progress", {})
    leveled_stats: list[str] = []

    for stat_name, amount in trains.items():
        if stat_name not in player["stats"]:
            continue
        progress[stat_name] = progress.get(stat_name, 0) + amount
        while progress[stat_name] >= STAT_TRAINING_THRESHOLD:
            progress[stat_name] -= STAT_TRAINING_THRESHOLD
            _apply_single_stat_gain(player, stat_name)
            leveled_stats.append(stat_name)

    if leveled_stats:
        player["stat_points_gained"] = player.get("stat_points_gained", 0) + len(leveled_stats)

    return leveled_stats


def check_total_level_up(player: dict) -> int:
    """Applies any pending total-level-ups from accumulated stat points. Returns levels gained."""
    levels_gained = 0

    while player.get("stat_points_gained", 0) >= STAT_POINTS_PER_LEVEL:
        player["stat_points_gained"] -= STAT_POINTS_PER_LEVEL
        player["level"] = player.get("level", 1) + 1
        levels_gained += 1

        # "Total level slightly affects every stat" — small universal bump layered
        # on top of whatever was individually trained.
        for stat_name in UNIVERSAL_LEVEL_STATS:
            _apply_single_stat_gain(player, stat_name)

        if player["level"] % STAT_RESET_LEVEL_INTERVAL == 0:
            player["training_progress"] = {}

    return levels_gained
