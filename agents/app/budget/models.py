"""
Structured output models for the Budgetize assistant.

Field aliases are camelCase so the JSON the LLM produces matches the client's
TypeScript types exactly — the .NET proxy forwards it untouched and the browser
consumes it without a mapping layer.
"""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field


def _camel(name: str) -> str:
    head, *tail = name.split("_")
    return head + "".join(word.capitalize() for word in tail)


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=_camel, populate_by_name=True)


OpName = Literal[
    "add_override",
    "remove_override",
    "add_one_off",
    "remove_one_off",
    "add_bill",
    "update_bill",
    "remove_bill",
    "update_debt",
    "update_person",
    "set_month_income",
    "set_balance",
]


class BudgetOp(CamelModel):
    """
    One reviewable change. Kept as a single flat model rather than a discriminated
    union because LLMs produce it far more reliably; `validate_op` enforces which
    fields each op actually requires.
    """

    op: OpName = Field(description="Which change to make.")
    rationale: str = Field(description="One sentence explaining why, in plain language.")

    # Targeting. `target_id` is the id of the existing bill/debt/person/override/one-off.
    target_id: str | None = Field(default=None, description="Id of the existing item this op acts on.")
    target_kind: Literal["bill", "debt"] | None = Field(
        default=None, description="For overrides: whether targetId names a bill or a debt."
    )

    # Date range for overrides, or the single date for a one-off.
    from_iso: str | None = Field(default=None, description="Inclusive range start, yyyy-mm-dd.")
    to_iso: str | None = Field(default=None, description="Inclusive range end, yyyy-mm-dd.")
    date_iso: str | None = Field(default=None, description="Date of a one-off, yyyy-mm-dd.")

    mode: Literal["skip", "amount"] | None = Field(
        default=None, description="Override mode: skip the charge entirely, or replace its amount."
    )

    # Payload fields. Only the ones relevant to `op` should be set.
    name: str | None = None
    amount_cents: int | None = Field(default=None, description="Money is always whole cents.")
    due_day: int | None = Field(default=None, description="Calendar day 1-31.")
    paid_from: Literal["shared", "autopay"] | None = None
    frequency: Literal["monthly", "weekly", "biweekly", "quarterly", "annual"] | None = None
    anchor_iso: str | None = Field(default=None, description="First occurrence; required unless monthly.")
    start_iso: str | None = None
    end_iso: str | None = None
    kind: Literal["expense", "income"] | None = Field(default=None, description="For one-off entries.")
    account: Literal["shared", "autopay", "personal"] | None = None
    person_id: str | None = None
    note: str = ""

    # set_balance
    balance_target: Literal["essentials", "autopay", "personal"] | None = None

    # set_month_income
    year: int | None = Field(default=None, description="Four-digit calendar year.")
    month: int | None = Field(default=None, description="0-based month index, 0 = January.")
    paycheck_count: int | None = Field(
        default=None, description="Paydays in the month; omit to use the Wednesday calendar."
    )
    per_paycheck_cents: int | None = Field(
        default=None, description="Gross pay landing on each payday that month, in whole cents."
    )


class BudgetProposal(CamelModel):
    """A set of changes for the user to review, apply, or discard as a unit."""

    summary: str = Field(description="One or two sentences describing the whole change set.")
    ops: list[BudgetOp] = Field(default_factory=list, max_length=25)


class CapabilityGap(CamelModel):
    """Something the user asked for that Budgetize cannot model yet."""

    request: str = Field(description="What the user asked for, restated in one sentence.")
    reason: str = Field(description="Why the current data model can't express it.")
    suggested_feature: str = Field(
        description="Short lowercase feature name so repeat requests group together, "
        "e.g. 'debt interest accrual' or 'variable income forecasting'.",
        max_length=120,
    )


class RouterDecision(CamelModel):
    """How the assistant should handle this turn."""

    intent: Literal["question", "change_request", "unsupported"] = Field(
        description=(
            "'question' to read and explain existing numbers; "
            "'change_request' to propose edits Budgetize supports; "
            "'unsupported' when the ask needs a capability Budgetize does not have."
        )
    )
    reasoning: str = Field(description="Brief justification for the classification.")
