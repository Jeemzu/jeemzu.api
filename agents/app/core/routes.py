"""HTTP surface for the multi-agent chatbot."""

from fastapi import APIRouter
from pydantic import BaseModel, Field

from app.core.graph import agent_graph

router = APIRouter(tags=["chat"])


class ConversationMessage(BaseModel):
    role: str = Field(description="'user' or 'assistant'")
    content: str


class ChatRequest(BaseModel):
    question: str = Field(min_length=1, max_length=2000)
    history: list[ConversationMessage] = Field(default_factory=list)


class ChatResponse(BaseModel):
    answer: str
    agents_used: list[str] = Field(default_factory=list)
    used_web_search: bool = False


@router.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest) -> ChatResponse:
    """Multi-agent chat endpoint. Routes to specialized agents based on intent."""
    history = [{"role": msg.role, "content": msg.content} for msg in request.history]

    result = await agent_graph.ainvoke(
        {
            "question": request.question,
            "history": history,
            "agents_to_run": [],
            "knowledge_context": "",
            "game_data": "",
            "web_search_results": "",
            "used_web_search": False,
            "answer": "",
        }
    )

    return ChatResponse(
        answer=result.get("answer", "I'm sorry, I couldn't process your question."),
        agents_used=result.get("agents_to_run", []),
        used_web_search=result.get("used_web_search", False),
    )
