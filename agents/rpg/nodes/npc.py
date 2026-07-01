"""NPC dialogue node — personality-driven NPC conversations."""

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage

from config import OPENAI_API_KEY, OPENAI_MODEL_FAST
from state import GameState, GamePhase

import json
from pathlib import Path


NPC_SYSTEM_PROMPT = """You are roleplaying as {npc_name} in a fantasy RPG.

Your personality: {personality}

Background context: {dialogue_context}

Rules:
- Stay fully in character at all times
- Keep responses to 2-4 sentences — this is a game, not a novel
- If you can give quests: {quest_info}
- If you can trade: {trade_info}
- Reference the player by name when appropriate
- If asked about things you wouldn't know, deflect in character
- End dialogue naturally if the player says goodbye or walks away

Previous interactions with this player:
{memory}"""


async def npc_node(state: GameState) -> dict:
    """Handles NPC dialogue with personality-driven responses."""
    npc_id = state.get("npc_target", "")
    if not npc_id:
        return {"narrative_output": "There's no one here to talk to."}

    # Load world data
    world_path = Path(__file__).parent.parent / "data" / "world.json"
    world_data = json.loads(world_path.read_text())
    npc_data = world_data["npcs"].get(npc_id, {})

    if not npc_data:
        return {"narrative_output": "That person doesn't seem interested in talking."}

    llm = ChatOpenAI(model=OPENAI_MODEL_FAST, api_key=OPENAI_API_KEY, temperature=0.7)

    # Quest info
    quest_info = "You have no quests to offer."
    if npc_data.get("can_give_quests"):
        active_quest_ids = [q["id"] for q in state.get("active_quests", [])]
        completed_ids = state.get("completed_quests", [])
        available_quests = [
            qid for qid in npc_data["can_give_quests"]
            if qid not in active_quest_ids and qid not in completed_ids
        ]
        if available_quests:
            quest_details = []
            for qid in available_quests:
                q = world_data["quests"].get(qid, {})
                quest_details.append(f"'{q.get('name', qid)}': {q.get('description', '')}")
            quest_info = f"You can offer these quests (work them into conversation naturally): {'; '.join(quest_details)}"
        else:
            quest_info = "You've already given your quests. If they're complete, thank the adventurer."

    # Trade info
    trade_info = "You don't trade."
    if npc_data.get("can_trade"):
        shop = npc_data.get("shop_inventory", [])
        item_names = []
        for item_id in shop:
            item = world_data["items"].get(item_id, {})
            item_names.append(item.get("name", item_id))
        trade_info = f"You sell: {', '.join(item_names)}. Mention what you have if asked."

    # NPC memory
    npc_memory = state.get("npc_memory", {})
    memory_entries = npc_memory.get(npc_id, [])
    memory_text = "\n".join(memory_entries[-5:]) if memory_entries else "First time meeting this adventurer."

    # Build prompt
    prompt = NPC_SYSTEM_PROMPT.format(
        npc_name=npc_data.get("name", npc_id),
        personality=npc_data.get("personality", ""),
        dialogue_context=npc_data.get("dialogue_context", ""),
        quest_info=quest_info,
        trade_info=trade_info,
        memory=memory_text,
    )

    # Get acting player name
    players = state.get("players", {})
    acting_player = players.get(state.get("player_id", ""), {})
    acting_name = acting_player.get("name", "adventurer") if acting_player else "adventurer"

    messages = [
        SystemMessage(content=prompt),
        HumanMessage(content=f"{acting_name} says: {state['player_action']}"),
    ]

    response = await llm.ainvoke(messages)
    npc_response = response.content

    # Build visual commands for dialogue
    visual_commands = list(state.get("visual_commands", []))
    visual_commands.append({
        "type": "npc_speak",
        "data": {
            "npc_id": npc_id,
            "portrait": npc_data.get("portrait", npc_id),
            "npc_name": npc_data.get("name", npc_id),
        },
    })

    # Check if NPC offered a quest (simple heuristic — look for quest keywords in response)
    state_mutations = list(state.get("state_mutations", []))
    active_quests = list(state.get("active_quests", []))

    if npc_data.get("can_give_quests"):
        for qid in npc_data["can_give_quests"]:
            quest_data = world_data["quests"].get(qid, {})
            active_ids = [q["id"] for q in active_quests]
            if qid not in active_ids and qid not in state.get("completed_quests", []):
                # Check if player accepts (any affirmative in their message)
                action_lower = state["player_action"].lower()
                accept_words = ["yes", "accept", "sure", "okay", "ok", "help", "i'll", "will do", "deal"]
                if any(w in action_lower for w in accept_words):
                    # Normalize objectives to always include "completed" — the source
                    # world.json entries omit it (false is the implied default), but
                    # downstream code (world_state, combat) reads obj["completed"] directly.
                    objectives = [
                        {**obj, "completed": obj.get("completed", False)}
                        for obj in quest_data.get("objectives", [])
                    ]
                    quest_obj = {
                        "id": qid,
                        "name": quest_data.get("name", ""),
                        "description": quest_data.get("description", ""),
                        "giver_npc": npc_id,
                        "objectives": objectives,
                        "reward_xp": quest_data.get("reward_xp", 0),
                        "reward_items": quest_data.get("reward_items", []),
                    }
                    active_quests.append(quest_obj)
                    state_mutations.append({"type": "quest_accepted", "quest": qid})
                    visual_commands.append({"type": "quest_accepted", "data": {"quest_name": quest_data.get("name", "")}})

    # Update NPC memory
    npc_memory = dict(state.get("npc_memory", {}))
    if npc_id not in npc_memory:
        npc_memory[npc_id] = []
    npc_memory[npc_id].append(f"{acting_name}: {state['player_action'][:80]}")
    # Keep last 10 interactions
    npc_memory[npc_id] = npc_memory[npc_id][-10:]

    return {
        "narrative_output": npc_response,
        "visual_commands": visual_commands,
        "state_mutations": state_mutations,
        "active_quests": active_quests,
        "npc_memory": npc_memory,
    }
