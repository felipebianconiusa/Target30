using Target30.Api.Models;

namespace Target30.Api;

public record DetectedCharge(string MerchantName, decimal AverageAmount, int SuggestedDayOfMonth, int Occurrences, DateOnly LastDate);

// Heurística simples pra achar cobranças recorrentes (assinaturas etc.) no histórico de
// transações: mesmo estabelecimento, valor consistente (±15%, mín. ±$2) e intervalo entre
// compras sempre parecido com um mês (21-40 dias). Não é perfeita — é só um ponto de partida.
// Usada tanto pra sugerir novas assinaturas quanto pra detectar mudança de valor numa já
// cadastrada.
public static class SubscriptionDetector
{
    public static DetectedCharge? Detect(IReadOnlyList<PlaidTransaction> transactionsForOneMerchant)
    {
        var txs = transactionsForOneMerchant.OrderBy(t => t.Date).ToList();
        if (txs.Count < 2)
            return null;

        var avgAmount = txs.Average(t => t.Amount);
        var tolerance = Math.Max(2m, avgAmount * 0.15m);
        if (txs.Any(t => Math.Abs(t.Amount - avgAmount) > tolerance))
            return null;

        var gaps = new List<int>();
        for (var i = 1; i < txs.Count; i++)
            gaps.Add(txs[i].Date.DayNumber - txs[i - 1].Date.DayNumber);

        if (gaps.Any(g => g is < 21 or > 40))
            return null;

        var last = txs[^1];
        var displayName = last.MerchantName ?? last.Name;
        return new DetectedCharge(displayName, Math.Round(avgAmount, 2), last.Date.Day, txs.Count, last.Date);
    }
}
