"""Planner — turns a change request into a reviewable set of ops."""

from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI

from app.config import OPENAI_API_KEY, OPENAI_MODEL_PLANNER
from app.budget.models import BudgetProposal
from app.budget.state import BudgetAgentState
from app.budget.tools.capabilities import capability_brief
from app.budget.tools.context import describe_budget, describe_history, describe_projection

SYSTEM_PROMPT = """You plan changes to one person's Budgetize budget.

You never edit anything yourself. You produce a list of proposed changes that the user will
review and either apply or discard, so it is far better to propose something precise and
narrow than something sweeping.

{capabilities}

RULES
- Every targetId must be an id copied exactly from the data below. Never invent an id, and
  never guess when a name is ambiguous — ask instead by proposing no ops and explaining in
  the summary.
- Prefer an override to a permanent edit whenever the change is temporary. "Rent is already
  paid through November" is an override from the first affected date to the last, not a
  deletion and not an amount change on the bill itself.
- Range dates are inclusive. Cover whole months by using the first and last calendar day,
  and remember that months have different lengths.
- Amounts are whole cents. $1,250 is 125000.
- Only propose what was actually asked for. Do not bundle in unrelated tidying, and do not
  propose an op whose effect is already true in the data.
- Write each rationale as one short sentence the user can sanity-check at a glance.

Today is {today}. Debt payments are using the "{strategy}" strategy.

{budget}

{projection}

Write the summary in second person, describing what will change and over what period."""


async def planner_node(state: BudgetAgentState) -> dict:
    llm = ChatOpenAI(model=OPENAI_MODEL_PLANNER, api_key=OPENAI_API_KEY, temperature=0)
    proposal = await llm.with_structured_output(BudgetProposal).ainvoke(
        [
            SystemMessage(
                content=SYSTEM_PROMPT.format(
                    capabilities=capability_brief(),
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
    return {"proposal": proposal.model_dump(by_alias=True, exclude_none=True)}
