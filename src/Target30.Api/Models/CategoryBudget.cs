namespace Target30.Api.Models;

// Teto de gasto mensal por categoria (código do Plaid, ex.: GENERAL_MERCHANDISE) — independente
// da meta de utilização de cartão, é sobre o total gasto na categoria no mês corrente.
public class CategoryBudget
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Category { get; set; } = null!;
    public decimal MonthlyLimit { get; set; }

    // "yyyy-MM" do último mês em que já avisamos por email que estourou — evita reenviar todo
    // dia enquanto o mês não vira.
    public string? LastAlertSentMonth { get; set; }
}
