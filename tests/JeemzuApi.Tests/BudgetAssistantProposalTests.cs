using System.Text.Json;
using JeemzuApi.DTOs;
using JeemzuApi.Services;

namespace JeemzuApi.Tests;

public sealed class BudgetAssistantProposalTests
{
    private static BudgetOp Op(string name) => new() { Op = name, Rationale = "You requested this change." };

    public static IEnumerable<object[]> ValidOperations()
    {
        yield return [Op("add_override") with { TargetKind = "bill", TargetId = "rent", FromISO = "2026-10-01", ToISO = "2026-11-30", Mode = "skip" }];
        yield return [Op("add_override") with { TargetKind = "debt", TargetId = "card", FromISO = "2026-10-01", ToISO = "2026-10-01", Mode = "amount", AmountCents = 0 }];
        yield return [Op("remove_override") with { TargetId = "override" }];
        yield return [Op("add_one_off") with { Kind = "income", Name = "Bonus", AmountCents = 50000, DateISO = "2026-10-15", Account = "personal", PersonId = "person" }];
        yield return [Op("remove_one_off") with { TargetId = "bonus" }];
        yield return [Op("add_bill") with { Name = "New bill", AmountCents = 1000, DueDay = 31, PaidFrom = "autopay", Frequency = "weekly", AnchorISO = "2026-10-01" }];
        yield return [Op("update_bill") with { TargetId = "rent", AmountCents = 130000 }];
        yield return [Op("remove_bill") with { TargetId = "rent" }];
        yield return [Op("update_debt") with { TargetId = "card", AmountCents = 2000 }];
        yield return [Op("update_person") with { TargetId = "person", Name = "New name" }];
        yield return [Op("set_month_income") with { TargetId = "person", Year = 2027, Month = 0, PerPaycheckCents = 100000 }];
        yield return [Op("set_balance") with { BalanceTarget = "personal", TargetId = "person", AmountCents = -1234 }];
        yield return [Op("set_balance") with { BalanceTarget = "essentials", AmountCents = int.MinValue }];
    }

    [Theory]
    [MemberData(nameof(ValidOperations))]
    public void PreservesEverySupportedOperationAndDoesNotMutateInput(BudgetOp op)
    {
        var budget = BudgetAssistantServiceTests.Request().Budget;
        var original = JsonSerializer.Serialize(budget);
        Assert.Null(BudgetProposalValidator.Validate(op, budget));
        var result = BudgetProposalValidator.Compose(new("Review your change.", [op]), budget);
        Assert.Equal(op, Assert.Single(result.Proposal!.Ops));
        Assert.Equal(original, JsonSerializer.Serialize(budget));
    }

    public static IEnumerable<object[]> InvalidOperations()
    {
        yield return [Op("invented")];
        yield return [Op("remove_bill") with { TargetId = "missing" }];
        yield return [Op("update_bill") with { TargetId = "rent" }];
        yield return [Op("update_bill") with { TargetId = "rent", AmountCents = -1 }];
        yield return [Op("update_bill") with { TargetId = "rent", DueDay = 32 }];
        yield return [Op("update_bill") with { TargetId = "rent", Name = " " }];
        yield return [Op("update_bill") with { TargetId = "rent", Name = new string('x', 201) }];
        yield return [Op("update_bill") with { TargetId = "rent", PaidFrom = "personal" }];
        yield return [Op("update_bill") with { TargetId = "rent", Frequency = "daily" }];
        yield return [Op("update_bill") with { TargetId = "rent", Frequency = "annual" }];
        yield return [Op("update_bill") with { TargetId = "rent", AnchorISO = "2026-02-30" }];
        yield return [Op("update_bill") with { TargetId = "rent", StartISO = "2026-11-01", EndISO = "2026-10-01" }];
        yield return [Op("update_bill") with { TargetId = "rent", BalanceTarget = "essentials", AmountCents = 10 }];
        yield return [Op("update_debt") with { TargetId = "rent", Name = "Wrong kind" }];
        yield return [Op("update_person") with { TargetId = "missing", Name = "Name" }];
        yield return [Op("remove_override") with { TargetId = "rent" }];
        yield return [Op("remove_one_off") with { TargetId = "missing" }];
        yield return [Op("set_month_income") with { TargetId = "person", Year = 2027, Month = 12, PerPaycheckCents = 1 }];
        yield return [Op("set_month_income") with { TargetId = "person", Year = 2027, Month = 0, PerPaycheckCents = 1, PaycheckCount = 0 }];
        yield return [Op("set_month_income") with { TargetId = "person", Year = 2027, Month = 0, PerPaycheckCents = -1 }];
        yield return [Op("set_balance") with { BalanceTarget = "personal", TargetId = "missing", AmountCents = -10 }];
        yield return [Op("set_balance") with { BalanceTarget = "autopay", TargetId = "person", AmountCents = 10 }];
        yield return [Op("set_balance") with { BalanceTarget = "shared", AmountCents = 10 }];
        yield return [Op("add_override") with { TargetKind = "bill", TargetId = "rent", FromISO = "2026-11-01", ToISO = "2026-10-01", Mode = "skip" }];
        yield return [Op("add_override") with { TargetKind = "bill", TargetId = "rent", FromISO = "2026-10-01", ToISO = "2026-11-01", Mode = "amount" }];
        yield return [Op("add_one_off") with { Name = "Expense", AmountCents = 10, DateISO = "2026-10-01", Kind = "expense", Account = "personal" }];
        yield return [Op("add_one_off") with { Name = "Expense", AmountCents = 10, DateISO = "2026-10-01", Kind = "expense", Account = "shared", Note = new string('x', 501) }];
    }

