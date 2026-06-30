from langgraph.graph import StateGraph, END

from state import AgentState
from nodes.router import router_node
from nodes.knowledge import knowledge_node
from nodes.game_stats import game_stats_node
from nodes.web_search import web_search_node
from nodes.synthesizer import synthesizer_node


def route_to_agents(state: AgentState) -> list[str]:
    """Conditional edge: routes to the agents selected by the router."""
    agents = state.get("agents_to_run", [])
    destinations = []

    if "knowledge" in agents:
        destinations.append("knowledge")
    if "game_stats" in agents:
        destinations.append("game_stats")
    if "web_search" in agents:
        destinations.append("web_search")

    # Chitchat or fallback — go straight to synthesizer
    if not destinations or "chitchat" in agents:
        destinations.append("chitchat_passthrough")

    return destinations


def chitchat_passthrough(state: AgentState) -> dict:
    """For simple chitchat, generate a direct response without tools."""
    from langchain_openai import ChatOpenAI
    from langchain_core.messages import BaseMessage, SystemMessage, HumanMessage, AIMessage

    from config import OPENAI_API_KEY, OPENAI_MODEL

    llm = ChatOpenAI(model=OPENAI_MODEL, api_key=OPENAI_API_KEY, temperature=0.7)

    messages: list[BaseMessage] = [
        SystemMessage(
            content=(
                "You are a friendly assistant on James's personal portfolio site (jeemzu.me). "
                "Respond to greetings and small talk naturally. Keep responses brief and warm. "
                "If the user seems to want information, suggest they ask about James's skills, "
                "projects, experience, or the games on the site."
            )
        ),
    ]

    if state.get("history"):
        for msg in state["history"][-4:]:
            if msg["role"] == "user":
                messages.append(HumanMessage(content=msg["content"]))
            else:
                messages.append(AIMessage(content=msg["content"]))

    messages.append(HumanMessage(content=state["question"]))
    import asyncio

    response = asyncio.get_event_loop().run_until_complete(llm.ainvoke(messages))
    return {"knowledge_context": response.content}


async def async_chitchat_passthrough(state: AgentState) -> dict:
    """For simple chitchat, generate a direct response without tools."""
    from langchain_openai import ChatOpenAI
    from langchain_core.messages import BaseMessage, SystemMessage, HumanMessage, AIMessage

    from config import OPENAI_API_KEY, OPENAI_MODEL

    llm = ChatOpenAI(model=OPENAI_MODEL, api_key=OPENAI_API_KEY, temperature=0.7)

    messages: list[BaseMessage] = [
        SystemMessage(
            content=(
                "You are Lil' Jay, a friendly and casual assistant on James's portfolio site (jeemzu.me). "
                "You've got a relaxed vibe — like a chill coworker who's happy to chat. "
                "Keep responses brief and warm. Use contractions, short sentences, natural tone. "
                "Still professional — no slang overload, no emojis. "
                "If the user seems to want information, nudge them to ask about James's skills, "
                "projects, experience, or the games on the site."
            )
        ),
    ]

    if state.get("history"):
        for msg in state["history"][-4:]:
            if msg["role"] == "user":
                messages.append(HumanMessage(content=msg["content"]))
            else:
                messages.append(AIMessage(content=msg["content"]))

    messages.append(HumanMessage(content=state["question"]))
    response = await llm.ainvoke(messages)
    return {"knowledge_context": response.content}


def build_graph() -> StateGraph:
    """Constructs the multi-agent orchestration graph."""
    graph = StateGraph(AgentState)

    # Add nodes
    graph.add_node("router", router_node)
    graph.add_node("knowledge", knowledge_node)
    graph.add_node("game_stats", game_stats_node)
    graph.add_node("web_search", web_search_node)
    graph.add_node("chitchat_passthrough", async_chitchat_passthrough)
    graph.add_node("synthesizer", synthesizer_node)

    # Entry point
    graph.set_entry_point("router")

    # Conditional routing from router to agents
    graph.add_conditional_edges(
        "router",
        route_to_agents,
        {
            "knowledge": "knowledge",
            "game_stats": "game_stats",
            "web_search": "web_search",
            "chitchat_passthrough": "chitchat_passthrough",
        },
    )

    # All agents converge at synthesizer
    graph.add_edge("knowledge", "synthesizer")
    graph.add_edge("game_stats", "synthesizer")
    graph.add_edge("web_search", "synthesizer")
    graph.add_edge("chitchat_passthrough", "synthesizer")

    # Synthesizer → END
    graph.add_edge("synthesizer", END)

    return graph


# Compile the graph once at module level
agent_graph = build_graph().compile()
