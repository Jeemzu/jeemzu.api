"""Game state schema and data models for the RPG multi-agent system."""

from __future__ import annotations

from enum import Enum
from typing import TypedDict

from pydantic import BaseModel, Field


# ─── Enums ────────────────────────────────────────────────────────────────────


class CharacterClass(str, Enum):
    WARRIOR = "warrior"
    MAGE = "mage"
    ROGUE = "rogue"


class ActionType(str, Enum):
    EXPLORE = "explore"
    TALK = "talk"
    FIGHT = "fight"
    USE_ITEM = "use_item"
    EXAMINE = "examine"
    REST = "rest"
    CAST_SPELL = "cast_spell"
    MOVE = "move"
    DEFEND = "defend"


class GamePhase(str, Enum):
    EXPLORATION = "exploration"
    COMBAT = "combat"
    DIALOGUE = "dialogue"


# ─── Character Models ─────────────────────────────────────────────────────────


class Stats(BaseModel):
    max_hp: int
    hp: int
    max_mp: int
    mp: int
    strength: int
    dexterity: int
    intelligence: int
    armor: int
    # Progenitors-inspired stats (see tools/progression.py):
    vitality: int = 0      # trains into bonus max HP
    agility: int = 0       # reduces chance of being hit (dodge)
    perception: int = 0    # increases chance to land hits
    # Corruption is deliberately excluded — it needs dedicated narrative content
    # (specific "dark path" enemies/choices) that doesn't exist in this MVP quest.


class Equipment(BaseModel):
    weapon: str | None = None
    armor_slot: str | None = None
    accessory: str | None = None


class CharacterSheet(BaseModel):
    player_id: str
    name: str
    character_class: CharacterClass
    level: int = 1
    xp: int = 0
    stats: Stats
    equipment: Equipment = Field(default_factory=Equipment)
    # Per-stat training progress (0-99 each) — see tools/progression.py. A stat's
    # value increases by 1 each time its progress crosses the training threshold.
    training_progress: dict[str, int] = Field(default_factory=dict)
    # Cumulative stat points gained but not yet converted into a total level-up.
    stat_points_gained: int = 0


class Item(BaseModel):
    id: str
    name: str
    item_type: str  # weapon, armor, consumable, quest_item
    description: str
    effect: dict | None = None  # e.g. {"heal_hp": 20} or {"damage_die": "d8", "stat": "strength"}


# ─── Combat Models ────────────────────────────────────────────────────────────


class CombatEntity(BaseModel):
    id: str
    name: str
    is_player: bool
    hp: int
    max_hp: int
    stats: Stats | None = None  # full stats for players, simplified for enemies
    sprite_key: str | None = None


class CombatState(BaseModel):
    enemies: list[CombatEntity]
    initiative_order: list[str]  # entity IDs in turn order
    current_turn_index: int = 0
    round_number: int = 1


# ─── Visual Command Models ────────────────────────────────────────────────────


class VisualCommand(BaseModel):
    type: str
    data: dict = Field(default_factory=dict)


# ─── Quest Models ─────────────────────────────────────────────────────────────


class QuestObjective(BaseModel):
    id: str
    description: str
    completed: bool = False


class Quest(BaseModel):
    id: str
    name: str
    description: str
    giver_npc: str
    objectives: list[QuestObjective]
    reward_xp: int = 0
    reward_items: list[str] = Field(default_factory=list)


# ─── LangGraph State ──────────────────────────────────────────────────────────


class GameState(TypedDict):
    """Shared state that flows through the LangGraph nodes."""

    # Session
    session_id: str
    thread_id: str

    # Player input (current action being processed)
    player_id: str
    player_action: str
    action_type: str  # ActionType value

    # Party (multiple players)
    players: dict  # player_id → CharacterSheet.model_dump()
    inventories: dict  # player_id → list of Item.model_dump()
    turn_order: list[str]  # player_ids for combat turns

    # World
    current_location: str  # location ID
    visited_locations: list[str]
    active_quests: list[dict]  # Quest.model_dump() objects
    completed_quests: list[str]  # quest IDs

    # NPC
    npc_target: str  # NPC ID currently being interacted with (empty string if none)
    npc_memory: dict  # NPC ID → list of summarized past interactions

    # Combat
    game_phase: str  # GamePhase value
    combat_state: dict  # CombatState.model_dump() or empty dict

    # Context (rolling window to manage token usage)
    recent_narrative: list[str]  # last 5 narrative outputs
    action_log: list[dict]  # last 10 actions: {"player_id", "action", "summary"}

    # Agent outputs (intermediate, reset per action)
    narrative_output: str
    visual_commands: list[dict]  # list of VisualCommand.model_dump()
    state_mutations: list[dict]  # structured changes to apply

    # Final response
    response: str
    ui_state: dict  # structured deltas for frontend


# ─── Starting Stats by Class ─────────────────────────────────────────────────

CLASS_BASE_STATS: dict[CharacterClass, Stats] = {
    CharacterClass.WARRIOR: Stats(
        max_hp=50, hp=50, max_mp=10, mp=10,
        strength=8, dexterity=4, intelligence=2, armor=4,
        vitality=6, agility=3, perception=3,
    ),
    CharacterClass.MAGE: Stats(
        max_hp=30, hp=30, max_mp=40, mp=40,
        strength=2, dexterity=4, intelligence=8, armor=1,
        vitality=2, agility=3, perception=6,
    ),
    CharacterClass.ROGUE: Stats(
        max_hp=38, hp=38, max_mp=20, mp=20,
        strength=4, dexterity=8, intelligence=4, armor=2,
        vitality=3, agility=7, perception=6,
    ),
}
