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
                  plus anchorISO when frequency is not "monthly". Optional: startISO, endISO.
update_bill     — change an existing bill permanently. Needs: targetId plus the fields to change.
remove_bill     — delete a bill outright. Needs: targetId.
update_debt     — change an existing debt permanently. Needs: targetId plus fields
                  (amountCents maps to minPaymentCents, dueDay, paidFrom, frequency, startISO, endISO).
update_person   — rename someone. Needs: targetId plus name.
set_month_income — set one person's gross pay for one calendar month. Needs: targetId, year,
                  month (0-based, 0 = January), perPaycheckCents. Optional: paycheckCount, which
                  otherwise comes from the Wednesday calendar. One op per person per month.
set_balance     — correct a current account balance. Needs: balanceTarget
                  ("essentials", "autopay", or "personal"), amountCents, and targetId when "personal".
"""

DATA_MODEL = """\
- Money is always whole cents (integers). $1,250.00 is 125000.
- Paychecks land every Wednesday. Each person has a per-month pay schedule: a gross
  amount per paycheck and how many paychecks that month has (4 or 5). Months missing
  from a person's schedule have NO known income and are left blank in the projection —
  never assume a number for them, and say so when a question touches one.
- Each paycheck is split automatically, not by the user: the month's auto-pay need and
  shared essentials need are sized from the bills and debts themselves, divided between
  people in proportion to their gross pay, and whatever is left over is personal money.
  You cannot set the split directly; change the bills, the debts, or the gross pay.
- Bills and debts draft from whichever account their paidFrom names.
- Recurrence: monthly, weekly, biweekly, quarterly, or annual. Monthly/quarterly/annual
  charge on dueDay (days 29-31 clamp to the last day of shorter months); weekly and
  biweekly stride from anchorISO and ignore dueDay.
- startISO/endISO bound when an item exists at all — use these for a permanent change
  like a lease ending. Use an override for a temporary one, so the item resumes by itself.
- Debts are paid at either their promo "suggested" payment or their minimum, depending on
  the strategy the user has selected. You cannot change the strategy.
- A debt's interest rate, promotion end date, and post-promotion rate are stored for
  reference only. Nothing is computed from them, and the promotion does not expire on its own.
"""

NOT_SUPPORTED = """\
- Interest accrual, amortization schedules, or payoff-date math on debts. Balances do
  not compound; a debt's balance is a number the user types in.
- Hourly income, pay tied to a formula, or any cadence other than the Wednesday paycheck.
  Pay that changes month to month IS supported — use set_month_income. A known one-time
  bonus IS supported — use add_one_off.
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
