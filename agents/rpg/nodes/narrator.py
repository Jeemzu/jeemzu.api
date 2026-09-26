"""Narrator node — generates vivid scene descriptions and story progression."""

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage

from config import OPENAI_API_KEY, OPENAI_MODEL_NARRATOR
from state import GameState

import json
from pathlib import Path


NARRATOR_SYSTEM_PROMPT = """You are the Game Master narrator for "Progenitors: Echoes of the First",
a sci-fantasy RPG set on the planet Thrae in the year 6999.

Setting: {setting_summary}

Your job is to describe scenes, narrate actions, and advance the story.

Style:
- Second person ("You step into the lantern-lit square...")
- Sci-fantasy tone — ancient faith and starfaring wonder side by side; reverent and
  atmospheric, turning cold and wrong near discordant Aura
- Spirit Aura is real and visible in small ways: lantern-glow, humming waymarkers,
  the shrine-spring's threads of light
- Acknowledge ALL party members by name when relevant (this is multiplayer)

Pacing — this matters most:
Players act frequently; narration is the feedback loop, not the main event.
Save rich prose for moments that earn it (new discoveries, story beats). For
routine actions, be a crisp game master: state what happened and what's
actionable, then get out of the way. Never pad. Never re-describe scenery the
party has already seen unless something about it changed.

A NARRATION MODE with a hard sentence budget is given for each action.
Treat its sentence cap as absolute — going over the cap is a failure.

Rules:
- Base descriptions on the location data and current game state provided
- Do NOT invent new locations, NPCs, or items that aren't in the world data
- Do NOT resolve combat — that's handled by the combat agent
- If the player tries something impossible, narrate the failure gracefully and
  point at valid options (exits, NPCs) in the same breath
- Mechanical outcomes listed below are facts — reflect them accurately (exact
  numbers welcome), and never invent outcomes that aren't listed

Current context:
- Location: {location_name}
- Description: {location_description}
- Exits: {exits}
- NPCs here: {npcs}
- Party members: {party_members}
- Recent events: {recent_narrative}
- Active quests: {active_quests}

NARRATION MODE for this action:
{narration_directive}"""


# Mode directives, selected deterministically in _narration_directive().
_DIRECTIVE_OPENING = """SCENE-SETTING (campaign opening). Budget: 3-5 sentences.
Paint the starting scene with full atmosphere. End on something that invites
action — visible exits, an NPC, a point of interest."""

_DIRECTIVE_RESUME = """RE-ORIENTATION (resumed campaign). Budget: 2-3 sentences.
Welcome the party back into the moment: where they stand, what they were
pursuing (active quests), and the immediate options. No lengthy scenery."""

_DIRECTIVE_DISCOVERY = """DISCOVERY (first time at this location). Budget: 3-4 sentences.
This is a reveal — describe the new location with atmosphere, then surface
what's actionable: exits, NPCs, anything that stands out."""

_DIRECTIVE_STORY_BEAT = """STORY BEAT (quest progress this turn). Budget: 2-4 sentences.
Give the moment its due weight, then make the new objective or consequence clear."""

_DIRECTIVE_REVISIT = """FAMILIAR GROUND (returning to a known location). Budget: 1-2 sentences.
The party has been here before. Note the arrival and anything that changed or
is actionable. Do not re-describe the scenery."""

_DIRECTIVE_ROUTINE = """ROUTINE ACTION. Budget: 1-2 sentences.
Communicate information, not atmosphere: the direct result of the action plus
anything actionable (exits, NPCs, objects, changes). At most one brief flourish.
If nothing new was learned or changed, say so plainly and briefly."""


def _narration_directive(state: GameState) -> str:
    """Picks the response-length directive from deterministic game signals."""
    hint = state.get("narration_hint", "")
    if hint == "opening":
        return _DIRECTIVE_OPENING
    if hint == "resume":
        return _DIRECTIVE_RESUME

    mutations = state.get("state_mutations", [])
    move = next((m for m in mutations if m.get("type") == "moved"), None)
    if move is not None:
        return _DIRECTIVE_DISCOVERY if move.get("first_visit") else _DIRECTIVE_REVISIT

    if any(m.get("type") == "quest_progress" for m in mutations):
        return _DIRECTIVE_STORY_BEAT

    return _DIRECTIVE_ROUTINE


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

    # Structured location facts so brief responses can still point at options
    connections = location_data.get("connections", {})
    exits_text = (
        ", ".join(f"{direction} → {loc_id}" for direction, loc_id in connections.items())
        or "none"
    )
    npcs_text = ", ".join(location_data.get("npcs", [])) or "none"

    prompt = NARRATOR_SYSTEM_PROMPT.format(
        setting_summary=world_data.get("setting", {}).get("summary", ""),
        location_name=location_data.get("name", current_loc),
        location_description=location_data.get("description", ""),
        exits=exits_text,
        npcs=npcs_text,
        party_members=party_members,
        recent_narrative=recent_text,
        active_quests=quest_text,
        narration_directive=_narration_directive(state),
    )

    # Include the acting player's name
    acting_player = players.get(state.get("player_id", ""), {})
    acting_name = acting_player.get("name", "the adventurer") if acting_player else "the adventurer"

    # Surface what actually happened mechanically (heals, item use, movement, quest
    # progress) so info-first narration reports facts instead of inventing them.
    mutations = state.get("state_mutations", [])
    outcomes_text = json.dumps(mutations) if mutations else "none"

    messages = [
        SystemMessage(content=prompt),
        HumanMessage(
            content=(
                f"{acting_name}'s action: {state['player_action']}\n"
                f"Action type: {state['action_type']}\n"
                f"Mechanical outcomes this turn (JSON): {outcomes_text}"
            )
        ),
    ]

    response = await llm.ainvoke(messages)
    narrative = response.content

    return {"narrative_output": narrative}
