using Target30.Api.Models;

namespace Target30.Api;

public static class TransactionClassifier
{
    // Só o que é INEQUIVOCAMENTE dinheiro do próprio usuário mudando de lugar. Transferência
    // por app (Zelle/Venmo...) fica de fora de propósito: pode ser pagamento a terceiros ou
    // renda de verdade.
    private static readonly HashSet<string> InternalDetailedCategories =
    [
        "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT",
        "TRANSFER_OUT_ACCOUNT_TRANSFER",
        "TRANSFER_IN_ACCOUNT_TRANSFER",
    ];

    public static bool IsInternalTransfer(string? detailedCategory) =>
        detailedCategory is not null && InternalDetailedCategories.Contains(detailedCategory);

    // Base pra tudo que soma "quanto eu gastei/recebi" (dashboard, orçamentos, totais,
    // detecção de assinaturas).
    public static IQueryable<PlaidTransaction> ExcludingInternalTransfers(this IQueryable<PlaidTransaction> query) =>
        query.Where(t => !t.IsInternalTransfer);
}
