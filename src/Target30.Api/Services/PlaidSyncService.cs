using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Entity;
using Going.Plaid.Liabilities;
using Going.Plaid.Transactions;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Services;

// Lógica de sincronização com o Plaid, compartilhada entre o PlaidController (sync disparado
// pelo usuário na tela) e o PlaidBackgroundService (sync automático periódico) — pra nunca
// divergir entre os dois caminhos.
public class PlaidSyncService
{
    private readonly PlaidClient _client;
    private readonly Target30DbContext _db;

    public PlaidSyncService(PlaidClient client, Target30DbContext db)
    {
        _client = client;
        _db = db;
    }

    public async Task SyncItemAsync(PlaidItem item)
    {
        var cursor = item.NextCursor;
        var hasMore = true;
        var rules = await _db.CategoryRules
            .Where(r => r.UserId == item.UserId)
            .ToDictionaryAsync(r => r.MerchantKey, r => r.Category);

        while (hasMore)
        {
            var response = await _client.TransactionsSyncAsync(new TransactionsSyncRequest
            {
                AccessToken = item.AccessToken,
                Cursor = cursor,
            });

            if (response.Error is not null)
                return; // item com erro (ex.: precisa reconectar) — não trava o sync dos outros

            await UpsertAsync(item, response.Added, rules);
            await UpsertAsync(item, response.Modified, rules);
            await RemoveAsync(response.Removed);

            cursor = response.NextCursor;
            hasMore = response.HasMore;
        }

        item.NextCursor = cursor;
        item.LastSyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await SyncAccountsAsync(item);
    }

    public async Task SyncAccountsAsync(PlaidItem item)
    {
        var accountsResponse = await _client.AccountsGetAsync(new AccountsGetRequest
        {
            AccessToken = item.AccessToken,
        });
        if (accountsResponse.Error is not null)
            return;

        // /liabilities/get só funciona se o Item tiver o produto Liabilities habilitado (itens
        // conectados antes desse produto existir vão falhar aqui — não deve travar o resto).
        var creditByAccountId = new Dictionary<string, CreditCardLiability>();
        var liabilitiesResponse = await _client.LiabilitiesGetAsync(new LiabilitiesGetRequest
        {
            AccessToken = item.AccessToken,
        });
        if (liabilitiesResponse.Error is null && liabilitiesResponse.Liabilities?.Credit is not null)
        {
            foreach (var credit in liabilitiesResponse.Liabilities.Credit)
                if (credit.AccountId is not null)
                    creditByAccountId[credit.AccountId] = credit;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var acc in accountsResponse.Accounts)
        {
            var existing = await _db.PlaidAccounts.FirstOrDefaultAsync(a => a.AccountId == acc.AccountId);
            if (existing is null)
            {
                existing = new PlaidAccount { AccountId = acc.AccountId };
                _db.PlaidAccounts.Add(existing);
            }

            existing.UserId = item.UserId;
            existing.ItemId = item.ItemId;
            existing.Name = acc.Name;
            existing.OfficialName = acc.OfficialName;
            existing.InstitutionName = item.InstitutionName;
            existing.Type = acc.Type.ToString();
            existing.Subtype = acc.Subtype?.ToString();
            existing.CurrentBalance = acc.Balances.Current;
            existing.AvailableBalance = acc.Balances.Available;
            existing.CreditLimit = acc.Balances.Limit;
            existing.IsoCurrencyCode = acc.Balances.IsoCurrencyCode;

            if (creditByAccountId.TryGetValue(acc.AccountId, out var credit))
            {
                existing.LastStatementBalance = credit.LastStatementBalance;
                existing.LastStatementIssueDate = credit.LastStatementIssueDate;
                existing.NextPaymentDueDate = credit.NextPaymentDueDate;
                existing.MinimumPaymentAmount = credit.MinimumPaymentAmount;
                existing.IsOverdue = credit.IsOverdue;

                // Sugere o dia de fechamento a partir do último extrato (só na 1ª vez — o
                // Plaid não informa a próxima data, então isso é só um ponto de partida
                // editável pelo usuário).
                if (existing.StatementClosingDay is null && credit.LastStatementIssueDate is not null)
                    existing.StatementClosingDay = credit.LastStatementIssueDate.Value.Day;
            }

            if (existing.Type == "Credit")
                await UpsertSnapshotAsync(existing, today);
        }

        await _db.SaveChangesAsync();
    }

    private async Task UpsertSnapshotAsync(PlaidAccount account, DateOnly today)
    {
        var snapshot = await _db.CardBalanceSnapshots
            .FirstOrDefaultAsync(s => s.AccountId == account.AccountId && s.Date == today);
        if (snapshot is null)
        {
            snapshot = new CardBalanceSnapshot { AccountId = account.AccountId, UserId = account.UserId, Date = today };
            _db.CardBalanceSnapshots.Add(snapshot);
        }

        var balance = account.CurrentBalance ?? 0m;
        var limit = account.EffectiveCreditLimit ?? account.CreditLimit;
        snapshot.Balance = balance;
        snapshot.Limit = limit;
        snapshot.UtilizationPercent = limit is > 0 ? Math.Round(balance / limit.Value * 100, 1) : null;
    }

#pragma warning disable CS0612 // Category/Name legados usados como fallback
    private async Task UpsertAsync(
        PlaidItem item, IReadOnlyList<Transaction> transactions, IReadOnlyDictionary<string, string> rules)
    {
        foreach (var t in transactions)
        {
            var transactionId = t.TransactionId ?? "";
            var existing = await _db.PlaidTransactions
                .FirstOrDefaultAsync(x => x.PlaidTransactionId == transactionId);

            if (existing is null)
            {
                existing = new PlaidTransaction { PlaidTransactionId = transactionId };
                _db.PlaidTransactions.Add(existing);
            }

            existing.UserId = item.UserId;
            existing.AccountId = t.AccountId ?? "";
            existing.ItemId = item.ItemId;
            existing.InstitutionName = item.InstitutionName;
            existing.Amount = t.Amount ?? 0m;
            existing.IsoCurrencyCode = t.IsoCurrencyCode;
            existing.Date = t.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            existing.Name = t.MerchantName ?? t.Name ?? "Transação sem descrição";
            existing.MerchantName = t.MerchantName;
            existing.Pending = t.Pending ?? false;
            existing.Category = t.PersonalFinanceCategory?.Primary ?? t.Category?.FirstOrDefault();
            existing.DetailedCategory = t.PersonalFinanceCategory?.Detailed;
            existing.IsInternalTransfer = TransactionClassifier.IsInternalTransfer(existing.DetailedCategory);

            // Regra por estabelecimento (só onde o usuário ainda não escolheu uma categoria à mão).
            if (existing.UserCategory is null)
                existing.UserCategory = MerchantKey.RuleFor(rules, existing.MerchantName, existing.Name);
        }
    }
#pragma warning restore CS0612

    private async Task RemoveAsync(IReadOnlyList<RemovedTransaction> removed)
    {
        foreach (var r in removed)
        {
            var existing = await _db.PlaidTransactions
                .FirstOrDefaultAsync(x => x.PlaidTransactionId == r.TransactionId);
            if (existing is not null)
                _db.PlaidTransactions.Remove(existing);
        }
    }
}
