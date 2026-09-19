namespace Target30.Api;

public record HealthScoreResult(int Score, int CardComponent, int SpendingComponent, string Label);

// Um número único (0-100) resumindo "como você está indo": combina o quanto os cartões estão
// acima da meta de utilização (peso 60%) com a tendência de gasto esse mês vs mês passado
// (peso 40%). Não é ciência exata — é um indicador rápido pro Dashboard, não uma nota de
// crédito de verdade.
public static class HealthScoreCalculator
{
    public static HealthScoreResult Compute(
        IReadOnlyList<CardProjection> cardProjections, decimal currentMonthSpend, decimal previousMonthSpend)
    {
        var cardComponent = ComputeCardComponent(cardProjections);
        var spendingComponent = ComputeSpendingComponent(currentMonthSpend, previousMonthSpend);
        var score = (int)Math.Round(cardComponent * 0.6 + spendingComponent * 0.4);

        var label = score switch
        {
            >= 80 => "great",
            >= 60 => "good",
            >= 40 => "fair",
            _ => "poor",
        };

        return new HealthScoreResult(score, cardComponent, spendingComponent, label);
    }

    private static int ComputeCardComponent(IReadOnlyList<CardProjection> cards)
    {
        var withUtilization = cards.Where(c => c.UtilizationPercent is not null).ToList();
        if (withUtilization.Count == 0)
            return 100;

        // 2 pontos de penalidade por cada 1% de utilização acima da meta, até 100 (zera o
        // componente); cartões dentro da meta não penalizam.
        var penalties = withUtilization.Select(c =>
            Math.Min(100m, Math.Max(0m, c.UtilizationPercent!.Value - c.TargetPercent) * 2));

        var avgPenalty = penalties.Average();
        return (int)Math.Round(Math.Max(0, 100 - avgPenalty));
    }

    private static int ComputeSpendingComponent(decimal currentMonthSpend, decimal previousMonthSpend)
    {
        if (previousMonthSpend <= 0)
            return 100;

        var changeRatio = (currentMonthSpend - previousMonthSpend) / previousMonthSpend;
        // Gastar igual ou menos que o mesmo período do mês passado = 100. Cada 10% de aumento
        // tira 10 pontos.
        var score = 100m - Math.Max(0, changeRatio) * 100;
        return (int)Math.Round(Math.Clamp(score, 0, 100));
    }
}
