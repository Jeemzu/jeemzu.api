"""Offline checks for graph wiring and the composer's op validation — no API key needed."""

from graph import budget_graph
from nodes.composer import composer_node

BUDGET = {
    "people": [{"id": "p1", "name": "James"}],
    "bills": [{"id": "rent", "name": "Rent"}],
    "debts": [{"id": "card", "name": "Card"}],
    "overrides": [],
    "oneOffs": [],
}


def base(**extra):
    return {
        "question": "",
        "history": [],
        "budget": BUDGET,
        "projection": {},
        "today": "2026-10-01",
        "strategy": "suggested",
        "intent": "",
        "proposal": None,
        "capability_gap": None,
        "answer": "",
        **extra,
    }


def check(label, condition):
    print(f'{"PASS" if condition else "FAIL"}  {label}')
    assert condition, label


check("graph compiles with all nodes", set(budget_graph.get_graph().nodes) >= {
    "router", "analyst", "planner", "gap_reporter", "composer"
})

good = {
    "op": "add_override", "rationale": "Rent is already covered.", "targetKind": "bill",
    "targetId": "rent", "fromISO": "2026-10-01", "toISO": "2026-11-30", "mode": "skip",
}
out = composer_node(base(proposal={"summary": "Pausing rent.", "ops": [good]}))
check("valid override survives validation", len(out["proposal"]["ops"]) == 1)

bogus = {**good, "targetId": "mortgage"}
out = composer_node(base(proposal={"summary": "Pausing rent.", "ops": [bogus]}))
check("hallucinated bill id is rejected", out["proposal"] is None)
check("rejection is explained to the user", "mortgage" in out["answer"])

incomplete = {"op": "add_override", "rationale": "x", "targetKind": "bill", "targetId": "rent",
              "fromISO": "2026-10-01", "toISO": "2026-11-30", "mode": "amount"}
out = composer_node(base(proposal={"summary": "s", "ops": [incomplete]}))
check("amount override without an amount is rejected", out["proposal"] is None)

backwards = {**good, "fromISO": "2026-11-30", "toISO": "2026-10-01"}
out = composer_node(base(proposal={"summary": "s", "ops": [backwards]}))
check("inverted date range is rejected", out["proposal"] is None)

stray = {"op": "add_one_off", "rationale": "x", "kind": "expense", "name": "Tires",
         "amountCents": 30000, "dateISO": "2026-10-05", "account": "personal", "personId": "nobody"}
out = composer_node(base(proposal={"summary": "s", "ops": [stray]}))
check("one-off naming an unknown person is rejected", out["proposal"] is None)

out = composer_node(base(capability_gap={"request": "r", "reason": "No interest model exists.",
                                         "suggestedFeature": "debt interest projection"}))
check("capability gap becomes a user-facing answer", "logged" in out["answer"])

out = composer_node(base(answer="Your essentials account is fine."))
check("analyst answers pass through untouched", out == {})

print("\nAll offline checks passed.")
