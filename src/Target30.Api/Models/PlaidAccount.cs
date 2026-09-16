namespace Target30.Api.Models;

public class PlaidAccount
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string ItemId { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? OfficialName { get; set; }
    public string? InstitutionName { get; set; }
    public string Type { get; set; } = null!;
    public string? Subtype { get; set; }
    public decimal? CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public decimal? CreditLimit { get; set; }
    public string? IsoCurrencyCode { get; set; }

    // Só preenchido para cartões de crédito, via /liabilities/get (precisa do produto
    // Liabilities habilitado no Item — itens conectados antes disso não têm até reconectar).
    public decimal? LastStatementBalance { get; set; }
    public DateOnly? LastStatementIssueDate { get; set; }
    public DateOnly? NextPaymentDueDate { get; set; }
    public decimal? MinimumPaymentAmount { get; set; }
    public bool? IsOverdue { get; set; }

    // Configuração do usuário. StatementClosingDay é sugerido a partir de LastStatementIssueDate
    // na primeira sincronização, mas pode ser corrigido manualmente (o Plaid não informa a
    // próxima data de fechamento, só a do último extrato).
    public int? StatementClosingDay { get; set; }
    public decimal? TargetUtilizationPercent { get; set; }
}
