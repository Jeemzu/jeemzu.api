"""LangGraph graph definition — the Budgetize assistant."""

from langgraph.graph import END, StateGraph

from app.budget.nodes.analyst import analyst_node
from app.budget.nodes.composer import composer_node
from app.budget.nodes.gap_reporter import gap_reporter_node
from app.budget.nodes.planner import planner_node
from app.budget.nodes.router import router_node
from app.budget.state import BudgetAgentState


def route_after_router(state: BudgetAgentState) -> str:
    intent = state.get("intent", "question")
    if intent == "change_request":
        return "planner"
    if intent == "unsupported":
        return "gap_reporter"
    return "analyst"


def build_graph() -> StateGraph:
    graph = StateGraph(BudgetAgentState)

    graph.add_node("router", router_node)
    graph.add_node("analyst", analyst_node)
    graph.add_node("planner", planner_node)
    graph.add_node("gap_reporter", gap_reporter_node)
    graph.add_node("composer", composer_node)

    graph.set_entry_point("router")

    graph.add_conditional_edges(
        "router",
        route_after_router,
        {"analyst": "analyst", "planner": "planner", "gap_reporter": "gap_reporter"},
    )

    # Everything funnels through the composer so proposals are always validated.
    graph.add_edge("analyst", "composer")
    graph.add_edge("planner", "composer")
    graph.add_edge("gap_reporter", "composer")
    graph.add_edge("composer", END)

    return graph


budget_graph = build_graph().compile()
