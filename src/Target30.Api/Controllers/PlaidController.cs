using System.Security.Claims;
using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PlaidController : ControllerBase
{
    private readonly PlaidClient _client;
    private readonly Target30DbContext _db;

    public PlaidController(PlaidClient client, Target30DbContext db)
    {
        _client = client;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Passo 1: o frontend chama isso para obter um link_token e abrir o Plaid Link
    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkToken()
    {
        var response = await _client.LinkTokenCreateAsync(new LinkTokenCreateRequest
        {
            User = new LinkTokenCreateRequestUser
            {
                ClientUserId = CurrentUserId,
            },
            ClientName = "Target30",
            Products = [Products.Transactions],
            CountryCodes = [CountryCode.Us],
            Language = Language.English,
        });

        if (response.Error is not null)
            return Problem(response.Error.ErrorMessage);

        return Ok(new { linkToken = response.LinkToken });
    }

    // Passo 2: depois que o usuário conecta a conta no Plaid Link, o frontend manda o public_token aqui.
    // O access_token nunca volta pro frontend — fica só persistido no banco.
    [HttpPost("exchange-token")]
    public async Task<IActionResult> ExchangePublicToken([FromBody] ExchangeTokenRequest request)
    {
        var response = await _client.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest
        {
            PublicToken = request.PublicToken,
        });

        if (response.Error is not null)
            return Problem(response.Error.ErrorMessage);

        var item = new PlaidItem
        {
            UserId = CurrentUserId,
            ItemId = response.ItemId,
            AccessToken = response.AccessToken,
            InstitutionName = request.InstitutionName,
        };
        _db.PlaidItems.Add(item);
        await _db.SaveChangesAsync();

        // Primeira carga: já traz o histórico de transações pro banco local.
        await SyncItemAsync(item);

        return Ok(new { itemId = item.ItemId });
    }

    // Lista as contas/instituições já conectadas pelo usuário logado (sem expor o access_token)
    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var items = await _db.PlaidItems
            .Where(i => i.UserId == CurrentUserId)
            .OrderByDescending(i => i.ConnectedAt)
            .Select(i => new { i.ItemId, i.InstitutionName, i.ConnectedAt })
            .ToListAsync();

        return Ok(items);
    }

    // Desconecta uma conta: remove o item no Plaid e apaga os dados locais (item + transações)
    [HttpDelete("items/{itemId}")]
    public async Task<IActionResult> RemoveItem(string itemId)
    {
        var item = await _db.PlaidItems.FirstOrDefaultAsync(i => i.ItemId == itemId && i.UserId == CurrentUserId);
        if (item is null)
            return NotFound();

        try
        {
            await _client.ItemRemoveAsync(new ItemRemoveRequest { AccessToken = item.AccessToken });
        }
        catch
        {
            // Mesmo se o Plaid recusar (ex.: item já inválido), ainda removemos localmente.
        }

        var transactions = _db.PlaidTransactions.Where(t => t.ItemId == itemId && t.UserId == CurrentUserId);
        _db.PlaidTransactions.RemoveRange(transactions);
        _db.PlaidItems.Remove(item);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Busca com o Plaid o que mudou desde a última sincronização de cada item do usuário
    // (usa o cursor salvo — não rebaixa o histórico inteiro toda vez).
    [HttpPost("sync")]
    public async Task<IActionResult> SyncAll()
    {
        var items = await _db.PlaidItems.Where(i => i.UserId == CurrentUserId).ToListAsync();
        foreach (var item in items)
            await SyncItemAsync(item);

        return Ok();
    }

    // Transações de um item específico, lidas do banco local (rápido, sem chamar o Plaid)
    [HttpGet("items/{itemId}/transactions")]
    public async Task<IActionResult> GetTransactions(string itemId)
    {
        var itemExists = await _db.PlaidItems.AnyAsync(i => i.ItemId == itemId && i.UserId == CurrentUserId);
        if (!itemExists)
            return NotFound();

        var transactions = await _db.PlaidTransactions
            .Where(t => t.ItemId == itemId && t.UserId == CurrentUserId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Ok(transactions.Select(ToDto));
    }

    // Transações de todas as contas conectadas pelo usuário logado, lidas do banco local
    [HttpGet("transactions")]
    public async Task<IActionResult> GetAllTransactions()
    {
        var transactions = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return Ok(transactions.Select(ToDto));
    }

    private async Task SyncItemAsync(PlaidItem item)
    {
        var cursor = item.NextCursor;
        var hasMore = true;

        while (hasMore)
        {
            var response = await _client.TransactionsSyncAsync(new TransactionsSyncRequest
            {
                AccessToken = item.AccessToken,
                Cursor = cursor,
            });

            if (response.Error is not null)
                return; // item com erro (ex.: precisa reconectar) — não trava o sync dos outros

            await UpsertAsync(item, response.Added);
            await UpsertAsync(item, response.Modified);
            await RemoveAsync(response.Removed);

            cursor = response.NextCursor;
            hasMore = response.HasMore;
        }

        item.NextCursor = cursor;
        await _db.SaveChangesAsync();
    }

#pragma warning disable CS0612 // Category/Name legados usados como fallback
    private async Task UpsertAsync(PlaidItem item, IReadOnlyList<Transaction> transactions)
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

    private static TransactionDto ToDto(PlaidTransaction t) => new(
        t.PlaidTransactionId,
        t.AccountId,
        t.ItemId,
        t.InstitutionName,
        t.Amount,
        t.IsoCurrencyCode,
        t.Date,
        t.Name,
        t.MerchantName,
        t.Pending,
        t.Category
    );
}

public record ExchangeTokenRequest(string PublicToken, string? InstitutionName);
