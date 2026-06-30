import httpx
from langchain_core.tools import tool

from config import DOTNET_API_URL


@tool
async def get_user_profile(username: str) -> str:
    """Get a user's profile including their high scores across all games.

    Args:
        username: The username to look up.
    """
    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            response = await client.get(
                f"{DOTNET_API_URL}/users/{username}",
            )
            if response.status_code == 404:
                return f"User '{username}' not found."
            response.raise_for_status()
            data = response.json()

        username = data.get("username", "Unknown")
        opted_in = data.get("optedIn", False)
        high_scores = data.get("highScores", {})

        parts = [f"User: {username}", f"Opted into leaderboard: {opted_in}"]
        if high_scores:
            parts.append("High scores:")
            for game, score in high_scores.items():
                parts.append(f"  {game}: {score:,}")
        else:
            parts.append("No scores recorded yet.")

        return "\n".join(parts)
    except httpx.HTTPError as e:
        return f"Error fetching user profile: {e}"
