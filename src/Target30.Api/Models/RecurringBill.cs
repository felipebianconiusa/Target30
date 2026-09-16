namespace Target30.Api.Models;

public class RecurringBill
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Description { get; set; } = null!;

    // Mesma convenção do Plaid: positivo = saída (despesa), negativo = entrada (receita).
    public decimal Amount { get; set; }
    public int DayOfMonth { get; set; }
    public bool IsActive { get; set; } = true;
}
