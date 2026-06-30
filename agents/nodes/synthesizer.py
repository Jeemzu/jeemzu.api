from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage

from config import OPENAI_API_KEY, OPENAI_MODEL
from state import AgentState


SYNTHESIZER_SYSTEM_PROMPT = """You are Lil' Jay, a friendly assistant on James's portfolio site. Your job is to combine information from multiple sources into a single coherent response.

Personality:
- Casual and approachable — like a chill coworker.
- Concise, natural, no corporate speak.
- Use contractions and a relaxed tone. Still professional.

You may receive:
- Knowledge context: Verified information about James from his personal knowledge base.
- Game data: Live game statistics and leaderboard information.
- Web search results: Information found on the web (not verified by James).

Rules:
- Combine all provided information into a natural, conversational response.
- Do NOT mention the internal agent names or architecture.
- If multiple sources provide information, weave them together naturally.
- If only one source provided information, just present that cleanly.
- Keep the response concise and helpful.
- Do NOT add a web search disclaimer — that will be added separately if needed."""


async def synthesizer_node(state: AgentState) -> dict:
    """Combines outputs from all agents that ran into a final coherent answer."""
    knowledge = state.get("knowledge_context", "")
    game_data = state.get("game_data", "")
    web_results = state.get("web_search_results", "")

    # If only one source has content, use it directly (no LLM call needed)
    sources = [(knowledge, "knowledge"), (game_data, "game_stats"), (web_results, "web_search")]
    active_sources = [(content, label) for content, label in sources if content]

    if len(active_sources) == 1:
        answer = active_sources[0][0]
    elif len(active_sources) == 0:
        answer = (
            "Hmm, I'm having trouble pulling up the details right now. "
            "Could you rephrase or give me a bit more context? "
            "For example, if you're asking about a specific role, "
            "paste the job description and I can map James's experience to it."
        )
    else:
        # Multiple sources — use LLM to synthesize
        llm = ChatOpenAI(model=OPENAI_MODEL, api_key=OPENAI_API_KEY, temperature=0.3)

        source_text = ""
        if knowledge:
            source_text += f"## Knowledge Base (verified info about James):\n{knowledge}\n\n"
        if game_data:
            source_text += f"## Live Game Data:\n{game_data}\n\n"
        if web_results:
            source_text += f"## Web Search Results:\n{web_results}\n\n"

        messages = [
            SystemMessage(content=SYNTHESIZER_SYSTEM_PROMPT),
            HumanMessage(
                content=f"Combine the following into a single response to the user's question: \"{state['question']}\"\n\n{source_text}"
            ),
        ]

        response = await llm.ainvoke(messages)
        answer = response.content

    # Append web search disclaimer if web search was used
    if state.get("used_web_search"):
        answer += "\n\n---\n*ℹ️ This response includes information from the web and may not reflect James's personal views.*"

    return {"answer": answer}
