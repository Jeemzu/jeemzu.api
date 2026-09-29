"""Analyst — answers questions about the budget using only the numbers it was given."""

from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI

from config import OPENAI_API_KEY, OPENAI_MODEL_FAST
from state import BudgetAgentState
from tools.context import describe_budget, describe_history, describe_projection

SYSTEM_PROMPT = """You are the Budgetize assistant, helping one person understand their own budget.

Ground every figure in the data below. The weekly projection was computed by Budgetize itself,
so quote it rather than doing your own forecasting. You may add or subtract the numbers you are
given, but never invent one, and never guess at a value that isn't shown.

If the data doesn't contain what's needed to answer, say so plainly and name what's missing.

Today is {today}. Debt payments are currently using the "{strategy}" strategy.

{budget}

{projection}

Be concise and concrete — two or three short paragraphs at most. Write amounts as dollars
(e.g. $1,250.00). Speak to the user directly as "you"."""


async def analyst_node(state: BudgetAgentState) -> dict:
    llm = ChatOpenAI(model=OPENAI_MODEL_FAST, api_key=OPENAI_API_KEY, temperature=0.2)
    response = await llm.ainvoke(
        [
            SystemMessage(
                content=SYSTEM_PROMPT.format(
                    today=state.get("today", ""),
                    strategy=state.get("strategy", "suggested"),
                    budget=describe_budget(state.get("budget", {})),
                    projection=describe_projection(state.get("projection", {})),
                )
            ),
            HumanMessage(
                content=(
                    f"Earlier conversation:\n{describe_history(state.get('history', []))}\n\n"
                    f"Latest message: {state['question']}"
                )
            ),
        ]
    )
    return {"answer": str(response.content).strip()}
