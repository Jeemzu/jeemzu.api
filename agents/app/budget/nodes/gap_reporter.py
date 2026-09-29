"""Gap reporter — records an ask Budgetize cannot model yet."""

from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI

from app.config import OPENAI_API_KEY, OPENAI_MODEL_FAST
from app.budget.models import CapabilityGap
from app.budget.state import BudgetAgentState
from app.budget.tools.capabilities import capability_brief
from app.budget.tools.context import describe_history

SYSTEM_PROMPT = """The user has asked Budgetize for something it cannot do yet.

{capabilities}

Describe the gap for the developer's backlog. The suggestedFeature must be a short lowercase
noun phrase naming the missing capability itself — not a restatement of this user's specific
situation — so that everyone who asks for the same thing lands under the same name.

Good: "debt interest and payoff projection", "variable income forecasting", "savings goals".
Bad: "let James see when his Best Buy card is paid off"."""


async def gap_reporter_node(state: BudgetAgentState) -> dict:
    llm = ChatOpenAI(model=OPENAI_MODEL_FAST, api_key=OPENAI_API_KEY, temperature=0)
    gap = await llm.with_structured_output(CapabilityGap).ainvoke(
        [
            SystemMessage(content=SYSTEM_PROMPT.format(capabilities=capability_brief())),
            HumanMessage(
                content=(
                    f"Earlier conversation:\n{describe_history(state.get('history', []))}\n\n"
                    f"Latest message: {state['question']}"
                )
            ),
        ]
    )
    return {"capability_gap": gap.model_dump(by_alias=True)}
