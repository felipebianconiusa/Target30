using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;
using Microsoft.AspNetCore.Mvc;

namespace Target30.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaidController : ControllerBase
{
    private readonly PlaidClient _client;

    public PlaidController(PlaidClient client)
    {
        _client = client;
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

    // Passo 2: depois que o usuário conecta a conta no Plaid Link, o frontend manda o public_token aqui
    [HttpPost("exchange-token")]
    public async Task<IActionResult> ExchangePublicToken([FromBody] ExchangeTokenRequest request)
    {
        var response = await _client.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest
        {
            PublicToken = request.PublicToken,
        });

        if (response.Error is not null)
            return Problem(response.Error.ErrorMessage);

        // TODO: persistir response.AccessToken associado ao usuário (nunca expor ao frontend)
        return Ok(new { itemId = response.ItemId });
    }

    // Lista transações de um item já conectado
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions([FromQuery] string accessToken)
    {
        var response = await _client.TransactionsSyncAsync(new TransactionsSyncRequest
        {
            AccessToken = accessToken,
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

public record ExchangeTokenRequest(string PublicToken);
