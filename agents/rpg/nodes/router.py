"""Router node — classifies player intent and determines which agent handles the action."""

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage
from pydantic import BaseModel, Field

from config import OPENAI_API_KEY, OPENAI_MODEL_FAST
from state import GameState, ActionType, GamePhase


class RouterDecision(BaseModel):
    """The router's classification of the player's intended action."""

    action_type: str = Field(
        description=f"The action type. Options: {', '.join(a.value for a in ActionType)}"
    )
    reasoning: str = Field(description="Brief explanation of the classification")


ROUTER_SYSTEM_PROMPT = """You are the intent router for a fantasy RPG game. Classify the player's action into one of these types:

- "explore": Moving to a new area, looking around, entering a room, going somewhere
- "talk": Speaking to an NPC, asking a question to a character, interacting with a person
- "fight": Attacking an enemy, initiating combat, drawing weapons
- "use_item": Using a potion, equipping gear, consuming something
- "examine": Looking at a specific object, inspecting something, reading, checking details
- "rest": Resting, sleeping, recovering, waiting
- "cast_spell": Casting a spell outside of combat
- "move": Simple movement within current location (not to a new location)

Context:
- Current location: {location}
- Game phase: {phase}
- Available NPCs here: {npcs}
- Available exits: {exits}

If in combat phase, most actions should map to "fight" unless they're clearly using an item or casting a spell.
If talking to an NPC, classify as "talk".
If moving to a connected location, classify as "explore"."""


async def router_node(state: GameState) -> dict:
    """Classifies the player's action and sets action_type."""
    # If in combat, default to fight unless clearly something else
    if state.get("game_phase") == GamePhase.COMBAT.value:
        action_lower = state["player_action"].lower()
        if any(word in action_lower for word in ["potion", "use", "drink", "eat"]):
            return {"action_type": ActionType.USE_ITEM.value}
        if any(word in action_lower for word in ["cast", "spell", "fireball", "heal"]):
            return {"action_type": ActionType.CAST_SPELL.value}
        if any(word in action_lower for word in ["defend", "block", "guard", "brace"]):
            return {"action_type": ActionType.DEFEND.value}
        if any(word in action_lower for word in ["flee", "run", "escape"]):
            return {"action_type": ActionType.EXPLORE.value}
        return {"action_type": ActionType.FIGHT.value}

    llm = ChatOpenAI(model=OPENAI_MODEL_FAST, api_key=OPENAI_API_KEY, temperature=0)
    structured_llm = llm.with_structured_output(RouterDecision)

    # Build context from state
    import json
    from pathlib import Path

    world_path = Path(__file__).parent.parent / "data" / "world.json"
    world_data = json.loads(world_path.read_text())

    current_loc = state.get("current_location", "village_square")
    location_data = world_data["locations"].get(current_loc, {})
    exits = list(location_data.get("connections", {}).keys())
    npcs = location_data.get("npcs", [])

    prompt = ROUTER_SYSTEM_PROMPT.format(
        location=location_data.get("name", current_loc),
        phase=state.get("game_phase", "exploration"),
        npcs=", ".join(npcs) if npcs else "none",
        exits=", ".join(exits) if exits else "none",
    )

    response = await structured_llm.ainvoke([
        SystemMessage(content=prompt),
        HumanMessage(content=f"Player action: {state['player_action']}"),
    ])

    return {"action_type": response.action_type}
