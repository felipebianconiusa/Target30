namespace Target30.Api.Models;

// Cópia local de uma transação do Plaid (transactions/sync é incremental via
// cursor — sem guardar isso aqui, teríamos que rebaixar o histórico inteiro
// do zero a cada chamada).
public class PlaidTransaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string PlaidTransactionId { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public string ItemId { get; set; } = null!;
    public string? InstitutionName { get; set; }
    public decimal Amount { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = null!;
    public string? MerchantName { get; set; }
    public bool Pending { get; set; }
    public string? Category { get; set; }
}
