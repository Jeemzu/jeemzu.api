"""Enemy flavor node — adds AI-generated unique names and descriptions to enemies."""

from __future__ import annotations

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage

from state import GameState


async def enemy_flavor_node(state: GameState) -> dict:
    """
    Adds unique AI-generated flavor to dynamically generated enemies.
    
    Takes the generated_enemies from world_state_node and enriches them with:
    - Unique names (e.g., "Skeletal Warrior" → "Grimfang the Undying")
    - Atmospheric descriptions for the narrator
    - Thematic consistency with the campaign world
    
    Only runs when generated_enemies exist in state.
    """
    generated_enemies = state.get("generated_enemies", [])
    
    # Skip if no generated enemies or already flavored
    if not generated_enemies or generated_enemies[0].get("flavored"):
        return {}
    
    # Get campaign context
    current_location = state.get("current_location", "unknown location")
    recent_narrative = state.get("recent_narrative", [])
    context = " ".join(recent_narrative[-3:]) if recent_narrative else "The party ventures forth."
    
    # Build prompt for the LLM
    enemy_summary = "\n".join([
        f"- {e['name']} (CR {e.get('cr', 1.0)}, {e.get('archetype', 'generic')})"
        for e in generated_enemies
    ])
    
    prompt = f"""You are a fantasy dungeon master. The party has encountered these enemies at {current_location}:

{enemy_summary}

Previous context: {context}

For EACH enemy, provide:
1. A unique, memorable name (keep the base type but add personality)
2. A brief, atmospheric description (1-2 sentences, dark fantasy tone)

Format your response as:
ENEMY_1_NAME: <unique name>
ENEMY_1_DESC: <description>
ENEMY_2_NAME: <unique name>
ENEMY_2_DESC: <description>
...

Keep names concise (2-4 words). Descriptions should hint at personality/threat level."""
    
    llm = ChatOpenAI(model="gpt-4o-mini", temperature=0.9)  # High temp for creativity
    
    messages = [
        SystemMessage(content="You are a creative dungeon master specializing in atmospheric enemy descriptions."),
        HumanMessage(content=prompt),
    ]
    
    response = await llm.ainvoke(messages)
    flavor_text = response.content
    
    # Parse the LLM response
    lines = [line.strip() for line in flavor_text.split("\n") if line.strip()]
    enemy_index = 0
    flavored_enemies = []
    
    for enemy in generated_enemies:
        # Find the corresponding NAME and DESC lines
        name_prefix = f"ENEMY_{enemy_index + 1}_NAME:"
        desc_prefix = f"ENEMY_{enemy_index + 1}_DESC:"
        
        unique_name = enemy["name"]  # Fallback
        description = ""
        
        for line in lines:
            if line.startswith(name_prefix):
                unique_name = line.replace(name_prefix, "").strip()
            elif line.startswith(desc_prefix):
                description = line.replace(desc_prefix, "").strip()
        
        # Create flavored enemy dict
        flavored_enemy = dict(enemy)
        flavored_enemy["name"] = unique_name
        flavored_enemy["description"] = description
        flavored_enemy["flavored"] = True
        flavored_enemies.append(flavored_enemy)
        
        enemy_index += 1
    
    return {
        "generated_enemies": flavored_enemies,
    }
