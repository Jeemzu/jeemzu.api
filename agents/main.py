"""FastAPI entrypoint — serves the chatbot and the Budgetize assistant from one app."""

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.budget.routes import router as budget_router
from app.core.routes import router as core_router

app = FastAPI(
    title="Jeemzu Agents",
    description="Multi-agent orchestration for the jeemzu.me chatbot and the Budgetize assistant",
    version="0.2.0",
)

# Kept for local development; in production this service is private-network only.
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

app.include_router(core_router)
app.include_router(budget_router)


@app.head("/")
@app.get("/")
@app.head("/health")
@app.get("/health")
async def health() -> dict:
    return {"status": "ok", "service": "jeemzu-agents"}