    [Theory]
    [MemberData(nameof(InvalidOperations))]
    public void DropsInvalidOperationsWithUserVisibleReasons(BudgetOp invalid)
    {
        var result = BudgetProposalValidator.Compose(new("Requested change.", [invalid]), BudgetAssistantServiceTests.Request().Budget);
        Assert.Null(result.Proposal);
        Assert.Contains("Dropped", result.Answer);
    }

    [Fact]
    public void ValidatesMergedAndSequentialRecurrenceWithoutErasingOmittedValues()
    {
        var budget = BudgetAssistantServiceTests.Request().Budget;
        budget.Bills[0].EndISO = "2026-11-01";
        var invalid = Op("update_bill") with { TargetId = "rent", StartISO = "2026-12-01" };
        Assert.NotNull(BudgetProposalValidator.Validate(invalid, budget));
        var result = BudgetProposalValidator.Compose(new("Change recurrence.", [
            Op("update_bill") with { TargetId = "rent", Frequency = "weekly", AnchorISO = "2026-10-01" },
            Op("update_bill") with { TargetId = "rent", AmountCents = 1000 },
            Op("remove_bill") with { TargetId = "rent" },
            Op("update_bill") with { TargetId = "rent", Name = "Removed" },
            Op("remove_override") with { TargetId = "override" },
        ]), budget);
        Assert.Equal(3, result.Proposal!.Ops.Length);
        Assert.Contains("Dropped \"update_bill\"", result.Answer);
        Assert.Contains("Dropped \"remove_override\"", result.Answer);
        Assert.Equal("monthly", budget.Bills[0].Frequency);
        Assert.Single(budget.Overrides);
    }

    [Fact]
    public void ClarificationsAreAnswersWithoutProposalsAndOversizedProposalsFailExplicitly()
    {
        var budget = BudgetAssistantServiceTests.Request().Budget;
        Assert.Null(BudgetProposalValidator.Compose(new("Which bill do you mean?", []), budget).Proposal);
        Assert.Throws<InvalidDataException>(() => BudgetProposalValidator.Compose(
            new("Too many.", Enumerable.Repeat(Op("remove_bill") with { TargetId = "rent" }, 26).ToArray()), budget));
    }

    [Fact]
    public void TargetIdsAreScopedByCollectionAndDebtUpdatesUseDebtRecurrence()
    {
        var request = BudgetAssistantServiceTests.Request();
        request.Budget.Debts[0].Id = "rent";
        request.Budget.Debts[0].Frequency = "weekly";
        request.Budget.Debts[0].AnchorISO = "2026-10-01";
        Assert.Null(BudgetAssistantContext.Validate(request));
        Assert.Null(BudgetProposalValidator.Validate(
            Op("update_debt") with { TargetId = "rent", AmountCents = 1234 }, request.Budget));
        request.Budget.Debts[0].AnchorISO = null;
        Assert.NotNull(BudgetProposalValidator.Validate(
            Op("update_debt") with { TargetId = "rent", AmountCents = 1234 }, request.Budget));
    }
}
