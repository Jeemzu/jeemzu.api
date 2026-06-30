from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage
from pydantic import BaseModel, Field

from config import OPENAI_API_KEY, OPENAI_MODEL
from state import AgentState


class RouterDecision(BaseModel):
    """The router's classification of which agents should handle the query."""

    agents: list[str] = Field(
        description="List of agents to invoke. Options: 'knowledge', 'game_stats', 'web_search', 'chitchat'"
    )
    reasoning: str = Field(
        description="Brief explanation of why these agents were selected"
    )


ROUTER_SYSTEM_PROMPT = """You are a router that classifies user questions and decides which specialized agents should handle them.

Available agents:
- "knowledge": Handles questions about James (his skills, experience, projects, education, personal info, strengths, qualifications, fit for roles). Also handles questions about what games or content are available on the site. Uses a vector database of James's personal information.
- "game_stats": Handles questions about game leaderboards, scores, user profiles, and game statistics. Available games with leaderboards: snake, tetris, pong, brickbreak, zaim.
- "web_search": Handles questions about general topics, current events, technology concepts, companies, or anything NOT specifically about James's personal info or game scores on this site.
- "chitchat": Handles greetings, small talk, or simple conversational responses that don't need any data retrieval.

Rules:
- You may select MULTIPLE agents if the question spans multiple domains.
- "Is James qualified for X role at Y company?" → ["knowledge", "web_search"] (knowledge for James's skills, web_search for info about the role/company)
- "Tell me about James's experience and who has the high score in snake" → ["knowledge", "game_stats"]
- "How does pgvector compare to Pinecone?" → ["web_search"]
- "What technologies does James use?" → ["knowledge"]
- "What games can I play on this site?" → ["knowledge"] (site content is in the knowledge base)
- "Hi there!" → ["chitchat"]
- "What's James's experience with React and what's new in React 19?" → ["knowledge", "web_search"]
- "Would James be a good fit for a consulting role?" → ["knowledge"] (his strengths and work style are in the knowledge base)
- "Who has the high score in tetris?" → ["game_stats"] (leaderboard/score queries)
- Always select at least one agent.
- When a question asks about what's available on the site, what games exist, or site content: use "knowledge".
- When a question asks about specific scores, leaderboards, or player stats: use "game_stats".
- When a question asks about James's qualifications, fit, or suitability for a role: ALWAYS include "knowledge". Add "web_search" if the question mentions a specific company or role you'd need external context on.
- When in doubt between knowledge and web_search, prefer knowledge first — it contains James's verified information."""


async def router_node(state: AgentState) -> dict:
    """Classifies the user's question and decides which agents to invoke."""
    llm = ChatOpenAI(model=OPENAI_MODEL, api_key=OPENAI_API_KEY, temperature=0)
    structured_llm = llm.with_structured_output(RouterDecision)

    # Include recent history for context
    history_context = ""
    if state.get("history"):
        recent = state["history"][-4:]  # Last 2 exchanges
        history_context = "\n\nRecent conversation:\n"
        for msg in recent:
            history_context += f"{msg['role']}: {msg['content']}\n"

    response = await structured_llm.ainvoke([
        SystemMessage(content=ROUTER_SYSTEM_PROMPT),
        HumanMessage(content=f"Classify this question:{history_context}\n\nCurrent question: {state['question']}"),
    ])

    return {"agents_to_run": response.agents}
