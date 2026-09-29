import httpx
from langchain_core.tools import tool

from config import DOTNET_API_URL, INTERNAL_API_KEY


@tool
async def get_leaderboard(game_id: str, limit: int = 10) -> str:
    """Get the top scores leaderboard for a specific game.

    Available games: snake, tetris, pong, brickbreak, zaim.

    Args:
        game_id: The game identifier (e.g., "snake", "tetris", "pong").
        limit: Number of top scores to return (1-100). Default 10.
    """
    headers = {"X-Internal-Key": INTERNAL_API_KEY} if INTERNAL_API_KEY else {}
    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            response = await client.get(
                f"{DOTNET_API_URL}/scores/{game_id}",
                params={"limit": limit},
                headers=headers,
            )
            response.raise_for_status()
            scores = response.json()

        if not scores:
            return f"No scores found for game '{game_id}'."

        lines = [f"Leaderboard for {game_id} (top {len(scores)}):"]
        for i, s in enumerate(scores, 1):
            lines.append(f"  {i}. {s['username']} — {s['score']:,}")
        return "\n".join(lines)
    except httpx.HTTPError as e:
        return f"Error fetching leaderboard: {e}"


@tool
async def get_game_summary(game_id: str) -> str:
    """Get a summary of a game including the all-time record.

    Available games: snake, tetris, pong, brickbreak, zaim.

    Args:
        game_id: The game identifier (e.g., "snake", "tetris").
    """
    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            response = await client.get(
                f"{DOTNET_API_URL}/scores/{game_id}/summary",
            )
            response.raise_for_status()
            data = response.json()

        parts = []
        record = data.get("allTimeRecord")
        if record:
            parts.append(
                f"All-time record for {game_id}: {record['score']:,} "
                f"by {record['username']}"
            )
        else:
            parts.append(f"No scores recorded yet for {game_id}.")

        return "\n".join(parts)
    except httpx.HTTPError as e:
        return f"Error fetching game summary: {e}"
