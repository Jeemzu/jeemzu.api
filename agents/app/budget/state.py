"""Shared state for the Budgetize assistant graph."""

from typing import TypedDict


class BudgetAgentState(TypedDict):
    """
    The budget and projection arrive with every request rather than being fetched,
    so the assistant sees unsaved edits and there is only ever one projection
    engine — the client's.
    """

    question: str
    history: list[dict]
    budget: dict
    projection: dict
    today: str
    strategy: str

    # Router output: "question" | "change_request" | "unsupported"
    intent: str

    proposal: dict | None
    capability_gap: dict | None
    answer: str
