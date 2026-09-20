using Target30.Api.Controllers;

namespace Target30.Api;

// Primeiro ponto em que o saldo projetado fica abaixo do limite escolhido. Description = o
// lançamento que faz o saldo cruzar o limite (null quando o saldo JÁ está abaixo hoje).
public record LowBalanceWarning(
    DateOnly Date, decimal Balance, string? Description, bool AlreadyBelow, decimal MinimumBalance)
{
    // Identifica o problema pra não mandar o mesmo email de novo a cada sync.
    public string Key => AlreadyBelow ? "now" : $"{Date:yyyy-MM-dd}|{Description}";
}

public static class LowBalanceDetector
{
    public static string BuildAlertBody(LowBalanceWarning warning, decimal threshold)
    {
        var limit = threshold > 0 ? $"abaixo de {threshold:C}" : "negativo";
        var what = warning.AlreadyBelow
            ? $"O saldo das suas contas correntes já está {limit} ({warning.Balance:C})."
            : $"Em {warning.Date:yyyy-MM-dd}, com \"{warning.Description}\", o saldo projetado das suas contas correntes fica {limit} ({warning.Balance:C}).";
        return $"Target30 — saldo baixo:\n\n{what}\nMenor saldo projetado nos próximos dias: {warning.MinimumBalance:C}.\n\nVeja o Fluxo de Caixa pra decidir o que adiantar ou postergar.";
    }

    // Olha o saldo de hoje e depois cada lançamento futuro (Date > today); os de hoje pra trás
    // já estão no saldo atual.
    public static LowBalanceWarning? Find(
        IReadOnlyList<CashFlowEntryDto> rows, decimal currentBalance, decimal threshold, DateOnly today)
    {
        var future = rows.Where(r => r.Date > today).ToList();
        var minimum = future.Count > 0 ? Math.Min(currentBalance, future.Min(r => r.Balance)) : currentBalance;

        if (currentBalance < threshold)
            return new LowBalanceWarning(today, currentBalance, null, true, minimum);

        var crossing = future.FirstOrDefault(r => r.Balance < threshold);
        return crossing is null
            ? null
            : new LowBalanceWarning(crossing.Date, crossing.Balance, crossing.Description, false, minimum);
    }
}
