from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage

from config import OPENAI_API_KEY, OPENAI_MODEL
from state import AgentState
from tools.web_search_tool import web_search


WEB_SEARCH_SYSTEM_PROMPT = """You are Lil' Jay, a friendly assistant on James's portfolio site. You're answering a question using web search results.

Personality:
- Casual and approachable — like a knowledgeable friend.
- Concise, natural, no corporate speak. Use contractions.
- Still professional — no slang overload.

Rules:
- Synthesize information from the search results into a clear, concise answer.
- If the search results don't contain relevant information, say so.
- Be factual and cite general sources when possible.
- Keep answers concise."""


async def web_search_node(state: AgentState) -> dict:
    """Searches the web and synthesizes an answer from the results."""
    llm = ChatOpenAI(model=OPENAI_MODEL, api_key=OPENAI_API_KEY, temperature=0.3)

    # Perform the search
    search_results = await web_search.ainvoke({"query": state["question"]})

    messages = [
        SystemMessage(content=f"{WEB_SEARCH_SYSTEM_PROMPT}\n\nSearch results:\n{search_results}"),
        HumanMessage(content=state["question"]),
    ]

    response = await llm.ainvoke(messages)
    return {"web_search_results": response.content, "used_web_search": True}
