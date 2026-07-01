"""Deterministic dice rolling tools for the RPG system."""

import random


def roll(die: str, count: int = 1) -> list[int]:
    """Roll dice and return individual results.

    Args:
        die: Die type string (e.g., "d4", "d6", "d8", "d12", "d20").
        count: Number of dice to roll.

    Returns:
        List of individual roll results.
    """
    sides = int(die.lstrip("d"))
    return [random.randint(1, sides) for _ in range(count)]


def roll_total(die: str, count: int = 1) -> int:
    """Roll dice and return the sum."""
    return sum(roll(die, count))


def roll_d20() -> int:
    """Roll a single d20."""
    return random.randint(1, 20)


def roll_initiative(dexterity_modifier: int) -> int:
    """Roll initiative: d20 + DEX modifier."""
    return roll_d20() + dexterity_modifier


def stat_modifier(stat_value: int) -> int:
    """Calculate the modifier for a stat value (D&D-lite: stat - 4, min 0)."""
    return max(0, stat_value - 4)
