"""Renders the incoming budget and projection into compact prompt text."""


def _money(cents: int | None) -> str:
    if cents is None:
        return "—"
    return f"${cents / 100:,.2f}"


def _recurrence(item: dict) -> str:
    freq = item.get("frequency", "monthly")
    if freq in ("weekly", "biweekly"):
        detail = f"{freq} from {item.get('anchorISO')}"
    elif freq == "monthly":
        detail = f"monthly on day {item.get('dueDay')}"
    else:
        detail = f"{freq} on day {item.get('dueDay')} from {item.get('anchorISO')}"
    window = []
    if item.get("startISO"):
        window.append(f"starts {item['startISO']}")
    if item.get("endISO"):
        window.append(f"ends {item['endISO']}")
    return detail + (f", {', '.join(window)}" if window else "")


_MONTHS = (
    "Jan",
    "Feb",
    "Mar",
    "Apr",
    "May",
    "Jun",
    "Jul",
    "Aug",
    "Sep",
    "Oct",
    "Nov",
    "Dec",
)


def _schedule(entries: list[dict]) -> list[str]:
    lines = []
    for e in sorted(entries, key=lambda x: (x.get("year", 0), x.get("month", 0))):
        month = e.get("month")
        label = _MONTHS[month] if isinstance(month, int) and 0 <= month <= 11 else "?"
        count = e.get("paycheckCount", 0)
        gross = (e.get("perPaycheckCents") or 0) * count
        lines.append(
            f'    {label} {e.get("year")}: {_money(e.get("perPaycheckCents"))} × {count} paychecks '
            f"= {_money(gross)} gross"
        )
    return lines or ["    (no months on file — those months are blank in the projection)"]


def describe_budget(budget: dict) -> str:
    """Every id is included verbatim — ops must target real ids, never invented ones."""
    lines: list[str] = []

    lines.append("PEOPLE (gross pay per month)")
    for p in budget.get("people", []):
        lines.append(
            f'  id={p["id"]} "{p["name"]}" — personal balance {_money(p.get("personalBalanceCents"))}'
        )
        lines.extend(_schedule(p.get("schedule") or []))
    if not budget.get("people"):
        lines.append("  (none)")

    lines.append("")
    lines.append("ACCOUNT BALANCES")
    lines.append(f'  shared essentials: {_money(budget.get("essentialsBalanceCents"))}')
    lines.append(f'  auto-pay: {_money(budget.get("autopayBalanceCents"))}')

    lines.append("")
    lines.append("BILLS")
    for b in budget.get("bills", []):
        lines.append(
            f'  id={b["id"]} "{b["name"]}" — {_money(b.get("amountCents"))}, {_recurrence(b)}, '
            f'paid from {b.get("paidFrom")}'
        )
    if not budget.get("bills"):
        lines.append("  (none)")

    lines.append("")
    lines.append("DEBTS")
    for d in budget.get("debts", []):
        promo = (
            f', promo payment {_money(d.get("suggestedPaymentCents"))}'
            if d.get("hasPromotion")
            else ""
        )
        if d.get("promoEndISO"):
            promo += f' until {d["promoEndISO"]}'
        rate = d.get("interestRateBps")
        rate_text = f", rate {rate / 100:.2f}% (reference only)" if rate is not None else ""
        lines.append(
            f'  id={d["id"]} "{d["name"]}" — balance {_money(d.get("balanceCents"))}, '
            f'minimum {_money(d.get("minPaymentCents"))}{promo}{rate_text}, {_recurrence(d)}, '
            f'paid from {d.get("paidFrom")}'
        )
    if not budget.get("debts"):
        lines.append("  (none)")

    lines.append("")
    lines.append("ACTIVE OVERRIDES")
    for o in budget.get("overrides", []):
        what = "skipped" if o.get("mode") == "skip" else f'set to {_money(o.get("amountCents"))}'
        note = f' — {o["note"]}' if o.get("note") else ""
        lines.append(
            f'  id={o["id"]} {o.get("targetKind")} {o.get("targetId")} {what} '
            f'from {o.get("fromISO")} to {o.get("toISO")}{note}'
        )
    if not budget.get("overrides"):
        lines.append("  (none)")

    lines.append("")
    lines.append("ONE-TIME ENTRIES")
    for e in budget.get("oneOffs", []):
        where = e.get("account")
        if where == "personal":
            where = f'personal ({e.get("personId")})'
        lines.append(
            f'  id={e["id"]} "{e["name"]}" — {e.get("kind")} of {_money(e.get("amountCents"))} '
            f'on {e.get("dateISO")}, {where}'
        )
    if not budget.get("oneOffs"):
        lines.append("  (none)")

    return "\n".join(lines)


def describe_projection(projection: dict) -> str:
    """
    The client's own forecast. Quote these figures rather than recomputing anything —
    the projection engine is authoritative and this text is its output.
    """
    people = projection.get("people", [])
    lines = ["WEEKLY PROJECTION (computed by Budgetize — treat these numbers as fact)"]

    for week in projection.get("weeks", []):
        balances = ", ".join(
            f'{people[i]["name"]} {_money(acct.get("endBalanceCents"))}'
            for i, acct in enumerate(week.get("personal", []))
            if i < len(people)
        )
        lines.append(
            f'  {week.get("startISO")} → {week.get("endISO")}: '
            f'{week.get("paydayCount")} payday(s); '
            f'essentials {_money(week.get("essentials", {}).get("endBalanceCents"))}, '
            f'auto-pay {_money(week.get("autopay", {}).get("endBalanceCents"))}'
            + (f"; {balances}" if balances else "")
        )
        for flow in week.get("outflows", []):
            lines.append(
                f'      out {flow.get("dateISO")} {flow.get("name")} '
                f'{_money(flow.get("amountCents"))} from {flow.get("source")}'
            )
        for flow in week.get("inflows", []):
            lines.append(
                f'      in  {flow.get("dateISO")} {flow.get("name")} '
                f'{_money(flow.get("amountCents"))} to {flow.get("source")}'
            )

    return "\n".join(lines)


def describe_history(history: list[dict], limit: int = 6) -> str:
    if not history:
        return "(no earlier messages)"
    recent = history[-limit:]
    return "\n".join(f'{m.get("role", "user")}: {m.get("content", "")}' for m in recent)
