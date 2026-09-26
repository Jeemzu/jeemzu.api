"""Enemy encounter generator for balanced combat."""

from __future__ import annotations

import json
import random
from pathlib import Path
from typing import Any

from state import CombatEntity, DamageType, Stats

# Path to enemy templates
TEMPLATES_PATH = Path(__file__).parent.parent / "data" / "enemy_templates.json"


def load_templates() -> list[dict[str, Any]]:
    """Load enemy templates from JSON file."""
    with open(TEMPLATES_PATH, "r", encoding="utf-8") as f:
        data = json.load(f)
    return data["templates"]


def scale_stats(base_stats: dict[str, int], cr_multiplier: float) -> dict[str, int]:
    """Scale enemy stats based on CR multiplier."""
    return {
        "max_hp": int(base_stats["max_hp"] * cr_multiplier),
        "strength": base_stats["strength"] + int(cr_multiplier / 0.5),
        "dexterity": base_stats["dexterity"] + int(cr_multiplier / 0.5),
        "intelligence": base_stats["intelligence"] + int(cr_multiplier / 0.5),
        "armor": base_stats["armor"] + int(cr_multiplier / 0.7),
    }


def calculate_party_power(party_levels: list[int]) -> float:
    """
    Calculate total party power level for encounter balancing.
    
    Args:
        party_levels: List of player levels
    
    Returns:
        Target CR for the encounter
    """
    avg_level = sum(party_levels) / len(party_levels)
    party_size_multiplier = 1.0 + (len(party_levels) - 1) * 0.3  # Each additional player adds 30%
    return avg_level * party_size_multiplier


def select_templates_by_cr(
    templates: list[dict[str, Any]],
    target_cr: float,
    theme: str | None = None,
    max_enemies: int = 5,
) -> list[dict[str, Any]]:
    """
    Select enemy templates to match target CR.
    
    Args:
        templates: List of all enemy templates
        target_cr: Target challenge rating for the encounter
        theme: Optional theme filter (e.g., "undead", "elemental", "boss")
        max_enemies: Maximum number of enemies to spawn
    
    Returns:
        List of selected enemy templates
    """
    # Filter by theme if specified
    if theme:
        filtered = [t for t in templates if theme.lower() in t["archetype"].lower()]
        if not filtered:
            filtered = templates  # Fallback to all if no match
    else:
        filtered = templates
    
    # Remove boss enemies unless specifically themed or target CR is high
    if not theme or "boss" not in theme.lower():
        if target_cr < 8.0:
            filtered = [t for t in filtered if "boss" not in t["archetype"].lower()]
    
    # Select enemies to match target CR
    selected = []
    current_cr = 0.0
    attempts = 0
    max_attempts = 50
    
    while current_cr < target_cr * 0.8 and len(selected) < max_enemies and attempts < max_attempts:
        # Calculate remaining CR needed
        remaining_cr = target_cr - current_cr
        
        # Find templates within reasonable CR range
        suitable = [
            t for t in filtered
            if t["cr"] <= remaining_cr * 1.3 and t["cr"] >= remaining_cr * 0.3
        ]
        
        if not suitable:
            # If no suitable templates, pick any that fit
            suitable = [t for t in filtered if t["cr"] <= remaining_cr * 1.5]
        
        if not suitable:
            break
        
        # Select random template
        template = random.choice(suitable)
        selected.append(template)
        current_cr += template["cr"]
        attempts += 1
    
    # Ensure at least one enemy
    if not selected:
        selected.append(random.choice(filtered))
    
    return selected


def create_combat_entity(
    template: dict[str, Any],
    entity_id: str,
    cr_multiplier: float = 1.0,
    name_suffix: str = "",
) -> CombatEntity:
    """
    Create a CombatEntity from a template.
    
    Args:
        template: Enemy template dictionary
        entity_id: Unique ID for this entity
        cr_multiplier: Multiplier for stat scaling
        name_suffix: Optional suffix for the name (e.g., "Alpha", "Elder")
    
    Returns:
        CombatEntity instance
    """
    scaled_stats = scale_stats(template["base_stats"], cr_multiplier)
    
    # Build name with suffix
    name = template["name"]
    if name_suffix:
        name = f"{name_suffix} {name}"
    
    # Convert resistances and weaknesses to proper format
    resistances = {k: v for k, v in template.get("resistances", {}).items()}
    weaknesses = {k: v for k, v in template.get("weaknesses", {}).items()}
    
    # Create Stats object for non-player entity
    stats = Stats(
        max_hp=scaled_stats["max_hp"],
        hp=scaled_stats["max_hp"],
        max_mp=0,
        mp=0,
        strength=scaled_stats["strength"],
        dexterity=scaled_stats["dexterity"],
        intelligence=scaled_stats["intelligence"],
        armor=scaled_stats["armor"],
        vitality=0,
        agility=0,
        perception=0,
    )
    
    return CombatEntity(
        id=entity_id,
        name=name,
        is_player=False,
        hp=scaled_stats["max_hp"],
        max_hp=scaled_stats["max_hp"],
        stats=stats,
        sprite_key=template.get("sprite_key"),
        resistances=resistances,
        weaknesses=weaknesses,
    )


