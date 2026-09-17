using Target30.Api.Models;

namespace Target30.Api;

// Cálculo de utilização/prazo de um cartão de crédito, compartilhado entre CardsController
// (tela de Cartões) e CashFlowController — pra nunca ficarem com números diferentes pro
// mesmo cartão.
public readonly record struct CardProjection(
    decimal Balance,
    decimal Limit,
    decimal? UtilizationPercent,
    decimal TargetPercent,
    decimal AmountToPay,
    DateOnly? NextClosingDate,
    DateOnly? PaymentDeadline,
    int? DaysUntilPaymentDeadline
);

public static class CardMath
{
    public static CardProjection Compute(PlaidAccount account, decimal globalTargetPercent, DateOnly today)
    {
        var targetPercent = account.TargetUtilizationPercent ?? globalTargetPercent;
        var limit = account.CreditLimit ?? 0m;
        var balance = account.CurrentBalance ?? 0m;
        var utilizationPercent = limit > 0 ? Math.Round(balance / limit * 100, 1) : (decimal?)null;
        var targetBalance = limit * (targetPercent / 100m);
        var amountToPay = Math.Max(0, Math.Round(balance - targetBalance, 2));

        var nextClosingDate = ComputeNextClosingDate(account.StatementClosingDay, today);
        // Bancos não processam pagamento em fim de semana/feriado — se o fechamento cair num
        // desses dias, o prazo real pra pagar é o último dia útil anterior.
        var paymentDeadline = nextClosingDate is not null
            ? UsBusinessDays.PreviousOrSameBusinessDay(nextClosingDate.Value)
            : (DateOnly?)null;
        var daysUntilDeadline = paymentDeadline is not null
            ? paymentDeadline.Value.DayNumber - today.DayNumber
            : (int?)null;

        return new CardProjection(
            balance,
            limit,
            utilizationPercent,
            targetPercent,
            amountToPay,
            nextClosingDate,
            paymentDeadline,
            daysUntilDeadline);
    }

    private static DateOnly? ComputeNextClosingDate(int? closingDay, DateOnly today)
    {
        if (closingDay is null)
            return null;

        var candidate = DateMath.BuildClamped(today.Year, today.Month, closingDay.Value);
        if (candidate <= today)
        {
            var next = today.AddMonths(1);
            candidate = DateMath.BuildClamped(next.Year, next.Month, closingDay.Value);
        }
        return candidate;
    }
}
