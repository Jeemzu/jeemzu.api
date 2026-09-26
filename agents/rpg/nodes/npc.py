"""NPC dialogue node — personality-driven NPC conversations."""

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage, AIMessage

from config import OPENAI_API_KEY, OPENAI_MODEL_FAST
from state import GameState, GamePhase
from tools.skill_check import (
    build_dialogue_option_payloads,
    calculate_stat_check_chance,
    roll_stat_check,
)

import json
from pathlib import Path


NPC_SYSTEM_PROMPT = """You are roleplaying as {npc_name} in "Progenitors: Echoes of the First",
a sci-fantasy RPG set on the planet Thrae in the year 6999, weeks before the New
Millennium celebration at Preacher's Peak. Spirit Aura — the energy all races draw
on — flows through everything; lately, discordant Aura has been seeping from the
Hollow of the First nearby.

WHO YOU ARE:
{personality_summary}

YOUR TRAITS: {traits}

HOW YOU SPEAK: {speech_style}

REPEATED QUESTIONS — if the conversation history shows the player already asked
this (or nearly this) and you already answered:
{repetition_behavior}
Never robotically repeat a previous answer word-for-word.

CONVERSATIONAL HICCUPS — if the player's message is garbled, vague, contradictory,
or makes no sense in context:
{confusion_behavior}

YOU CARE ABOUT: {passions}
YOU DISLIKE: {dislikes}

Background you know: {dialogue_context}

Rules:
- Stay fully in character at all times — tone, vocabulary, and mood follow your traits
- Keep responses to 2-4 sentences — this is a game, not a novel
- If you can give quests: {quest_info}
- If you can trade: {trade_info}
- Reference the player by name when appropriate
- If asked about things you wouldn't know, deflect in character
- Do NOT invent locations, NPCs, items, or lore beyond your background
- End dialogue naturally if the player says goodbye or walks away

Conversation so far with this party:
{memory}"""


