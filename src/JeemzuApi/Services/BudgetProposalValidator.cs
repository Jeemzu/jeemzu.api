using System.Text.Json;
using JeemzuApi.DTOs;

namespace JeemzuApi.Services;

public static class BudgetProposalValidator
{
    private static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>
    {
        ["add_override"] = ["targetKind", "targetId", "fromISO", "toISO", "mode", "amountCents", "note"],
        ["remove_override"] = ["targetId"],
        ["add_one_off"] = ["kind", "name", "amountCents", "dateISO", "account", "personId", "note"],
        ["remove_one_off"] = ["targetId"],
        ["add_bill"] = ["name", "amountCents", "dueDay", "paidFrom", "frequency", "anchorISO", "startISO", "endISO"],
        ["update_bill"] = ["targetId", "name", "amountCents", "dueDay", "paidFrom", "frequency", "anchorISO", "startISO", "endISO"],
        ["remove_bill"] = ["targetId"],
        ["update_debt"] = ["targetId", "name", "amountCents", "dueDay", "paidFrom", "frequency", "anchorISO", "startISO", "endISO"],
        ["update_person"] = ["targetId", "name"],
        ["set_month_income"] = ["targetId", "year", "month", "perPaycheckCents", "paycheckCount"],
        ["set_balance"] = ["balanceTarget", "amountCents", "targetId"],
    };

    internal static bool Text(string? text, int limit) => !string.IsNullOrWhiteSpace(text) && text.Length <= limit;

    public static BudgetChatResult Compose(BudgetProposal proposal, BudgetDataDto budget)
    {
        if (!Text(proposal.Summary, 4000) || proposal.Ops is null || proposal.Ops.Length > 25)
            throw new InvalidDataException("The budget model returned an invalid proposal.");
        var kept = new List<BudgetOp>();
        var rejected = new List<string>();
        var working = JsonSerializer.Deserialize<BudgetDataDto>(
            JsonSerializer.Serialize(budget, BudgetAssistantContext.JsonOptions), BudgetAssistantContext.JsonOptions)!;
        foreach (var op in proposal.Ops)
        {
            var reason = op is null ? "the operation was null" : Validate(op, working);
            if (reason is not null)
                rejected.Add($"Dropped \"{(op?.Op is not null && Fields.ContainsKey(op.Op) ? op.Op : "unknown")}\": {reason}.");
            else
            {
                kept.Add(op!);
                ApplyValidationState(op!, working);
            }
        }
        var answer = proposal.Summary.Trim();
        if (kept.Count > 0) answer += $"\n\nReview the {kept.Count} proposed {(kept.Count == 1 ? "change" : "changes")} below.";
        if (rejected.Count > 0) answer += "\n\n" + string.Join("\n", rejected);
        return new(answer, "change_request", kept.Count == 0 ? null : proposal with { Ops = kept.ToArray() }, null);
    }

