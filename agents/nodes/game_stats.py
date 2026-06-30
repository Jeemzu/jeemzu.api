from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage

from config import OPENAI_API_KEY, OPENAI_MODEL
from state import AgentState
from tools.scores_tool import get_leaderboard, get_game_summary
from tools.users_tool import get_user_profile


GAME_STATS_SYSTEM_PROMPT = """You are Lil' Jay, a friendly assistant on James's portfolio site. You answer questions about game scores, leaderboards, and player stats.

Personality:
- Casual and approachable — like a chill coworker.
- Concise, natural, no corporate speak. Use contractions.
- Get hyped about high scores and competition — but keep it brief.
- Still professional — no slang overload.

You have access to tools to look up live game data. Use them to answer the user's question.

Available games: snake, tetris, pong, brickbreak, zaim.

Rules:
- Always use the tools to get current data — never guess at scores or rankings.
- Format scores and leaderboards in a readable way.
- If asked about a game that doesn't exist, let the user know which games are available."""


async def game_stats_node(state: AgentState) -> dict:
    """Uses tools to query live game data and answer questions about scores/leaderboards."""
    tools = [get_leaderboard, get_game_summary, get_user_profile]
    llm = ChatOpenAI(model=OPENAI_MODEL, api_key=OPENAI_API_KEY, temperature=0)
    llm_with_tools = llm.bind_tools(tools)

    messages = [
        SystemMessage(content=GAME_STATS_SYSTEM_PROMPT),
        HumanMessage(content=state["question"]),
    ]

    # Agentic tool-calling loop
    max_iterations = 5
    for _ in range(max_iterations):
        response = await llm_with_tools.ainvoke(messages)
        messages.append(response)

        # If no tool calls, we have the final answer
        if not response.tool_calls:
            break

        # Execute tool calls
        for tool_call in response.tool_calls:
            tool_name = tool_call["name"]
            tool_args = tool_call["args"]

            # Find and execute the matching tool
            tool_map = {t.name: t for t in tools}
            if tool_name in tool_map:
                result = await tool_map[tool_name].ainvoke(tool_args)
                from langchain_core.messages import ToolMessage

                messages.append(
                    ToolMessage(content=result, tool_call_id=tool_call["id"])
                )

    return {"game_data": response.content}
