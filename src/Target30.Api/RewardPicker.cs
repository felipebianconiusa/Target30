using Target30.Api.Models;

namespace Target30.Api;

public record RewardRanking(CardRanking Card, decimal RatePercent, bool Caution, bool NoClosingDay);

// "Qual cartão usar pra essa compra": maior recompensa na categoria; o prazo até o fechamento
// (BestCardPicker) desempata e serve de aviso. Cartão acima da meta de utilização ou fechando em
// poucos dias (grupo "cautela") só é sugerido se não houver outro; limite estourado fica de fora.
public static class RewardPicker
{
    public static decimal RateFor(IReadOnlyDictionary<string, decimal>? rates, string category)
    {
        if (rates is null)
            return 0m;

        if (rates.TryGetValue(category, out var specific))
            return specific;

        return rates.TryGetValue(CardRewardRate.BaseCategory, out var baseRate) ? baseRate : 0m;
    }

    // `cards`: eligible + excluded do BestCardPicker.Rank. Cartão sem dia de fechamento entra
    // (a recompensa não depende dele), só que sem prazo; limite estourado sai.
    public static List<RewardRanking> Rank(
        IEnumerable<CardRanking> cards,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> ratesByAccount,
        string category)
    {
        return cards
            .Where(c => c.ExclusionReason != CardExclusionReason.LimitReached)
            .Select(c =>
            {
                ratesByAccount.TryGetValue(c.Account.AccountId, out var rates);
                return new RewardRanking(
                    c, RateFor(rates, category), c.Tier == CardTier.Caution, c.ExclusionReason == CardExclusionReason.NoClosingDay);
            })
            .OrderBy(r => r.Caution)
            .ThenByDescending(r => r.RatePercent)
            .ThenBy(r => r.NoClosingDay)
            .ThenBy(r => r.Card.Tier)
            .ThenByDescending(r => r.Card.DaysUntilClosing)
            .ThenBy(r => r.Card.Account.Name)
            .ToList();
    }
}
