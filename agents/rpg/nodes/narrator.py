"""Narrator node — generates vivid scene descriptions and story progression."""

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage

from config import OPENAI_API_KEY, OPENAI_MODEL_NARRATOR
from state import GameState

import json
from pathlib import Path


NARRATOR_SYSTEM_PROMPT = """You are the Game Master narrator for a fantasy RPG called "The Sunken Crypt."
Your job is to describe scenes, narrate actions, and advance the story with vivid, atmospheric prose.

Style:
- Second person ("You step into the dimly lit chamber...")
- Concise but evocative — 2-4 sentences per response typically
- Dark fantasy tone — mysterious, atmospheric, occasionally tense
- Acknowledge ALL party members by name when relevant (this is multiplayer)
- Reference the environment, sounds, smells, lighting
- Build tension as the party ventures deeper

Rules:
- Base descriptions on the location data and current game state provided
- Do NOT invent new locations, NPCs, or items that aren't in the world data
- Do NOT resolve combat — that's handled by the combat agent
- If the player tries something impossible, narrate the failure gracefully
- Keep it brief — players want to act, not read novels

Current context:
- Location: {location_name}
- Description: {location_description}
- Party members: {party_members}
- Recent events: {recent_narrative}
- Active quests: {active_quests}"""


async def narrator_node(state: GameState) -> dict:
    """Generates narrative text for exploration and story moments."""
    llm = ChatOpenAI(model=OPENAI_MODEL_NARRATOR, api_key=OPENAI_API_KEY, temperature=0.8)

    # Load world data
    world_path = Path(__file__).parent.parent / "data" / "world.json"
    world_data = json.loads(world_path.read_text())

    current_loc = state.get("current_location", "village_square")
    location_data = world_data["locations"].get(current_loc, {})

    # Build party member names
    players = state.get("players", {})
    party_members = ", ".join(
        f"{p['name']} ({p['character_class']})" for p in players.values()
    ) or "Unknown adventurer"

    # Recent narrative for continuity
    recent = state.get("recent_narrative", [])
    recent_text = "\n".join(recent[-3:]) if recent else "The adventure has just begun."

    # Active quests
    quests = state.get("active_quests", [])
    quest_text = ", ".join(q["name"] for q in quests) if quests else "None"

    prompt = NARRATOR_SYSTEM_PROMPT.format(
        location_name=location_data.get("name", current_loc),
        location_description=location_data.get("description", ""),
        party_members=party_members,
        recent_narrative=recent_text,
        active_quests=quest_text,
    )

    # Include the acting player's name
    acting_player = players.get(state.get("player_id", ""), {})
    acting_name = acting_player.get("name", "the adventurer") if acting_player else "the adventurer"

    messages = [
        SystemMessage(content=prompt),
        HumanMessage(
            content=f"{acting_name}'s action: {state['player_action']}\nAction type: {state['action_type']}"
        ),
    ]

    response = await llm.ainvoke(messages)
    narrative = response.content

    return {"narrative_output": narrative}
