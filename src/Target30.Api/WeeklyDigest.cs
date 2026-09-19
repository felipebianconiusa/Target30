using System.Globalization;
using Target30.Api.Models;

namespace Target30.Api;

public static class WeeklyDigest
{
    // "Semanal" aqui não é preso a um dia da semana fixo — dispara sempre que já se passou
    // pelo menos `intervalDays` desde o último envio (ou nunca foi enviado). Assim funciona
    // independente de quando o background service acordar.
    public static bool ShouldSend(DateOnly? lastSentDate, DateOnly today, int intervalDays = 7) =>
        lastSentDate is null || today.DayNumber - lastSentDate.Value.DayNumber >= intervalDays;

    public static string BuildBody(IReadOnlyList<(PlaidAccount Account, CardProjection Projection)> cards, DateOnly today)
    {
        var totalBalance = cards.Sum(c => c.Projection.Balance);
        var totalLimit = cards.Sum(c => c.Projection.Limit);
        var overallUtilization = totalLimit > 0 ? Math.Round(totalBalance / totalLimit * 100, 1) : (decimal?)null;

        var lines = new List<string>
        {
            "Target30 — resumo semanal dos seus cartões:",
            "",
            overallUtilization is not null
                ? $"Utilização geral: {overallUtilization}% (saldo {totalBalance:C} de {totalLimit:C} de limite total)"
                : $"Saldo total: {totalBalance:C} (limite total não informado em todos os cartões)",
            "",
        };

        foreach (var (account, projection) in cards.OrderBy(c => c.Projection.DaysUntilPaymentDeadline ?? int.MaxValue))
        {
            var utilization = projection.UtilizationPercent?.ToString("F1", CultureInfo.InvariantCulture) ?? "?";
            var deadline = projection.PaymentDeadline is { } d
                ? $"fechamento até {d:yyyy-MM-dd}"
                : "sem data de fechamento configurada";
            var status = projection.AmountToPay > 0
                ? $"pagar {projection.AmountToPay:C} pra ficar em {projection.TargetPercent}%"
                : "dentro da meta";

            lines.Add($"- {account.Name}: {utilization}% de utilização, {status} ({deadline})");
        }

        lines.Add("");
        lines.Add("Esse resumo é enviado a cada ~7 dias. Pra desativar, vá em Configurações.");

        return string.Join("\n", lines);
    }
}
