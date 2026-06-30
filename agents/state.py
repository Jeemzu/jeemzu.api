from typing import TypedDict, Annotated
from operator import add


class AgentState(TypedDict):
    """Shared state that flows through the LangGraph nodes."""

    # Input
    question: str
    history: list[dict]

    # Router output — which agents to invoke
    agents_to_run: list[str]

    # Agent outputs
    knowledge_context: str
    game_data: str
    web_search_results: str

    # Metadata
    used_web_search: bool

    # Final output
    answer: str
