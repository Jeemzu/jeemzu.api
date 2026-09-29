"""
Composer — validates the proposal against the real budget and writes the final reply.

No LLM here. An op that names an id which doesn't exist would fail silently or, worse,
apply to the wrong item, so every op is checked against the data the request carried
before the user is ever shown it.
"""

from state import BudgetAgentState

# Ops that act on an existing item, and which collection that id must live in.
_TARGET_COLLECTION = {
    "remove_override": "overrides",
    "remove_one_off": "oneOffs",
    "update_bill": "bills",
    "remove_bill": "bills",
    "update_debt": "debts",
    "update_person": "people",
    "set_month_income": "people",
}

_REQUIRED_FIELDS = {
    "add_override": ("targetKind", "targetId", "fromISO", "toISO", "mode"),
    "add_one_off": ("kind", "name", "amountCents", "dateISO", "account"),
    "add_bill": ("name", "amountCents", "dueDay", "paidFrom"),
    "set_balance": ("balanceTarget", "amountCents"),
    "set_month_income": ("targetId", "year", "perPaycheckCents"),
}


def _ids(budget: dict, collection: str) -> set[str]:
    return {item["id"] for item in budget.get(collection, []) if "id" in item}


def _rejection(op: dict, reason: str) -> str:
    return f'Dropped "{op.get("op")}" — {reason}.'


def _validate(op: dict, budget: dict) -> str | None:
    """Returns a rejection reason, or None when the op is usable."""
    name = op.get("op")

    for field in _REQUIRED_FIELDS.get(name, ()):
        if op.get(field) in (None, ""):
            return f"it is missing {field}"

    if name == "add_override":
        collection = "bills" if op.get("targetKind") == "bill" else "debts"
        if op.get("targetId") not in _ids(budget, collection):
            return f'no {op.get("targetKind")} has id {op.get("targetId")}'
        if op.get("mode") == "amount" and op.get("amountCents") is None:
            return "an amount override needs amountCents"
        if op.get("toISO", "") < op.get("fromISO", ""):
            return "its date range ends before it starts"

    if name == "add_one_off" and op.get("account") == "personal":
        if op.get("personId") not in _ids(budget, "people"):
            return "it names a person who does not exist"

    if name == "add_bill":
        if op.get("frequency", "monthly") != "monthly" and not op.get("anchorISO"):
            return f'a {op.get("frequency")} bill needs a first-occurrence date'

    if name == "set_balance":
        if op.get("balanceTarget") == "personal" and op.get("targetId") not in _ids(budget, "people"):
            return "it names a person who does not exist"

    if name == "set_month_income":
        # month 0 is January, so check presence separately from the range.
        month = op.get("month")
        if month is None:
            return "it is missing month"
        if not isinstance(month, int) or not 0 <= month <= 11:
            return "month must be 0-11, where 0 is January"
        year = op.get("year")
        if not isinstance(year, int) or not 1900 <= year <= 2999:
            return "year must be a four-digit calendar year"
        count = op.get("paycheckCount")
        if count is not None and (not isinstance(count, int) or not 1 <= count <= 6):
            return "paycheckCount must be between 1 and 6"
        cents = op.get("perPaycheckCents")
        if not isinstance(cents, int) or cents < 0:
            return "perPaycheckCents must be a whole number of cents"

    collection = _TARGET_COLLECTION.get(name)
    if collection and op.get("targetId") not in _ids(budget, collection):
        return f'nothing in {collection} has id {op.get("targetId")}'

    return None


def composer_node(state: BudgetAgentState) -> dict:
    proposal = state.get("proposal")
    gap = state.get("capability_gap")

    if gap:
        answer = (
            f"{gap['reason']}\n\n"
            "I've logged this as a feature request so it can be built into Budgetize later."
        )
        return {"answer": answer}

    if not proposal:
        # The analyst already wrote the reply.
        return {}

    budget = state.get("budget", {})
    kept: list[dict] = []
    rejected: list[str] = []
    for op in proposal.get("ops", []):
        reason = _validate(op, budget)
        if reason:
            rejected.append(_rejection(op, reason))
        else:
            kept.append(op)

    proposal = {**proposal, "ops": kept}

    if not kept:
        answer = proposal.get("summary", "") or "I couldn't turn that into a change I can make."
        if rejected:
            answer = f"{answer}\n\n" + "\n".join(rejected)
        return {"answer": answer.strip(), "proposal": None}

    plural = "change" if len(kept) == 1 else "changes"
    answer = f'{proposal.get("summary", "").strip()}\n\nReview the {len(kept)} proposed {plural} below.'
    if rejected:
        answer += "\n\n" + "\n".join(rejected)

    return {"answer": answer.strip(), "proposal": proposal}
