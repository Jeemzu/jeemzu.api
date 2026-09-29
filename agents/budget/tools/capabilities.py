"""
The one description of what Budgetize can and cannot model.

Both the planner (to constrain what it proposes) and the router/gap reporter (to
decide something is unsupported) read from here, so the two can never drift into
disagreeing about what the tool is capable of.
"""

SUPPORTED_OPS = """\
add_override    — pause or re-price one bill/debt over an inclusive date range, leaving the item itself intact.
                  Needs: targetKind, targetId, fromISO, toISO, mode ("skip" or "amount"), amountCents when mode is "amount".
                  This is the right tool for "my rent is already covered through November".
remove_override — drop an existing override. Needs: targetId (the override's own id).
add_one_off     — a single dated expense or deposit that does not repeat.
                  Needs: kind ("expense" or "income"), name, amountCents, dateISO, account
                  ("shared", "autopay", or "personal"), and personId when account is "personal".
remove_one_off  — drop a one-off. Needs: targetId.
add_bill        — a new recurring bill. Needs: name, amountCents, dueDay, paidFrom, frequency,
                  plus anchorISO when frequency is not "monthly". Optional: category, startISO, endISO.
update_bill     — change an existing bill permanently. Needs: targetId plus the fields to change.
remove_bill     — delete a bill outright. Needs: targetId.
update_debt     — change an existing debt permanently. Needs: targetId plus fields
                  (amountCents maps to minPaymentCents, dueDay, paidFrom, frequency, startISO, endISO).
update_person   — change someone's paycheck split. Needs: targetId plus
                  personalPerPaycheckCents and/or essentialsPerPaycheckCents.
set_balance     — correct a current account balance. Needs: balanceTarget
                  ("essentials", "autopay", or "personal"), amountCents, and targetId when "personal".
"""

DATA_MODEL = """\
- Money is always whole cents (integers). $1,250.00 is 125000.
- Paychecks land every Wednesday. Each person deposits a fixed amount into their own
  personal account and a fixed amount into the shared essentials account.
- A slice of everyone's essentials deposit is automatically carved out each payday to
  fund the auto-pay account, sized from the bills and debts marked paidFrom "autopay".
- Bills and debts draft from whichever account their paidFrom names.
- Recurrence: monthly, weekly, biweekly, quarterly, or annual. Monthly/quarterly/annual
  charge on dueDay (days 29-31 clamp to the last day of shorter months); weekly and
  biweekly stride from anchorISO and ignore dueDay.
- startISO/endISO bound when an item exists at all — use these for a permanent change
  like a lease ending. Use an override for a temporary one, so the item resumes by itself.
- Debts are paid at either their promo "suggested" payment or their minimum, depending on
  the strategy the user has selected. You cannot change the strategy.
"""

NOT_SUPPORTED = """\
- Interest accrual, amortization schedules, or payoff-date math on debts. Balances do
  not compound; a debt's balance is a number the user types in.
- Variable or hourly income, bonuses tied to a formula, or any pay cadence other than the
  fixed Wednesday paycheck. (A known one-time bonus IS supported — use add_one_off.)
- Savings goals, sinking funds, investment or retirement accounts, and net worth.
- Taxes, withholding, or paycheck gross-to-net calculation.
- Categories or budget caps with enforced spending limits, and actual-vs-budget tracking
  of real transactions. Budgetize plans; it does not record what was actually spent.
- Bank or card account linking, transaction import, and balance syncing.
- Any account beyond the three that exist: each person's personal checking, the shared
  essentials account, and the auto-pay account.
- Scenario comparison, multi-user or shared budgets, and reminders or notifications.
"""


def capability_brief() -> str:
    """The full catalog, for prompts that need to plan or judge feasibility."""
    return (
        f"HOW BUDGETIZE MODELS MONEY\n{DATA_MODEL}\n"
        f"CHANGES YOU CAN PROPOSE\n{SUPPORTED_OPS}\n"
        f"WHAT BUDGETIZE CANNOT DO YET\n{NOT_SUPPORTED}"
    )
