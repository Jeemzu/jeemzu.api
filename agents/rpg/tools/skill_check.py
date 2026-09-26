"""Skill check tools — D20-based ability checks against difficulty classes + d100 stat checks."""

from tools.dice import roll_d20, stat_modifier
import random


def skill_check(stat_value: int, difficulty_class: int) -> tuple[bool, int, int]:
    """Perform a skill check: d20 + stat modifier vs DC.

    Args:
        stat_value: The relevant stat (strength, dexterity, intelligence).
        difficulty_class: The DC to beat (10=easy, 15=medium, 20=hard, 25=very hard).

    Returns:
        Tuple of (passed: bool, roll: int, total: int).
    """
    d20 = roll_d20()
    modifier = stat_modifier(stat_value)
    total = d20 + modifier
    passed = total >= difficulty_class
    return passed, d20, total


def contested_check(attacker_stat: int, defender_stat: int) -> tuple[bool, int, int]:
    """Contested check — both sides roll, higher total wins.

    Returns:
        Tuple of (attacker_wins: bool, attacker_total: int, defender_total: int).
    """
    atk_roll = roll_d20() + stat_modifier(attacker_stat)
    def_roll = roll_d20() + stat_modifier(defender_stat)
    return atk_roll >= def_roll, atk_roll, def_roll


def calculate_stat_check_chance(stat_value: int, threshold: int) -> int:
    """
    Calculate the percentage chance of passing a Fallout-style stat check (d100 system).
    
    Base chance = 50%
    +5% per point of stat above threshold
    -5% per point of stat below threshold
    Clamped to [10%, 95%] range
    
    Args:
        stat_value: The player's current stat value
        threshold: The minimum stat required for guaranteed success
    
    Returns:
        Percentage chance (10-95)
    
    Examples:
        calculate_stat_check_chance(8, 6) → 60% (2 above threshold)
        calculate_stat_check_chance(4, 6) → 40% (2 below threshold)
        calculate_stat_check_chance(15, 6) → 95% (9 above threshold, capped)
    """
    base_chance = 50
    stat_diff = stat_value - threshold
    chance = base_chance + (stat_diff * 5)
    return max(10, min(95, chance))


def build_dialogue_option_payloads(npc_data: dict, player_stats: dict) -> list[dict]:
    """Builds frontend-ready payloads for an NPC's stat-gated dialogue options.

    Includes the acting player's stat value and success chance so the UI can
    show Fallout-style percentages next to each option.
    """
    payloads = []
    for option in npc_data.get("dialogue_options", []):
        stat_req = option.get("stat_requirement", {})
        stat_name = stat_req.get("stat", "")
        threshold = stat_req.get("threshold", 0)
        player_value = player_stats.get(stat_name, 0)
        payloads.append({
            "id": option.get("id", ""),
            "prompt": option.get("prompt", ""),
            "stat": stat_name,
            "threshold": threshold,
            "player_value": player_value,
            "chance": calculate_stat_check_chance(player_value, threshold),
        })
    return payloads


def roll_stat_check(stat_value: int, threshold: int) -> tuple[bool, int, int]:
    """
    Roll a Fallout-style stat check (d100 vs calculated percentage).
    
    Args:
        stat_value: The player's current stat value
        threshold: The minimum stat required for the check
    
    Returns:
        Tuple of (success: bool, roll: int, chance: int)
    
    Example:
        roll_stat_check(7, 6) → (True, 42, 55) — rolled 42, needed ≤55
    """
    chance = calculate_stat_check_chance(stat_value, threshold)
    roll = random.randint(1, 100)
    success = roll <= chance
    return success, roll, chance


# Standard difficulty classes
DC_EASY = 10
DC_MEDIUM = 15
DC_HARD = 20
DC_VERY_HARD = 25
