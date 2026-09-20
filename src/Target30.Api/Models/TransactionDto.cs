namespace Target30.Api.Models;

public record TransactionDto(
    string TransactionId,
    string AccountId,
    string ItemId,
    string? InstitutionName,
    decimal Amount,
    string? IsoCurrencyCode,
    DateOnly Date,
    string Name,
    string? MerchantName,
    bool Pending,
    string? Category,
    bool IsCategoryCustom,
    bool IsInternalTransfer = false
);