    public static string? Validate(BudgetOp op, BudgetDataDto budget)
    {
        if (op.Op is null || !Fields.TryGetValue(op.Op, out var fields)) return "the operation is not supported";
        if (!Text(op.Rationale, 1000)) return "a short rationale is required";
        var properties = JsonSerializer.SerializeToElement(op, BudgetAssistantContext.JsonOptions).EnumerateObject()
            .Where(property => property.Name is not ("op" or "rationale")).ToArray();
        if (properties.Any(property => !fields.Contains(property.Name))) return "it contains fields not supported by this operation";
        if (op.Name is not null && !Text(op.Name, 200)) return "name must contain 1-200 characters";
        if (op.Note?.Length > 500) return "note exceeds 500 characters";
        if (op.AmountCents < 0 && op.Op != "set_balance") return "payments must be nonnegative whole cents";
        if (op.DueDay is not null && op.DueDay is < 1 or > 31) return "dueDay must be 1-31";
        if (op.PaidFrom is not null && op.PaidFrom is not ("shared" or "autopay")) return "paidFrom must be shared or autopay";
        if (op.Frequency is not null && op.Frequency is not ("monthly" or "weekly" or "biweekly" or "quarterly" or "annual"))
            return "frequency is invalid";
        foreach (var date in new[] { op.FromISO, op.ToISO, op.DateISO, op.AnchorISO, op.StartISO, op.EndISO })
            if (date is not null && !BudgetAssistantContext.IsDate(date)) return "dates must be real yyyy-mm-dd dates";
        if (op.StartISO is not null && op.EndISO is not null && string.CompareOrdinal(op.StartISO, op.EndISO) > 0)
            return "its end date is before its start";
        var bill = budget.Bills.Find(item => item.Id == op.TargetId);
        var debt = budget.Debts.Find(item => item.Id == op.TargetId);
        var person = budget.People.Find(item => item.Id == op.TargetId);
        switch (op.Op)
        {
            case "add_override":
                if (op.TargetKind is not ("bill" or "debt") ||
                    (op.TargetKind == "bill" ? bill is null : debt is null)) return "it names a bill or debt that does not exist";
                if (op.FromISO is null || op.ToISO is null || op.Mode is not ("skip" or "amount"))
                    return "target, date range and skip/amount mode are required";
                if (string.CompareOrdinal(op.FromISO, op.ToISO) > 0) return "its date range ends before it starts";
                if (op.Mode == "amount" && op.AmountCents is null) return "an amount override needs amountCents";
                if (op.Mode == "skip" && op.AmountCents is not null) return "a skip override must omit amountCents";
                break;
            case "remove_override":
                if (!budget.Overrides.Any(item => item.Id == op.TargetId)) return "the override does not exist";
                break;
            case "add_one_off":
                if (op.Kind is not ("expense" or "income") || op.Name is null ||
                    op.AmountCents is null || op.DateISO is null || op.Account is not ("shared" or "autopay" or "personal"))
                    return "kind, name, amount, date and account are required";
                if (op.Account == "personal" && !budget.People.Any(item => item.Id == op.PersonId)) return "the personal account owner does not exist";
                if (op.Account != "personal" && op.PersonId is not null) return "personId only applies to personal accounts";
                break;
            case "remove_one_off":
                if (!budget.OneOffs.Any(item => item.Id == op.TargetId)) return "the one-off does not exist";
                break;
            case "add_bill":
                if (op.Name is null || op.AmountCents is null || op.DueDay is null || op.PaidFrom is null)
                    return "name, amountCents, dueDay and paidFrom are required";
                if ((op.Frequency ?? "monthly") != "monthly" && op.AnchorISO is null) return "non-monthly bills need anchorISO";
                break;
            case "update_bill":
            case "update_debt":
                if (op.Op == "update_bill" ? bill is null : debt is null) return "the target does not exist";
                if (!properties.Any(property => property.Name != "targetId")) return "no changes were specified";
                var frequency = op.Frequency ?? (op.Op == "update_bill" ? bill!.Frequency : debt!.Frequency);
                var anchor = op.AnchorISO ?? (op.Op == "update_bill" ? bill!.AnchorISO : debt!.AnchorISO);
                if (frequency != "monthly" && anchor is null) return "non-monthly payments need anchorISO";
                var start = op.StartISO ?? (op.Op == "update_bill" ? bill!.StartISO : debt!.StartISO);
                var end = op.EndISO ?? (op.Op == "update_bill" ? bill!.EndISO : debt!.EndISO);
                if (start is not null && end is not null && string.CompareOrdinal(start, end) > 0)
                    return "the resulting date range ends before it starts";
                break;
            case "remove_bill":
                if (bill is null) return "the bill does not exist";
                break;
            case "update_person":
                if (person is null || op.Name is null) return "an existing person and new name are required";
                break;
            case "set_month_income":
                if (person is null) return "the person does not exist";
                if (op.Year is null or < 1900 or > 2999 || op.Month is null or < 0 or > 11)
                    return "year must be 1900-2999 and month 0-11 (January is 0)";
                if (op.PerPaycheckCents is null or < 0) return "perPaycheckCents must be nonnegative whole cents";
                if (op.PaycheckCount is not null && op.PaycheckCount is < 1 or > 6) return "paycheckCount must be 1-6";
                break;
            case "set_balance":
                if (op.BalanceTarget is not ("essentials" or "autopay" or "personal") || op.AmountCents is null)
                    return "a valid balanceTarget and amountCents are required";
                if (op.BalanceTarget == "personal" && person is null) return "the personal account owner does not exist";
                if (op.BalanceTarget != "personal" && op.TargetId is not null) return "targetId only applies to a personal balance";
                break;
        }
        return null;
    }

    private static void ApplyValidationState(BudgetOp op, BudgetDataDto budget)
    {
        if (op.Op == "remove_bill")
        {
            budget.Bills.RemoveAll(item => item.Id == op.TargetId);
            budget.Overrides.RemoveAll(item => item.TargetKind == "bill" && item.TargetId == op.TargetId);
        }
        if (op.Op == "remove_override") budget.Overrides.RemoveAll(item => item.Id == op.TargetId);
        if (op.Op == "remove_one_off") budget.OneOffs.RemoveAll(item => item.Id == op.TargetId);
        if (op.Op == "update_bill")
        {
            var bill = budget.Bills.Find(item => item.Id == op.TargetId)!;
            bill.Frequency = op.Frequency ?? bill.Frequency;
            bill.AnchorISO = op.AnchorISO ?? bill.AnchorISO;
            bill.StartISO = op.StartISO ?? bill.StartISO;
            bill.EndISO = op.EndISO ?? bill.EndISO;
        }
        if (op.Op == "update_debt")
        {
            var debt = budget.Debts.Find(item => item.Id == op.TargetId)!;
            debt.Frequency = op.Frequency ?? debt.Frequency;
            debt.AnchorISO = op.AnchorISO ?? debt.AnchorISO;
            debt.StartISO = op.StartISO ?? debt.StartISO;
            debt.EndISO = op.EndISO ?? debt.EndISO;
        }
    }
}
