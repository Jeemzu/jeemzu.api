"""LangGraph graph definition — multi-agent RPG orchestration."""

from langgraph.graph import StateGraph, END

from state import GameState, GamePhase
from nodes.router import router_node
from nodes.narrator import narrator_node
from nodes.npc import npc_node
from nodes.world_state import world_state_node
from nodes.combat import combat_node
from nodes.composer import composer_node


def route_after_router(state: GameState) -> str:
    """Conditional edge: routes to the appropriate agent based on action type."""
    # world_state always runs first — it bookkeeps the action log and, when not already
    # in combat, handles exploration/dialogue/item mutations (including detecting newly
    # triggered encounters). route_after_world_state decides where to go from there.
    return "world_state"


def route_after_world_state(state: GameState) -> str:
    """After world state mutations, decide which agent handles the actual resolution."""
    game_phase = state.get("game_phase", "")
    npc_target = state.get("npc_target", "")

    # In combat (either just triggered this turn, or already ongoing) — combat_node
    # owns initiative, turn resolution, and win/loss/flee handling.
    if game_phase == GamePhase.COMBAT.value:
        return "combat"

    # If in dialogue with an NPC, route to NPC agent
    if game_phase == GamePhase.DIALOGUE.value and npc_target:
        return "npc"

    # Otherwise, narrator handles it
    return "narrator"


def build_graph() -> StateGraph:
    """Constructs the RPG multi-agent orchestration graph."""
    graph = StateGraph(GameState)

    # Add nodes
    graph.add_node("router", router_node)
    graph.add_node("world_state", world_state_node)
    graph.add_node("narrator", narrator_node)
    graph.add_node("npc", npc_node)
    graph.add_node("combat", combat_node)
    graph.add_node("composer", composer_node)

    # Entry point
    graph.set_entry_point("router")

    # Router → world_state (always processes state mutations first)
    graph.add_conditional_edges("router", route_after_router, {
        "world_state": "world_state",
    })

    # World state → narrator, npc, or combat (depending on game phase)
    graph.add_conditional_edges("world_state", route_after_world_state, {
        "narrator": "narrator",
        "npc": "npc",
        "combat": "combat",
    })

    # Narrator → composer
    graph.add_edge("narrator", "composer")

    # NPC → composer
    graph.add_edge("npc", "composer")

    # Combat → composer
    graph.add_edge("combat", "composer")

    # Composer → END
    graph.add_edge("composer", END)

    return graph


# Compile the graph at module level
rpg_graph = build_graph().compile()
