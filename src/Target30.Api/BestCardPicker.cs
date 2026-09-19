using Target30.Api.Models;

namespace Target30.Api;

public enum CardExclusionReason
{
    LimitReached,
    NoClosingDay,
}

public record CardRanking(
    PlaidAccount Account,
    CardProjection Projection,
    int? DaysUntilClosing,
    decimal? AvailableCredit,
    CardExclusionReason? ExclusionReason);

// Escolhe o melhor cartão pra usar hoje: o que vai demorar mais pra fechar a fatura (a compra
// de hoje só entra na fatura — e só vence — depois disso, então é o maior "float"). Cartão
// com limite estourado não é sugerido, e sem dia de fechamento configurado não dá pra
// comparar. Empate no prazo: o de menor utilização (mais folga).
public static class BestCardPicker
{
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

            var ranking = new CardRanking(account, projection, daysUntilClosing, available, reason);
            (reason is null ? eligible : excluded).Add(ranking);
        }

        eligible = eligible
            .OrderByDescending(c => c.DaysUntilClosing)
            .ThenBy(c => c.Projection.UtilizationPercent ?? decimal.MaxValue)
            .ThenBy(c => c.Account.Name)
            .ToList();

        return (eligible, excluded);
    }
}
