from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

from graph import agent_graph


app = FastAPI(
    title="Jeemzu Agents",
    description="Multi-agent orchestration layer for jeemzu.me chatbot",
    version="0.1.0",
)

# CORS — allow frontend and .NET API
app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost:5173",   # Vite dev server
        "http://localhost:5000",   # .NET API
        "https://jeemzu.me",
        "https://www.jeemzu.me",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


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


@app.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest):
    """Multi-agent chat endpoint. Routes to specialized agents based on intent."""
    # Convert history to dicts for the graph state
    history = [{"role": msg.role, "content": msg.content} for msg in request.history]

    # Invoke the compiled LangGraph
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


@app.get("/")
@app.get("/health")
async def health():
    return {"status": "ok", "service": "jeemzu-agents"}
