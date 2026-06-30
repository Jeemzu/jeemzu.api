from langchain_community.tools.tavily_search import TavilySearchResults
from langchain_core.tools import tool

from config import TAVILY_API_KEY


@tool
async def web_search(query: str) -> str:
    """Search the web for information not available in the knowledge base.

    Use this for general technology questions, current events, or anything
    that wouldn't be in James's personal knowledge base.

    Args:
        query: The search query.
    """
    if not TAVILY_API_KEY:
        return "Web search is not configured (no API key)."

    search = TavilySearchResults(
        max_results=3,
        api_key=TAVILY_API_KEY,
    )
    results = await search.ainvoke({"query": query})

    if not results:
        return "No web results found."

    parts = []
    for r in results:
        content = r.get("content", "")
        url = r.get("url", "")
        parts.append(f"Source: {url}\n{content}")

    return "\n\n---\n\n".join(parts)
