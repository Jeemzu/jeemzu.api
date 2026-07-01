"""FastAPI application for the RPG multi-agent service."""

import uuid

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

from graph import rpg_graph
from state import GameState, CharacterClass, CharacterSheet, CLASS_BASE_STATS, Equipment, GamePhase
from tools.progression import apply_equipment_bonus

import json
from pathlib import Path


app = FastAPI(
    title="Jeemzu RPG",
    description="Multi-agent RPG orchestration service — AI Game Master",
    version="0.1.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost:5173",
        "http://localhost:5000",
        "https://jeemzu.me",
        "https://www.jeemzu.me",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# ─── In-memory session store (Phase 8 replaces with PostgresSaver) ────────────

_sessions: dict[str, dict] = {}


# ─── Request/Response Models ──────────────────────────────────────────────────


class PlayerCreate(BaseModel):
    player_id: str
    name: str
    character_class: str = Field(description="warrior, mage, or rogue")


class NewGameRequest(BaseModel):
    players: list[PlayerCreate] = Field(min_length=1, max_length=4)


class NewGameResponse(BaseModel):
    session_id: str
    narrative: str
    visual_commands: list[dict]
    ui_state: dict


class ActionRequest(BaseModel):
    player_id: str
    action: str = Field(min_length=1, max_length=500)


class ActionResponse(BaseModel):
    narrative: str
    visual_commands: list[dict]
    ui_state: dict
    action_type: str


class GameStateResponse(BaseModel):
    session_id: str
    current_location: str
    game_phase: str
    players: dict
    inventories: dict
    active_quests: list[dict]
    visited_locations: list[str]


# ─── Helpers ──────────────────────────────────────────────────────────────────


def _load_world() -> dict:
    world_path = Path(__file__).parent / "data" / "world.json"
    return json.loads(world_path.read_text())


def _create_initial_state(players: list[PlayerCreate]) -> dict:
    """Build the initial GameState for a new session."""
    world_data = _load_world()
    starting_location = world_data["starting_location"]
    starting_items = world_data["starting_items"]
    items_data = world_data["items"]

    # Build player characters
    players_dict = {}
    inventories_dict = {}

    for p in players:
        char_class = CharacterClass(p.character_class)
        base_stats = CLASS_BASE_STATS[char_class]

        character = CharacterSheet(
            player_id=p.player_id,
            name=p.name,
            character_class=char_class,
            stats=base_stats.model_copy(),
            equipment=Equipment(),
        )
        players_dict[p.player_id] = character.model_dump()

        # Starting inventory
        class_items = starting_items.get(p.character_class, [])
        inv = []
        for item_id in class_items:
            item_data = items_data.get(item_id)
            if item_data:
                inv.append({"id": item_id, **item_data})
        inventories_dict[p.player_id] = inv

        # Auto-equip the starting weapon/armor so their bonuses apply immediately —
        # players can re-equip different gear later via the "equip" action.
        character_dict = players_dict[p.player_id]
        for item in inv:
            slot = "weapon" if item["item_type"] == "weapon" else "armor_slot" if item["item_type"] == "armor" else None
            if slot and not character_dict["equipment"][slot]:
                character_dict["equipment"][slot] = item["id"]
                apply_equipment_bonus(character_dict["stats"], item.get("effect") or {}, reverse=False)

    # Get starting location data for visual commands
    start_loc_data = world_data["locations"].get(starting_location, {})

    return {
        "session_id": "",  # Set by caller
        "thread_id": "",
        "player_id": "",
        "player_action": "",
        "action_type": "",
        "players": players_dict,
        "inventories": inventories_dict,
        "turn_order": [p.player_id for p in players],
        "current_location": starting_location,
        "visited_locations": [starting_location],
        "active_quests": [],
        "completed_quests": [],
        "npc_target": "",
        "npc_memory": {},
        "game_phase": GamePhase.EXPLORATION.value,
        "combat_state": {},
        "recent_narrative": [],
        "action_log": [],
        "narrative_output": "",
        "visual_commands": [],
        "state_mutations": [],
        "response": "",
        "ui_state": {},
    }


# ─── Endpoints ────────────────────────────────────────────────────────────────


@app.post("/rpg/new", response_model=NewGameResponse)
async def create_game(request: NewGameRequest):
    """Create a new RPG session with the given players."""
    session_id = str(uuid.uuid4())

    # Validate character classes
    for p in request.players:
        if p.character_class not in [c.value for c in CharacterClass]:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid class '{p.character_class}'. Must be: warrior, mage, or rogue",
            )

    # Build initial state
    state = _create_initial_state(request.players)
    state["session_id"] = session_id
    state["thread_id"] = session_id

    # Generate opening narrative by invoking the graph with an "explore" action
    state["player_id"] = request.players[0].player_id
    state["player_action"] = "look around"
    state["action_type"] = "explore"

    result = await rpg_graph.ainvoke(state)

    # Store session state
    _sessions[session_id] = result

    # Build visual commands for initial scene
    world_data = _load_world()
    start_loc = world_data["locations"].get(result["current_location"], {})
    initial_visual_commands = [
        {
            "type": "load_map",
            "data": {
                "map": start_loc.get("tilemap", "village"),
                "spawn": start_loc.get("spawn", {"x": 5, "y": 5}),
            },
        },
    ]
    # Show NPCs
    for npc_id in start_loc.get("npcs", []):
        npc_data = world_data["npcs"].get(npc_id, {})
        initial_visual_commands.append({
            "type": "show_npc",
            "data": {
                "npc_id": npc_id,
                "sprite_key": npc_data.get("sprite_key", npc_id),
                "position": npc_data.get("position", {"x": 5, "y": 5}),
            },
        })

    # Merge with any commands from the graph
    all_visual_commands = initial_visual_commands + result.get("visual_commands", [])

    return NewGameResponse(
        session_id=session_id,
        narrative=result.get("response", "Your adventure begins..."),
        visual_commands=all_visual_commands,
        ui_state=result.get("ui_state", {}),
    )


