"""HTTP surface for the Budgetize assistant."""

import logging

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

from app.budget.graph import budget_graph
from app.budget.models import CamelModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/budget", tags=["budget"])


class ConversationMessage(BaseModel):
    role: str = Field(description="user or assistant")
    content: str


class BudgetChatRequest(BaseModel):
    question: str = Field(min_length=1, max_length=2000)
    history: list[ConversationMessage] = Field(default_factory=list, max_length=40)
    # The client's own budget and forecast, so unsaved edits are visible and the
    # projection engine stays in one place.
    budget: dict
    projection: dict
    today: str = Field(description="Client's local date as yyyy-mm-dd")
    strategy: str = Field(default="suggested")


class BudgetChatResponse(CamelModel):
    answer: str
    intent: str
    proposal: dict | None = None
    capability_gap: dict | None = None


@router.post("/chat", response_model=BudgetChatResponse, response_model_by_alias=True)
async def budget_chat(request: BudgetChatRequest) -> BudgetChatResponse:
    try:
        result = await budget_graph.ainvoke(
            {
                "question": request.question,
                "history": [m.model_dump() for m in request.history],
                "budget": request.budget,
                "projection": request.projection,
                "today": request.today,
                "strategy": request.strategy,
                "intent": "",
                "proposal": None,
                "capability_gap": None,
                "answer": "",
            }
        )
    except Exception:
        logger.exception("Budget assistant graph failed")
        raise HTTPException(status_code=502, detail="The budget assistant is unavailable right now.")

    return BudgetChatResponse(
        answer=result.get("answer", ""),
        intent=result.get("intent", ""),
        proposal=result.get("proposal"),
        capability_gap=result.get("capability_gap"),
    )
