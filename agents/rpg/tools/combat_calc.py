"""Combat calculation tools — deterministic damage, defense, and loot resolution."""

from __future__ import annotations

import random

from tools.dice import roll_total, stat_modifier


def calculate_attack_damage(
    weapon_die: str,
    attacker_stat: int,
    target_armor: int,
    hits: int = 1,
    critical: bool = False,
) -> int:
    """Calculate total damage for an attack.

    Args:
        weapon_die: Die string for the weapon (e.g., "d8").
        attacker_stat: The relevant stat value (STR/DEX/INT).
        target_armor: Target's armor value.
        hits: Number of hits (e.g., dual daggers = 2).
        critical: If True, double the dice damage.

    Returns:
        Final damage dealt (minimum 1 if attack hits at all).
    """
    total_damage = 0
    modifier = stat_modifier(attacker_stat)

    for _ in range(hits):
        die_damage = roll_total(weapon_die)
        if critical:
            die_damage *= 2
        hit_damage = die_damage + modifier - target_armor
        total_damage += max(1, hit_damage)

    return total_damage


def calculate_spell_damage(
    spell_die: str,
    caster_intelligence: int,
    target_armor: int,
    spell_modifier: int = 0,
) -> int:
    """Calculate spell damage. Spells partially bypass armor (halved)."""
    die_damage = roll_total(spell_die)
    int_mod = stat_modifier(caster_intelligence)
    effective_armor = target_armor // 2  # spells bypass half armor
    damage = die_damage + int_mod + spell_modifier - effective_armor
    return max(1, damage)


def calculate_heal(heal_amount: int, caster_intelligence: int) -> int:
    """Calculate healing amount — base + INT modifier bonus."""
    bonus = stat_modifier(caster_intelligence) // 2
    return heal_amount + bonus


def resolve_loot_drop(loot_table: list[dict]) -> list[str]:
    """Resolve which items drop from a loot table.

    Args:
        loot_table: List of {"item_id": str, "chance": float (0-1)}.

    Returns:
        List of item IDs that dropped.
    """
    drops = []
    for entry in loot_table:
        if random.random() <= entry["chance"]:
            drops.append(entry["item_id"])
    return drops
