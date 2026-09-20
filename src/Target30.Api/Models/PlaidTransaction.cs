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

    // Categoria escolhida manualmente pelo usuário — tem prioridade sobre a do Plaid e não é
    // sobrescrita nos syncs seguintes (só o UpsertAsync toca em Category, nunca neste campo).
    public string? UserCategory { get; set; }

    // Categoria detalhada do Plaid (ex.: LOAN_PAYMENTS_CREDIT_CARD_PAYMENT) — é ela que diz se
    // uma linha é só dinheiro trocando de lugar, o que a categoria primária não distingue.
    public string? DetailedCategory { get; set; }

    // Pagamento de fatura de cartão ou transferência entre contas do próprio usuário: aparece
    // nas duas pontas (saída na corrente, "entrada" no cartão) e por isso NÃO conta como gasto
    // nem como receita nos totais/orçamentos. Continua na lista e no Fluxo de Caixa (o saldo da
    // conta realmente muda). Definido no sync por TransactionClassifier.
    public bool IsInternalTransfer { get; set; }

    public string? EffectiveCategory => UserCategory ?? Category;
}
