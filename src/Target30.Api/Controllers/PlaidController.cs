using System.Security.Claims;
using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Target30.Api.Billing;
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
    private readonly BillingOptions _billing;

    public PlaidController(PlaidClient client, Target30DbContext db, PlaidSyncService sync, IOptions<BillingOptions> billing)
    {
        _billing = billing.Value;
        _client = client;
        _db = db;
        _sync = sync;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Cada banco conectado custa dinheiro no Plaid: com a cobrança ligada, há um teto por usuário
    // (o dono, em ExemptEmails, não tem).
    private async Task<bool> ReachedItemLimitAsync()
    {
        if (!_billing.Enabled || SubscriptionAccess.IsExempt(_billing, User.FindFirstValue(ClaimTypes.Email)))
            return false;

        return await _db.PlaidItems.CountAsync(i => i.UserId == CurrentUserId) >= _billing.MaxItemsPerUser;
    }

    // Passo 1: o frontend chama isso para obter um link_token e abrir o Plaid Link
    [RequiresSubscription]
    [HttpPost("link-token")]
    public async Task<IActionResult> CreateLinkToken()
    {
        if (await ReachedItemLimitAsync())
            return Conflict(new { code = "item_limit" });

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
    [RequiresSubscription]
    [HttpPost("exchange-token")]
    public async Task<IActionResult> ExchangePublicToken([FromBody] ExchangeTokenRequest request)
    {
        if (await ReachedItemLimitAsync())
            return Conflict(new { code = "item_limit" });

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

    // Quando o PLAID conseguiu buscar dados novos de cada banco pela última vez (diferente do
    // "último sync" do app, que só diz quando NÓS perguntamos ao Plaid). Serve pra saber se uma
    // transação que você fez agora ainda não chegou ao Plaid ou se o problema é nosso.
    // /item/get é gratuito.
    [HttpGet("items/freshness")]
    public async Task<IActionResult> GetFreshness()
    {
        var items = await _db.PlaidItems.Where(i => i.UserId == CurrentUserId).ToListAsync();

        var result = await Task.WhenAll(items.Select(async item =>
        {
            try
            {
                var r = await _client.ItemGetAsync(new ItemGetRequest { AccessToken = item.AccessToken });
                var tx = r.Status?.Transactions;
                var errorCode = r.Error?.ErrorCode ?? r.Item?.Error?.ErrorCode;
                var status = DataFreshness.Evaluate(tx?.LastSuccessfulUpdate, tx?.LastFailedUpdate, errorCode, DateTimeOffset.UtcNow);
                return new PlaidItemFreshnessDto(
                    item.ItemId, tx?.LastSuccessfulUpdate, tx?.LastFailedUpdate,
                    errorCode, item.LastRefreshRequestedAt, status.ToApiString(), item.InstitutionName);
            }
            catch
            {
                // Indicador informativo: se o Plaid não responder, só fica sem a informação.
                return new PlaidItemFreshnessDto(item.ItemId, null, null, null, item.LastRefreshRequestedAt, "ok", item.InstitutionName);
            }
        }));

        return Ok(result);
    }

    // "Atualizar agora": pede ao Plaid pra buscar no banco AGORA (/transactions/refresh — COBRADO
    // por chamada), espera um pouco o Plaid terminar e já sincroniza. Protegido por um
    // intervalo mínimo entre pedidos (PlaidRefreshGuard) pra clique repetido não custar dinheiro.
    [RequiresSubscription]
    [HttpPost("items/{itemId}/refresh")]
    public async Task<IActionResult> RefreshItem(string itemId, CancellationToken cancellationToken)
    {
        var item = await _db.PlaidItems.FirstOrDefaultAsync(i => i.ItemId == itemId && i.UserId == CurrentUserId);
        if (item is null)
            return NotFound();

        var remaining = PlaidRefreshGuard.RemainingCooldown(item.LastRefreshRequestedAt, DateTime.UtcNow);
        if (remaining is { } wait)
        {
            var seconds = (int)Math.Ceiling(wait.TotalSeconds);
            Response.Headers.RetryAfter = seconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { retryAfterSeconds = seconds });
        }

        var before = await GetLastSuccessfulUpdateAsync(item);

        var refresh = await _client.TransactionsRefreshAsync(new TransactionsRefreshRequest
        {
            AccessToken = item.AccessToken,
        });
        if (refresh.Error is not null)
            return Problem(refresh.Error.ErrorMessage);

        item.LastRefreshRequestedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // O refresh é assíncrono no Plaid (sem webhook aqui): espera até ~30s a atualização
        // avançar. Se não avançar, o sync automático pega quando o Plaid terminar.
        DateTimeOffset? current = before;
        var updated = false;
        for (var attempt = 0; attempt < 10 && !updated; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            current = await GetLastSuccessfulUpdateAsync(item);
            updated = current is not null && (before is null || current > before);
        }

        await _sync.SyncItemAsync(item);

        return Ok(new PlaidRefreshResultDto(updated, current));
    }

    private async Task<DateTimeOffset?> GetLastSuccessfulUpdateAsync(PlaidItem item)
    {
        var r = await _client.ItemGetAsync(new ItemGetRequest { AccessToken = item.AccessToken });
        return r.Status?.Transactions?.LastSuccessfulUpdate;
    }
    // Donos já usados nas contas/cartões (pro filtro de transações).
    [HttpGet("owners")]
    public async Task<IActionResult> GetOwners()
    {
        var owners = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Owner != null)
            .Select(a => a.Owner!)
            .Distinct()
            .OrderBy(o => o)
            .ToListAsync();
        return Ok(owners);
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
    [RequiresSubscription]
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
        [FromQuery] string? owners = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = BuildFilteredQuery(search, categories, institutions, owners, dateFrom, dateTo);

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
        [FromQuery] string? owners = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null)
    {
        // A lista mostra tudo, mas os totais ignoram pagamento de fatura/transferência entre contas
        // (senão o mesmo dinheiro conta duas vezes: saída na corrente e "entrada" no cartão).
        var query = BuildFilteredQuery(search, categories, institutions, owners, dateFrom, dateTo).ExcludingInternalTransfers();

        var totalExpenses = await query.Where(t => t.Amount > 0).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var totalIncome = -(await query.Where(t => t.Amount < 0).SumAsync(t => (decimal?)t.Amount) ?? 0m);

        return Ok(new { totalIncome, totalExpenses });
    }

    private IQueryable<PlaidTransaction> BuildFilteredQuery(
        string? search, string? categories, string? institutions, string? owners, string? dateFrom, string? dateTo)
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
            query = query.Where(t => categoryList.Contains((t.UserCategory ?? t.Category) ?? CategoryFallback));

        var institutionList = SplitParam(institutions);
        if (institutionList.Length > 0)
            query = query.Where(t => t.InstitutionName != null && institutionList.Contains(t.InstitutionName));

        // Dono da conta/cartão (PlaidAccount.Owner): filtra pelas contas desses donos.
        var ownerList = SplitParam(owners);
        if (ownerList.Length > 0)
        {
            var ownedAccountIds = _db.PlaidAccounts
                .Where(a => a.UserId == CurrentUserId && a.Owner != null && ownerList.Contains(a.Owner))
                .Select(a => a.AccountId);
            query = query.Where(t => ownedAccountIds.Contains(t.AccountId));
        }

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
        var allQuery = _db.PlaidTransactions.Where(t => t.UserId == CurrentUserId);
        var query = allQuery.ExcludingInternalTransfers();

        var totalExpenses = await query.Where(t => t.Amount > 0).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var totalIncome = -(await query.Where(t => t.Amount < 0).SumAsync(t => (decimal?)t.Amount) ?? 0m);

        var categoryTotals = await query
            .Where(t => t.Amount > 0)
            .GroupBy(t => t.UserCategory ?? t.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(t => t.Amount) })
            .OrderByDescending(g => g.Total)
            .Take(6)
            .ToListAsync();

        var recent = await allQuery
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

    // Recategoriza uma transação manualmente — fica valendo pra sempre (sync nunca sobrescreve
    // UserCategory, só Category). Mandar category=null volta a usar a categoria do Plaid.
    [HttpPut("transactions/{transactionId}/category")]
    public async Task<IActionResult> UpdateTransactionCategory(string transactionId, [FromBody] UpdateCategoryRequest request)
    {
        var transaction = await _db.PlaidTransactions
            .FirstOrDefaultAsync(t => t.PlaidTransactionId == transactionId && t.UserId == CurrentUserId);
        if (transaction is null)
            return NotFound();

        var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category;
        transaction.UserCategory = category;

        // "Aplicar a todas do estabelecimento": cria/atualiza a regra (o sync aplica às próximas) e
        // recategoriza as que já existem. Sem categoria (voltar à do Plaid) remove a regra.
        var applied = 1;
        var key = MerchantKey.From(transaction.MerchantName, transaction.Name);
        if (request.ApplyToMerchant && key.Length > 0)
        {
            var rule = await _db.CategoryRules.FirstOrDefaultAsync(r => r.UserId == CurrentUserId && r.MerchantKey == key);
            if (category is null)
            {
                if (rule is not null)
                    _db.CategoryRules.Remove(rule);
            }
            else if (rule is null)
            {
                _db.CategoryRules.Add(new CategoryRule { UserId = CurrentUserId, MerchantKey = key, Category = category });
            }
            else
            {
                rule.Category = category;
            }

            var sameMerchant = (await _db.PlaidTransactions.Where(t => t.UserId == CurrentUserId).ToListAsync())
                .Where(t => MerchantKey.From(t.MerchantName, t.Name) == key)
                .ToList();
            foreach (var t in sameMerchant)
                t.UserCategory = category;
            applied = sameMerchant.Count;
        }

        await _db.SaveChangesAsync();
        Response.Headers["X-Applied-Count"] = applied.ToString();

        return Ok(ToDto(transaction));
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
        t.UserCategory ?? t.Category,
        t.UserCategory is not null,
        t.IsInternalTransfer
    );
}

public record ExchangeTokenRequest(string PublicToken, string? InstitutionName);

public record UpdateCategoryRequest(string? Category, bool ApplyToMerchant = false);

public record PlaidItemFreshnessDto(
    string ItemId,
    DateTimeOffset? PlaidLastSuccessfulUpdate,
    DateTimeOffset? PlaidLastFailedUpdate,
    string? ErrorCode,
    DateTime? LastRefreshRequestedAt,
    string Status = "ok",
    string? InstitutionName = null);

public record PlaidRefreshResultDto(bool Updated, DateTimeOffset? PlaidLastSuccessfulUpdate);
