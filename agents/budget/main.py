"""FastAPI application for the Budgetize assistant service."""

import logging

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

from graph import budget_graph
from models import CamelModel

logger = logging.getLogger(__name__)

app = FastAPI(
    title="Jeemzu Budget Assistant",
    description="Answers budget questions and proposes reviewable schedule changes",
    version="0.1.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost:5173",
        "http://localhost:5050",
        "https://jeemzu.me",
        "https://www.jeemzu.me",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


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


@app.post("/budget/chat", response_model=BudgetChatResponse, response_model_by_alias=True)
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


@app.get("/health")
@app.head("/health")
async def health() -> dict:
    return {"status": "ok"}
