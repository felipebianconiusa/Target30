using Target30.Api.Models;

namespace Target30.Api;

public record SimulatedCard(
    PlaidAccount Account,
    decimal Balance,
    decimal Limit,
    decimal UtilizationBefore,
    decimal ToPay,
    decimal BalanceAfter,
    decimal UtilizationAfter,
    DateOnly? NextClosingDate,
    DateOnly? PayBy,
    int? DaysUntilPayBy);

public record UtilizationSimulation(
    decimal TargetPercent,
    IReadOnlyList<SimulatedCard> Cards,
    IReadOnlyList<PlaidAccount> SkippedWithoutLimit,
    decimal TotalToPay,
    decimal? OverallBefore,
    decimal? OverallAfter);

// "Quanto pagar antes do fechamento pra a utilização reportada ficar em X%": por cartão e no total.
// O banco reporta aos bureaus o saldo do fechamento da fatura, então o que importa é o saldo
// nessa data (supondo que você não gaste mais até lá). Cartão sem limite conhecido não entra na
// conta (não dá pra calcular %) e é listado à parte.
public static class UtilizationSimulator
{
    public static UtilizationSimulation Simulate(
        IEnumerable<(PlaidAccount Account, CardProjection Projection)> cards, decimal targetPercent)
    {
        targetPercent = Math.Clamp(targetPercent, 0m, 100m);
        var simulated = new List<SimulatedCard>();
        var skipped = new List<PlaidAccount>();

        foreach (var (account, p) in cards)
        {
            if (p.Limit <= 0)
            {
                skipped.Add(account);
                continue;
            }

            var balance = Math.Max(p.Balance, 0m);
            var targetBalance = Math.Floor(p.Limit * targetPercent) / 100m; // arredonda pra baixo: garante ficar dentro da meta
            var toPay = Math.Max(Math.Round(balance - targetBalance, 2), 0m);
            var after = balance - toPay;

            simulated.Add(new SimulatedCard(
                account, balance, p.Limit,
                Math.Round(balance / p.Limit * 100, 1), toPay, after,
                Math.Round(after / p.Limit * 100, 1),
                p.NextClosingDate, p.PaymentDeadline, p.DaysUntilPaymentDeadline));
        }

        simulated = simulated
            .OrderBy(c => c.DaysUntilPayBy ?? int.MaxValue)
            .ThenByDescending(c => c.ToPay)
            .ThenBy(c => c.Account.Name)
            .ToList();

        var totalLimit = simulated.Sum(c => c.Limit);
        decimal? overallBefore = totalLimit > 0 ? Math.Round(simulated.Sum(c => c.Balance) / totalLimit * 100, 1) : null;
        decimal? overallAfter = totalLimit > 0 ? Math.Round(simulated.Sum(c => c.BalanceAfter) / totalLimit * 100, 1) : null;

        return new UtilizationSimulation(targetPercent, simulated, skipped, simulated.Sum(c => c.ToPay), overallBefore, overallAfter);
    }
}
