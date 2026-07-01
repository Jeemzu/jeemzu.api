"""Skill check tools — D20-based ability checks against difficulty classes."""

from tools.dice import roll_d20, stat_modifier


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


# Standard difficulty classes
DC_EASY = 10
DC_MEDIUM = 15
DC_HARD = 20
DC_VERY_HARD = 25
