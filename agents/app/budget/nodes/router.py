"""Router — decides whether this turn is a question, a change request, or out of scope."""

from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI

from app.config import OPENAI_API_KEY, OPENAI_MODEL_FAST
from app.budget.models import RouterDecision
from app.budget.state import BudgetAgentState
from app.budget.tools.capabilities import capability_brief
from app.budget.tools.context import describe_history

SYSTEM_PROMPT = """You triage messages for Budgetize, a personal budget planner.

{capabilities}

Classify the user's latest message:

- "question" — they want to understand their existing numbers, or they're making small talk.
  Nothing about the budget needs to change.
- "change_request" — they want the budget altered, AND every part of the ask can be expressed
  with the changes listed above.
- "unsupported" — the ask needs something Budgetize genuinely cannot model. Only choose this
  when the capability is missing, not when you'd simply need several changes to do it.

Be careful not to over-use "unsupported". "My rent is covered for the next two months" is a
change_request — it's an override. "When will my credit card be paid off?" is unsupported,
because Budgetize has no interest or amortization model."""

USER_PROMPT = """Earlier conversation:
{history}

Latest message: {question}"""


async def router_node(state: BudgetAgentState) -> dict:
    llm = ChatOpenAI(model=OPENAI_MODEL_FAST, api_key=OPENAI_API_KEY, temperature=0)
    decision = await llm.with_structured_output(RouterDecision).ainvoke(
        [
            SystemMessage(content=SYSTEM_PROMPT.format(capabilities=capability_brief())),
            HumanMessage(
                content=USER_PROMPT.format(
                    history=describe_history(state.get("history", [])),
                    question=state["question"],
                )
            ),
        ]
    )
    return {"intent": decision.intent}