@app.post("/rpg/{session_id}/action", response_model=ActionResponse)
async def submit_action(session_id: str, request: ActionRequest):
    """Submit a player action and get the AI-driven response."""
    if session_id not in _sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    # Load current state
    current_state = _sessions[session_id]

    # Reset per-action fields
    current_state["player_id"] = request.player_id
    current_state["player_action"] = request.action
    current_state["action_type"] = ""
    current_state["narrative_output"] = ""
    current_state["visual_commands"] = []
    current_state["state_mutations"] = []
    current_state["response"] = ""
    current_state["ui_state"] = {}

    # If player says goodbye during dialogue, exit dialogue mode
    if current_state.get("game_phase") == GamePhase.DIALOGUE.value:
        farewell_words = ["bye", "goodbye", "leave", "walk away", "go", "exit", "done"]
        if any(w in request.action.lower() for w in farewell_words):
            current_state["game_phase"] = GamePhase.EXPLORATION.value
            current_state["npc_target"] = ""

    # Invoke the graph
    result = await rpg_graph.ainvoke(current_state)

    # Store updated state
    _sessions[session_id] = result

    return ActionResponse(
        narrative=result.get("response", ""),
        visual_commands=result.get("visual_commands", []),
        ui_state=result.get("ui_state", {}),
        action_type=result.get("action_type", ""),
    )


@app.get("/rpg/{session_id}/state", response_model=GameStateResponse)
async def get_game_state(session_id: str):
    """Get the current game state for UI hydration."""
    if session_id not in _sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    state = _sessions[session_id]

    return GameStateResponse(
        session_id=session_id,
        current_location=state.get("current_location", ""),
        game_phase=state.get("game_phase", ""),
        players=state.get("players", {}),
        inventories=state.get("inventories", {}),
        active_quests=state.get("active_quests", []),
        visited_locations=state.get("visited_locations", []),
    )


@app.delete("/rpg/{session_id}")
async def delete_session(session_id: str):
    """Delete a game session."""
    if session_id in _sessions:
        del _sessions[session_id]
    return {"status": "deleted"}


@app.get("/rpg/{session_id}/export")
async def export_state(session_id: str):
    """Export the full game state for persistence (save file).

    Returns the raw state dict minus ephemeral per-action fields that are
    rebuilt every turn (narrative_output, visual_commands, etc.).
    """
    if session_id not in _sessions:
        raise HTTPException(status_code=404, detail="Session not found")

    state = _sessions[session_id]
    # Strip ephemeral fields to reduce save size — these are rebuilt every action
    ephemeral_keys = {
        "narrative_output", "visual_commands", "state_mutations",
        "response", "ui_state",
    }
    return {k: v for k, v in state.items() if k not in ephemeral_keys}


class ImportRequest(BaseModel):
    state: dict


@app.post("/rpg/import", response_model=NewGameResponse)
async def import_state(request: ImportRequest):
    """Import a previously saved game state and create a new session.

    Runs a lightweight action through the graph to generate fresh narrative
    and UI state for the restored session.
    """
    state = request.state
    session_id = str(uuid.uuid4())
    state["session_id"] = session_id
    state["thread_id"] = session_id

    # Reset ephemeral per-action fields
    state["narrative_output"] = ""
    state["visual_commands"] = []
    state["state_mutations"] = []
    state["response"] = ""
    state["ui_state"] = {}

    # Generate a "resuming" narrative by invoking the graph
    first_player = list(state.get("players", {}).keys())[0] if state.get("players") else ""
    state["player_id"] = first_player

    if state.get("game_phase") == GamePhase.COMBAT.value:
        state["player_action"] = "assess the situation"
        state["action_type"] = ""
    else:
        state["player_action"] = "look around"
        state["action_type"] = "explore"

    result = await rpg_graph.ainvoke(state)
    _sessions[session_id] = result

    # Build visual commands for current location
    world_data = _load_world()
    loc_data = world_data["locations"].get(result.get("current_location", ""), {})
    visual_cmds: list[dict] = [
        {
            "type": "load_map",
            "data": {
                "map": loc_data.get("tilemap", "village"),
                "spawn": loc_data.get("spawn", {"x": 5, "y": 5}),
            },
        }
    ]
    for npc_id in loc_data.get("npcs", []):
        npc_data = world_data["npcs"].get(npc_id, {})
        visual_cmds.append({
            "type": "show_npc",
            "data": {
                "npc_id": npc_id,
                "sprite_key": npc_data.get("sprite_key", npc_id),
                "position": npc_data.get("position", {"x": 5, "y": 5}),
            },
        })
    visual_cmds.extend(result.get("visual_commands", []))

    return NewGameResponse(
        session_id=session_id,
        narrative=result.get("response", "Your adventure continues..."),
        visual_commands=visual_cmds,
        ui_state=result.get("ui_state", {}),
    )


@app.get("/health")
@app.head("/health")
async def health():
    return {"status": "ok", "service": "jeemzu-rpg"}
