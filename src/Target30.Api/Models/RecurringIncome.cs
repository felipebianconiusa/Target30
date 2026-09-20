namespace Target30.Api.Models;

// Entrada esperada que se repete (ex.: o Zelle semanal, o depósito mensal). Só entra na projeção
// do Fluxo de Caixa depois que o usuário confirma (a detecção só sugere).
public class RecurringIncome
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Description { get; set; } = null!;

    // Sempre positivo (dinheiro entrando) — ao contrário do RecurringBill, que segue o sinal do Plaid.
    public decimal Amount { get; set; }

    // "weekly", "biweekly" ou "monthly".
    public string Frequency { get; set; } = IncomeFrequency.Monthly;

    // Uma data em que a entrada aconteceu (ou vai acontecer): as demais saem dela pelo intervalo
    // (semanal/quinzenal) ou pelo dia do mês (mensal).
    public DateOnly AnchorDate { get; set; }

    public bool IsActive { get; set; } = true;
}

public static class IncomeFrequency
{
    public const string Weekly = "weekly";
    public const string Biweekly = "biweekly";
    public const string Monthly = "monthly";

    public static bool IsValid(string? value) => value is Weekly or Biweekly or Monthly;
}
