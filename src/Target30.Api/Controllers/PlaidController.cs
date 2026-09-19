using System.Security.Claims;
using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;
using Target30.Api.Services;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PlaidController : ControllerBase
{
    private readonly PlaidClient _client;
    private readonly Target30DbContext _db;
    private readonly PlaidSyncService _sync;

    public PlaidController(PlaidClient client, Target30DbContext db, PlaidSyncService sync)
    {
        _client = client;
        _db = db;
        _sync = sync;
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
            // Transactions é obrigatório pra qualquer instituição. Liabilities só faz sentido
            // pra cartão de crédito — como "required" ele bloqueava a conexão de QUALQUER banco
            // que não suporte liabilities (ex.: OnePay, uma conta corrente). Com
            // RequiredIfSupportedProducts, o Plaid ainda pede consentimento de liabilities nos
            // bancos que suportam, mas não impede conectar os que não suportam.
            Products = [Products.Transactions],
            RequiredIfSupportedProducts = [Products.Liabilities],
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
        await _sync.SyncItemAsync(item);

        return Ok(new { itemId = item.ItemId });
    }

    // Lista as contas/instituições já conectadas pelo usuário logado (sem expor o access_token)
    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var items = await _db.PlaidItems
            .Where(i => i.UserId == CurrentUserId)
            .OrderByDescending(i => i.ConnectedAt)
            .Select(i => new { i.ItemId, i.InstitutionName, i.ConnectedAt, i.LastSyncedAt })
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
        var accounts = _db.PlaidAccounts.Where(a => a.ItemId == itemId && a.UserId == CurrentUserId);
        _db.PlaidAccounts.RemoveRange(accounts);
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
            await _sync.SyncItemAsync(item);

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

    // Transações de todas as contas conectadas pelo usuário logado, paginadas e filtradas no
    // servidor (o front nunca busca "tudo" pra paginar do lado dele).
    [HttpGet("transactions")]
    public async Task<IActionResult> GetAllTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] string? categories = null,
        [FromQuery] string? institutions = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = BuildFilteredQuery(search, categories, institutions, dateFrom, dateTo);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            items = items.Select(ToDto),
            total,
            page,
            pageSize,
        });
    }

    // Totais de entrada/saída para o mesmo conjunto de filtros da listagem acima — calculado
    // no banco sobre todas as linhas que batem com o filtro, não só a página atual.
    [HttpGet("transactions/totals")]
    public async Task<IActionResult> GetTransactionTotals(
        [FromQuery] string? search = null,
        [FromQuery] string? categories = null,
        [FromQuery] string? institutions = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null)
    {
        var query = BuildFilteredQuery(search, categories, institutions, dateFrom, dateTo);

        var totalExpenses = await query.Where(t => t.Amount > 0).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var totalIncome = -(await query.Where(t => t.Amount < 0).SumAsync(t => (decimal?)t.Amount) ?? 0m);

        return Ok(new { totalIncome, totalExpenses });
    }

    private IQueryable<PlaidTransaction> BuildFilteredQuery(
        string? search, string? categories, string? institutions, string? dateFrom, string? dateTo)
    {
        var query = _db.PlaidTransactions.Where(t => t.UserId == CurrentUserId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // EF.Functions.Like em vez de .Contains(): no SQLite, Contains() vira instr() e é
            // case-sensitive; LIKE é case-insensitive para ASCII, que é o que queremos aqui.
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.Name, pattern) ||
                (t.MerchantName != null && EF.Functions.Like(t.MerchantName, pattern)));
        }

        var categoryList = SplitParam(categories);
        if (categoryList.Length > 0)
            query = query.Where(t => categoryList.Contains(t.Category ?? CategoryFallback));

        var institutionList = SplitParam(institutions);
        if (institutionList.Length > 0)
            query = query.Where(t => t.InstitutionName != null && institutionList.Contains(t.InstitutionName));

        if (DateOnly.TryParse(dateFrom, out var from))
            query = query.Where(t => t.Date >= from);
        if (DateOnly.TryParse(dateTo, out var to))
            query = query.Where(t => t.Date <= to);

        return query;
    }

    // Resumo agregado (receitas, despesas, gastos por categoria, transações recentes) —
    // calculado no banco, sem trazer o histórico inteiro pro servidor nem pro cliente.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var query = _db.PlaidTransactions.Where(t => t.UserId == CurrentUserId);

        var totalExpenses = await query.Where(t => t.Amount > 0).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var totalIncome = -(await query.Where(t => t.Amount < 0).SumAsync(t => (decimal?)t.Amount) ?? 0m);

        var categoryTotals = await query
            .Where(t => t.Amount > 0)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(t => t.Amount) })
            .OrderByDescending(g => g.Total)
            .Take(6)
            .ToListAsync();

        var recent = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Take(8)
            .ToListAsync();

        return Ok(new
        {
            totalIncome,
            totalExpenses,
            categoryTotals = categoryTotals.Select(c => new { category = c.Category ?? CategoryFallback, total = c.Total }),
            recentTransactions = recent.Select(ToDto),
        });
    }

    private const string CategoryFallback = "OUTROS";

    private static string[] SplitParam(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