def _format_personality(npc_data: dict) -> dict:
    """Extracts prompt fields from the structured personality block.

    Falls back gracefully if an NPC still uses a plain-string personality.
    """
    personality = npc_data.get("personality", "")
    if isinstance(personality, str):
        return {
            "personality_summary": personality or "A local of Pilgrim's Rest.",
            "traits": "unremarkable",
            "speech_style": "Plain and direct.",
            "repetition_behavior": "Point out you've already answered, then answer again more briefly.",
            "confusion_behavior": "Ask the player to clarify, in character.",
            "passions": "day-to-day life",
            "dislikes": "rudeness",
        }

    race = personality.get("race", "")
    summary = personality.get("summary", "")
    return {
        "personality_summary": f"({race}) {summary}".strip(),
        "traits": ", ".join(personality.get("traits", [])) or "unremarkable",
        "speech_style": personality.get("speech_style", "Plain and direct."),
        "repetition_behavior": personality.get(
            "on_repeated_question",
            "Point out you've already answered, then answer again more briefly.",
        ),
        "confusion_behavior": personality.get(
            "on_confusion", "Ask the player to clarify, in character."
        ),
        "passions": ", ".join(personality.get("passions", [])) or "day-to-day life",
        "dislikes": ", ".join(personality.get("dislikes", [])) or "rudeness",
    }


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
    
    # Get acting player
    players = state.get("players", {})
    acting_player = players.get(state.get("player_id", ""), {})
    acting_name = acting_player.get("name", "adventurer") if acting_player else "adventurer"
    player_stats = acting_player.get("stats", {})
    
    # Check for stat-gated dialogue options BEFORE calling LLM
    player_action = state.get("player_action", "").lower()
    dialogue_options = npc_data.get("dialogue_options", [])
    visual_commands = list(state.get("visual_commands", []))
    state_mutations = list(state.get("state_mutations", []))
    
    # Look for bracket notation like "[Persuade]", "[Intimidate]", etc.
    for option in dialogue_options:
        option_prompt_lower = option["prompt"].lower()
        # Extract the stat check label (e.g., "[persuade]" → "persuade")
        if "[" in option_prompt_lower:
            label_start = option_prompt_lower.index("[") + 1
            label_end = option_prompt_lower.index("]")
            stat_label = option_prompt_lower[label_start:label_end]
            
            # Check if player action contains this label
            if stat_label in player_action:
                # Perform the stat check
                stat_req = option["stat_requirement"]
                stat_name = stat_req["stat"]
                threshold = stat_req["threshold"]
                player_stat_value = player_stats.get(stat_name, 0)
                
                success, roll, chance = roll_stat_check(player_stat_value, threshold)
                
                # Build response based on success/failure
                if success:
                    response_text = option["success_response"]
                    reward = option.get("reward_on_success", {})
                    
                    # Apply rewards
                    inventories = dict(state.get("inventories", {}))
                    player_id = state.get("player_id", "")
                    
                    if "item" in reward:
                        item_id = reward["item"]
                        item_data = world_data["items"].get(item_id)
                        if item_data:
                            inventory = inventories.setdefault(player_id, [])
                            inventory.append({"id": item_id, **item_data})
                            state_mutations.append({
                                "type": "item_gained",
                                "item": item_data["name"],
                                "player_id": player_id,
                            })
                    
                    if "xp_bonus" in reward:
                        acting_player["xp"] = acting_player.get("xp", 0) + reward["xp_bonus"]
                        state_mutations.append({
                            "type": "xp_gained",
                            "amount": reward["xp_bonus"],
                            "player_id": player_id,
                        })
                    
                    if "shop_discount" in reward:
                        # Store discount in NPC memory or state
                        state_mutations.append({
                            "type": "shop_discount",
                            "npc": npc_id,
                            "discount": reward["shop_discount"],
                        })
                    
                    if "shop_unlock" in reward:
                        state_mutations.append({
                            "type": "shop_unlock",
                            "npc": npc_id,
                            "item": reward["shop_unlock"],
                        })
                    
                    if "lore_unlock" in reward:
                        state_mutations.append({
                            "type": "lore_unlock",
                            "lore_id": reward["lore_unlock"],
                        })
                    
                    if "spell_unlock" in reward:
                        state_mutations.append({
                            "type": "spell_unlock",
                            "spell": reward["spell_unlock"],
                            "player_id": player_id,
                        })
                    
                    result_text = f"[{stat_name.upper()} CHECK: {player_stat_value}/{threshold} - Rolled {roll}, needed ≤{chance}% - SUCCESS]\n\n{response_text}"
                else:
                    response_text = option["failure_response"]
                    result_text = f"[{stat_name.upper()} CHECK: {player_stat_value}/{threshold} - Rolled {roll}, needed ≤{chance}% - FAILED]\n\n{response_text}"
                
                # Build visual commands
                visual_commands.append({
                    "type": "npc_speak",
                    "data": {
                        "npc_id": npc_id,
                        "portrait": npc_data.get("portrait", npc_id),
                        "npc_name": npc_data.get("name", npc_id),
                        "dialogue_options": build_dialogue_option_payloads(npc_data, player_stats),
                    },
                })
                
                visual_commands.append({
                    "type": "stat_check_result",
                    "data": {
                        "stat": stat_name,
                        "threshold": threshold,
                        "player_value": player_stat_value,
                        "roll": roll,
                        "chance": chance,
                        "success": success,
                    },
                })
                
                return {
                    "narrative_output": result_text,
                    "visual_commands": visual_commands,
                    "state_mutations": state_mutations,
                    "players": players,
                    "inventories": inventories,
                }

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

    # NPC memory — both sides of the conversation, so the NPC can recognize
    # repeated questions and react per its personality instead of re-answering.
    npc_memory = state.get("npc_memory", {})
    memory_entries = npc_memory.get(npc_id, [])
    memory_text = "\n".join(memory_entries[-12:]) if memory_entries else "This is your first exchange with this party."

    # Build prompt
    personality_fields = _format_personality(npc_data)
    prompt = NPC_SYSTEM_PROMPT.format(
        npc_name=npc_data.get("name", npc_id),
        dialogue_context=npc_data.get("dialogue_context", ""),
        quest_info=quest_info,
        trade_info=trade_info,
        memory=memory_text,
        **personality_fields,
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
            "dialogue_options": build_dialogue_option_payloads(npc_data, player_stats),
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

    # Update NPC memory with BOTH sides of the exchange — seeing its own past
    # answers is what lets the NPC notice "you already asked me that".
    npc_memory = dict(state.get("npc_memory", {}))
    if npc_id not in npc_memory:
        npc_memory[npc_id] = []
    npc_name = npc_data.get("name", npc_id)
    npc_memory[npc_id].append(f"{acting_name}: {state['player_action'][:100]}")
    npc_memory[npc_id].append(f"{npc_name}: {str(npc_response)[:140]}")
    # Keep the last 20 lines (~10 exchanges)
    npc_memory[npc_id] = npc_memory[npc_id][-20:]

    return {
        "narrative_output": npc_response,
        "visual_commands": visual_commands,
        "state_mutations": state_mutations,
        "active_quests": active_quests,
        "npc_memory": npc_memory,
    }
