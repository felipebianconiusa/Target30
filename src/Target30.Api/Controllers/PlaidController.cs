using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

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

    // Passo 1: o frontend chama isso para obter um link_token e abrir o Plaid Link
    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkToken()
    {
        var response = await _client.LinkTokenCreateAsync(new LinkTokenCreateRequest
        {
            User = new LinkTokenCreateRequestUser
            {
                ClientUserId = "target30-user", // TODO: usar o id do usuário autenticado
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
            ItemId = response.ItemId,
            AccessToken = response.AccessToken,
            InstitutionName = request.InstitutionName,
        };
        _db.PlaidItems.Add(item);
        await _db.SaveChangesAsync();

        return Ok(new { itemId = item.ItemId });
    }

    // Lista as contas/instituições já conectadas (sem expor o access_token)
    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var items = await _db.PlaidItems
            .OrderByDescending(i => i.ConnectedAt)
            .Select(i => new { i.ItemId, i.InstitutionName, i.ConnectedAt })
            .ToListAsync();

        return Ok(items);
    }

    // Sincroniza transações de um item já conectado
    [HttpGet("items/{itemId}/transactions")]
    public async Task<IActionResult> GetTransactions(string itemId)
    {
        var item = await _db.PlaidItems.FirstOrDefaultAsync(i => i.ItemId == itemId);
        if (item is null)
            return NotFound();

        var response = await _client.TransactionsSyncAsync(new TransactionsSyncRequest
        {
            AccessToken = item.AccessToken,
        });

        if (response.Error is not null)
            return Problem(response.Error.ErrorMessage);

        return Ok(new
        {
            added = response.Added,
            modified = response.Modified,
            removed = response.Removed,
            hasMore = response.HasMore,
        });
    }
}

public record ExchangeTokenRequest(string PublicToken, string? InstitutionName);
