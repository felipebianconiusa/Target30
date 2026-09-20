using Target30.Api.Models;

namespace Target30.Api;

public record DetectedIncome(string Description, decimal Amount, string Frequency, DateOnly LastDate, int Occurrences);

// Acha entradas que se repetem (semanal, quinzenal, mensal) no histórico da conta corrente. Não é
// perfeita: só sugere, o usuário confirma antes de entrar na projeção do Fluxo de Caixa.
public static class IncomeDetector
{
    public const int MinOccurrences = 3;
    public const decimal MinAmount = 25m;

    private static readonly string[] Cutoffs = [" Conf#", " ID:", " INDN:", " CO ID", " for \""];

    // Tira o que muda a cada lançamento (número de confirmação, ids) pra agrupar o mesmo remetente.
    public static string Normalize(string name)
    {
        var end = name.Length;
        foreach (var cutoff in Cutoffs)
        {
            var i = name.IndexOf(cutoff, StringComparison.OrdinalIgnoreCase);
            if (i > 0 && i < end)
                end = i;
        }

        var normalized = string.Join(' ', name[..end].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 80 ? normalized[..80] : normalized;
    }

    // `inflows`: entradas (Amount < 0 no Plaid) de um mesmo remetente já normalizado.
    public static DetectedIncome? Detect(string description, IReadOnlyList<PlaidTransaction> inflows, DateOnly today)
    {
        var txs = inflows.OrderBy(t => t.Date).ToList();
        if (txs.Count < MinOccurrences)
            return null;

        var last = txs[^1].Date;
        if (today.DayNumber - last.DayNumber > 45)
            return null; // parou de acontecer

        var gaps = new List<int>();
        for (var i = 1; i < txs.Count; i++)
            gaps.Add(txs[i].Date.DayNumber - txs[i - 1].Date.DayNumber);

        var median = Median(gaps.Select(g => (decimal)g).ToList());
        var frequency = median switch
        {
            >= 5 and <= 9 => IncomeFrequency.Weekly,
            >= 12 and <= 16 => IncomeFrequency.Biweekly,
            >= 26 and <= 35 => IncomeFrequency.Monthly,
            _ => null,
        };
        if (frequency is null)
            return null;

        // Maioria dos intervalos precisa estar perto do típico, senão é coincidência.
        var tolerance = frequency == IncomeFrequency.Monthly ? 4 : 2;
        var consistent = gaps.Count(g => Math.Abs(g - median) <= tolerance);
        if (consistent < Math.Ceiling(gaps.Count * 0.6m))
            return null;

        var amount = Math.Round(Median(txs.TakeLast(4).Select(t => -t.Amount).ToList()), 2);
        return amount < MinAmount ? null : new DetectedIncome(description, amount, frequency, last, txs.Count);
    }

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
    }

    // Datas em que a entrada deve cair, estritamente depois de `fromExclusive` até `toInclusive`.
    public static IEnumerable<DateOnly> Occurrences(RecurringIncome income, DateOnly fromExclusive, DateOnly toInclusive)
    {
        if (income.Frequency == IncomeFrequency.Monthly)
        {
            foreach (var d in DateMath.MonthlyOccurrences(income.AnchorDate.Day, fromExclusive, toInclusive))
                yield return d;
            yield break;
        }

        var step = income.Frequency == IncomeFrequency.Weekly ? 7 : 14;
        var date = income.AnchorDate;
        if (date <= fromExclusive)
        {
            var jumps = (fromExclusive.DayNumber - date.DayNumber) / step + 1;
            date = date.AddDays(jumps * step);
        }

        for (; date <= toInclusive; date = date.AddDays(step))
            yield return date;
    }
}