def generate_encounter(
    party_levels: list[int],
    party_size: int,
    theme: str | None = None,
    difficulty_modifier: float = 1.0,
) -> tuple[list[CombatEntity], list[str]]:
    """
    Generate a balanced combat encounter.
    
    Args:
        party_levels: List of player levels
        party_size: Number of players in the party
        theme: Optional theme (e.g., "undead", "fire", "boss_dragon")
        difficulty_modifier: Multiplier for difficulty (0.9 = easier, 1.1 = harder)
    
    Returns:
        Tuple of (list of CombatEntity, list of ability names)
    """
    # Load templates
    templates = load_templates()
    
    # Calculate target CR
    base_cr = calculate_party_power(party_levels)
    target_cr = base_cr * difficulty_modifier
    
    # Select templates
    selected_templates = select_templates_by_cr(templates, target_cr, theme)
    
    # Create combat entities
    entities = []
    abilities_pool = []
    
    for i, template in enumerate(selected_templates):
        # Add variety with name suffixes for multiples of the same type
        count_of_type = sum(1 for t in selected_templates[:i] if t["id"] == template["id"])
        suffix = ""
        if count_of_type > 0:
            suffixes = ["Alpha", "Elder", "Savage", "Corrupted", "Ancient"]
            suffix = suffixes[count_of_type % len(suffixes)]
        
        entity_id = f"enemy_{i}_{template['id']}"
        entity = create_combat_entity(template, entity_id, 1.0, suffix)
        entities.append(entity)
        
        # Collect abilities
        abilities_pool.extend(template.get("abilities", []))
    
    return entities, abilities_pool


def get_arena_effects_for_templates(templates: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """
    Determine potential arena effects based on enemy templates.
    
    Args:
        templates: List of enemy template dictionaries
    
    Returns:
        List of potential arena effects with their probabilities
    """
    effects = []
    
    for template in templates:
        if "arena_effect_chance" in template:
            for effect_type, chance in template["arena_effect_chance"].items():
                if random.random() < chance:
                    # Map effect type to damage type and description
                    effect_data = {
                        "burning_ground": {
                            "damage_type": DamageType.FIRE,
                            "magnitude": 1.25,
                            "description": "The ground smolders with intense heat, boosting fire damage.",
                        },
                        "icy_terrain": {
                            "damage_type": DamageType.ICE,
                            "magnitude": 1.25,
                            "description": "Frost covers the battlefield, strengthening ice attacks.",
                        },
                        "electrified": {
                            "damage_type": DamageType.LIGHTNING,
                            "magnitude": 1.5,
                            "description": "The air crackles with electricity, amplifying lightning strikes.",
                        },
                        "poisoned_air": {
                            "damage_type": DamageType.POISON,
                            "magnitude": 1.3,
                            "description": "Toxic fumes fill the air, empowering poison attacks.",
                        },
                        "cursed_ground": {
                            "damage_type": DamageType.DARK,
                            "magnitude": 1.4,
                            "description": "Dark energy permeates the area, enhancing shadow magic.",
                        },
                        "burning_inferno": {
                            "damage_type": DamageType.FIRE,
                            "magnitude": 1.5,
                            "description": "An inferno rages across the battlefield, greatly boosting fire damage.",
                        },
                        "raging_storm": {
                            "damage_type": DamageType.LIGHTNING,
                            "magnitude": 1.4,
                            "description": "A violent storm surrounds the battlefield, empowering thunder and lightning.",
                        },
                        "necrotic_aura": {
                            "damage_type": DamageType.DARK,
                            "magnitude": 1.6,
                            "description": "Necrotic energy drains life, strengthening dark magic.",
                        },
                    }
                    
                    if effect_type in effect_data:
                        effects.append({
                            "effect_type": effect_type,
                            "duration_turns": random.randint(3, 5),
                            **effect_data[effect_type],
                        })
    
    return effects
