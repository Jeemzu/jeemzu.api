import httpx
from langchain_core.tools import tool

from config import DOTNET_API_URL, INTERNAL_API_KEY


@tool
async def search_knowledge(query: str, top_k: int = 5) -> str:
    """Search the personal knowledge base about James using semantic similarity.

    Use this to find information about James's skills, experience, projects,
    education, or personal details. Returns relevant knowledge chunks.

    Args:
        query: The search query to find relevant knowledge about James.
        top_k: Number of results to return (1-20). Default 5.
    """
    headers = {"X-Internal-Key": INTERNAL_API_KEY} if INTERNAL_API_KEY else {}
    try:
        async with httpx.AsyncClient(timeout=30.0) as client:
            response = await client.get(
                f"{DOTNET_API_URL}/knowledge/search",
                params={"query": query, "topK": top_k},
                headers=headers,
            )
            response.raise_for_status()
            data = response.json()

        results = data.get("results", [])
        if not results:
            return "No relevant knowledge found."

        chunks = []
        for r in results:
            chunks.append(f"[{r['sourceKey']}]: {r['content']}")
        return "\n\n".join(chunks)
    except httpx.HTTPError as e:
        return f"Knowledge search unavailable: {e}"
