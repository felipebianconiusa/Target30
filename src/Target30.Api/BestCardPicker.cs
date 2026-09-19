using Target30.Api.Models;

namespace Target30.Api;

public enum CardExclusionReason
{
    LimitReached,
    NoClosingDay,
}

// Recommended: fecha longe e a utilização está dentro da meta. Alternative: dá pra usar sem
// prejuízo, mas fecha mais cedo. Caution: fecha muito em breve ou já está acima da meta de
// utilização (usar mais empurra o percentual do credit score pra cima).
public enum CardTier
{
    Recommended,
    Alternative,
    Caution,
}

public record CardRanking(
    PlaidAccount Account,
    CardProjection Projection,
    int? DaysUntilClosing,
    decimal? AvailableCredit,
    CardExclusionReason? ExclusionReason,
    CardTier? Tier = null,
    bool OverTarget = false);

// Classifica os cartões de crédito pra "qual usar hoje". O critério principal é o quanto
// demora pra fechar a fatura (a compra de hoje só entra na fatura — e só vence — depois disso,
// então é o maior "float"). Cartão com limite estourado ou sem dia de fechamento configurado
// (não dá pra comparar) fica de fora. Dentro de cada grupo: mais dias até fechar primeiro;
// empate, menor utilização.
public static class BestCardPicker
{
    public const int RecommendedMinDays = 15;
    public const int AlternativeMinDays = 7;

    public static (List<CardRanking> Eligible, List<CardRanking> Excluded) Rank(
        IEnumerable<(PlaidAccount Account, CardProjection Projection)> cards, DateOnly today)
    {
        var eligible = new List<CardRanking>();
        var excluded = new List<CardRanking>();

        foreach (var (account, projection) in cards)
        {
            int? daysUntilClosing = projection.NextClosingDate is { } closing
                ? closing.DayNumber - today.DayNumber
                : null;

            // Limite desconhecido (ex.: Capital One via Plaid) = não dá pra afirmar que estourou.
            decimal? available = projection.Limit > 0 ? projection.Limit - projection.Balance : null;

            CardExclusionReason? reason = null;
            if (available is <= 0)
                reason = CardExclusionReason.LimitReached;
            else if (daysUntilClosing is null)
                reason = CardExclusionReason.NoClosingDay;

            if (reason is not null)
            {
                excluded.Add(new CardRanking(account, projection, daysUntilClosing, available, reason));
                continue;
            }

            var overTarget = projection.UtilizationPercent is { } utilization && utilization > projection.TargetPercent;
            var tier = Classify(daysUntilClosing!.Value, overTarget);
            eligible.Add(new CardRanking(account, projection, daysUntilClosing, available, null, tier, overTarget));
        }

        eligible = eligible
            .OrderBy(c => c.Tier)
            .ThenByDescending(c => c.DaysUntilClosing)
            .ThenBy(c => c.Projection.UtilizationPercent ?? decimal.MaxValue)
            .ThenBy(c => c.Account.Name)
            .ToList();

        return (eligible, excluded);
    }

    private static CardTier Classify(int daysUntilClosing, bool overTarget)
    {
        if (overTarget || daysUntilClosing < AlternativeMinDays)
            return CardTier.Caution;

        return daysUntilClosing >= RecommendedMinDays ? CardTier.Recommended : CardTier.Alternative;
    }
}
