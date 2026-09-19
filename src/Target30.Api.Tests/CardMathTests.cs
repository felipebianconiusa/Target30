using Target30.Api;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class CardMathTests
{
    private static PlaidAccount MakeAccount(
        decimal? currentBalance = 0m,
        decimal? creditLimit = 1000m,
        decimal? manualCreditLimit = null,
        int? statementClosingDay = null,
        decimal? targetUtilizationPercent = null) =>
        new()
        {
            UserId = "user-1",
            ItemId = "item-1",
            AccountId = "account-1",
            Name = "Test Card",
            Type = "Credit",
            CurrentBalance = currentBalance,
            CreditLimit = creditLimit,
            ManualCreditLimit = manualCreditLimit,
            StatementClosingDay = statementClosingDay,
            TargetUtilizationPercent = targetUtilizationPercent,
        };

    [Fact]
    public void Compute_calculates_utilization_percent_rounded_to_one_decimal()
    {
        var account = MakeAccount(currentBalance: 291.4m, creditLimit: 1000m);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(29.1m, result.UtilizationPercent);
    }

    [Fact]
    public void Compute_uses_global_target_when_account_has_no_custom_target()
    {
        var account = MakeAccount(targetUtilizationPercent: null);

        var result = CardMath.Compute(account, globalTargetPercent: 25m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(25m, result.TargetPercent);
    }

    [Fact]
    public void Compute_uses_account_custom_target_over_global_target()
    {
        var account = MakeAccount(targetUtilizationPercent: 10m);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(10m, result.TargetPercent);
    }

    [Fact]
    public void Compute_amount_to_pay_is_zero_when_balance_is_within_target()
    {
        // Limite 1000, meta 30% => até 300 de saldo não precisa pagar nada.
        var account = MakeAccount(currentBalance: 250m, creditLimit: 1000m);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(0m, result.AmountToPay);
    }

    [Fact]
    public void Compute_amount_to_pay_covers_the_excess_over_the_target_balance()
    {
        // Limite 1000, meta 30% (target balance = 300), saldo 450 => precisa pagar 150.
        var account = MakeAccount(currentBalance: 450m, creditLimit: 1000m);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(150m, result.AmountToPay);
    }

    [Fact]
    public void Compute_treats_missing_limit_as_zero_utilization_percent_null_but_still_requires_full_balance()
    {
        // Sem limite cadastrado (nem manual): não dá pra calcular %, mas o "quanto pagar" cai
        // pro saldo inteiro (meta de 0 sobre limite 0).
        var account = MakeAccount(currentBalance: 72.14m, creditLimit: null, manualCreditLimit: null);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Null(result.UtilizationPercent);
        Assert.Equal(72.14m, result.AmountToPay);
    }

    [Fact]
    public void Compute_uses_manual_credit_limit_override_when_plaid_does_not_report_one()
    {
        var account = MakeAccount(currentBalance: 100m, creditLimit: null, manualCreditLimit: 1000m);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(1000m, result.Limit);
        Assert.Equal(10m, result.UtilizationPercent);
    }

    [Fact]
    public void Compute_manual_credit_limit_takes_priority_over_plaid_limit()
    {
        var account = MakeAccount(currentBalance: 100m, creditLimit: 500m, manualCreditLimit: 1000m);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(1000m, result.Limit);
    }

    [Fact]
    public void Compute_returns_null_dates_when_no_statement_closing_day_is_configured()
    {
        var account = MakeAccount(statementClosingDay: null);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Null(result.NextClosingDate);
        Assert.Null(result.PaymentDeadline);
        Assert.Null(result.DaysUntilPaymentDeadline);
    }

    [Fact]
    public void Compute_next_closing_date_is_this_month_when_closing_day_has_not_passed_yet()
    {
        var account = MakeAccount(statementClosingDay: 20);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(new DateOnly(2026, 1, 20), result.NextClosingDate);
    }

    [Fact]
    public void Compute_next_closing_date_rolls_over_to_next_month_when_closing_day_already_passed()
    {
        var account = MakeAccount(statementClosingDay: 10);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(new DateOnly(2026, 2, 10), result.NextClosingDate);
    }

    [Fact]
    public void Compute_next_closing_date_equals_today_rolls_over_to_next_month()
    {
        // "candidate <= today" empurra pro mês seguinte mesmo quando cai exatamente hoje —
        // já não dá mais tempo de agir no fechamento de hoje.
        var account = MakeAccount(statementClosingDay: 15);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(new DateOnly(2026, 2, 15), result.NextClosingDate);
    }

    [Fact]
    public void Compute_clamps_closing_day_to_the_last_day_of_short_months()
    {
        // Fechamento configurado como dia 31, mas fevereiro/2026 (não bissexto) só tem 28 dias.
        var account = MakeAccount(statementClosingDay: 31);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 31));

        Assert.Equal(new DateOnly(2026, 2, 28), result.NextClosingDate);
    }

    [Fact]
    public void Compute_payment_deadline_matches_closing_date_when_it_falls_on_a_business_day()
    {
        // 2026-01-20 é uma terça-feira.
        var account = MakeAccount(statementClosingDay: 20);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(new DateOnly(2026, 1, 20), result.PaymentDeadline);
    }

    [Fact]
    public void Compute_payment_deadline_moves_back_to_friday_when_closing_falls_on_saturday()
    {
        // 2026-01-17 é sábado; dia útil anterior é sexta 2026-01-16.
        var account = MakeAccount(statementClosingDay: 17);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 10));

        Assert.Equal(new DateOnly(2026, 1, 16), result.PaymentDeadline);
    }

    [Fact]
    public void Compute_payment_deadline_moves_back_when_closing_falls_on_a_holiday()
    {
        // 2026-01-01 é feriado (Ano Novo, quinta-feira) — prazo real cai pra 2025-12-31 (quarta).
        var account = MakeAccount(statementClosingDay: 1);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2025, 12, 20));

        Assert.Equal(new DateOnly(2025, 12, 31), result.PaymentDeadline);
    }

    [Fact]
    public void Compute_days_until_payment_deadline_counts_from_today()
    {
        var account = MakeAccount(statementClosingDay: 20);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(5, result.DaysUntilPaymentDeadline);
    }

    [Fact]
    public void Compute_balance_and_limit_reflect_zero_when_not_reported()
    {
        var account = MakeAccount(currentBalance: null, creditLimit: null);

        var result = CardMath.Compute(account, globalTargetPercent: 30m, today: new DateOnly(2026, 1, 15));

        Assert.Equal(0m, result.Balance);
        Assert.Equal(0m, result.Limit);
    }
}
