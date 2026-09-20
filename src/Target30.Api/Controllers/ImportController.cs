using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Import;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    public const string ManualItemId = "manual";
    private const int MaxCsvChars = 2_000_000;

    private readonly Target30DbContext _db;

    public ImportController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Contas onde dá pra importar: as do Plaid e as manuais.
    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts()
    {
        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId)
            .OrderBy(a => a.InstitutionName).ThenBy(a => a.Name)
            .ToListAsync();
        return Ok(accounts.Select(a => new ImportAccountDto(
            a.AccountId, a.Name, a.Nickname, a.InstitutionName, a.Type, a.ItemId == ManualItemId)));
    }

    // Conta manual: pra banco/cartão que o Plaid não cobre. Nunca é tocada pelo sync.
    [HttpPost("accounts")]
    public async Task<IActionResult> CreateManualAccount([FromBody] ManualAccountRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80 || request.Type is not ("Depository" or "Credit"))
            return BadRequest(new { code = "invalid_account" });

        var account = new PlaidAccount
        {
            UserId = CurrentUserId,
            ItemId = ManualItemId,
            AccountId = $"manual-{Guid.NewGuid():N}",
            Name = name,
            InstitutionName = string.IsNullOrWhiteSpace(request.InstitutionName) ? "Manual" : request.InstitutionName.Trim()[..Math.Min(request.InstitutionName.Trim().Length, 60)],
            Type = request.Type,
            CurrentBalance = request.CurrentBalance,
            ManualCreditLimit = request.Type == "Credit" ? request.CreditLimit : null,
            IsoCurrencyCode = "USD",
        };
        _db.PlaidAccounts.Add(account);
        await _db.SaveChangesAsync();
        return Ok(new ImportAccountDto(account.AccountId, account.Name, null, account.InstitutionName, account.Type, true));
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] ImportRequest request)
    {
        var (plan, parse, error) = await BuildPlanAsync(request);
        if (error is not null)
            return error;

        return Ok(new ImportPreviewDto(
            plan!.Take(50).Select(p => new ImportRowDto(p.Row.Line, p.Row.Date, p.Row.Description, p.Row.Amount, p.Duplicate)).ToList(),
            parse!.Errors.Take(50).Select(e => new ImportErrorDto(e.Line, e.Message)).ToList(),
            plan!.Count(p => !p.Duplicate),
            plan!.Count(p => p.Duplicate),
            parse.Errors.Count));
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromBody] ImportRequest request)
    {
        var (plan, parse, error) = await BuildPlanAsync(request);
        if (error is not null)
            return error;

        var account = await _db.PlaidAccounts.FirstAsync(a => a.AccountId == request.AccountId && a.UserId == CurrentUserId);
        var rules = await _db.CategoryRules.Where(r => r.UserId == CurrentUserId).ToDictionaryAsync(r => r.MerchantKey, r => r.Category);

        var fresh = plan!.Where(p => !p.Duplicate).ToList();
        foreach (var p in fresh)
        {
            _db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = CurrentUserId,
                PlaidTransactionId = p.TransactionId,
                AccountId = account.AccountId,
                ItemId = account.ItemId,
                InstitutionName = account.InstitutionName,
                Amount = p.Row.Amount,
                IsoCurrencyCode = account.IsoCurrencyCode ?? "USD",
                Date = p.Row.Date,
                Name = p.Row.Description,
                Pending = false,
                UserCategory = MerchantKey.RuleFor(rules, null, p.Row.Description),
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new ImportResultDto(fresh.Count, plan!.Count - fresh.Count, parse!.Errors.Count));
    }

    private async Task<(List<PlannedRow>? Plan, ParseResult? Parse, IActionResult? Error)> BuildPlanAsync(ImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Csv) || request.Csv.Length > MaxCsvChars)
            return (null, null, BadRequest(new { code = "invalid_file" }));

        var account = await _db.PlaidAccounts.FirstOrDefaultAsync(a => a.AccountId == request.AccountId && a.UserId == CurrentUserId);
        if (account is null)
            return (null, null, NotFound(new { code = "account_not_found" }));

        var convention = string.Equals(request.Convention, "expense_positive", StringComparison.OrdinalIgnoreCase)
            ? AmountConvention.ExpensePositive
            : AmountConvention.ExpenseNegative;
        var dateFormat = request.DateFormat?.ToLowerInvariant() switch
        {
            "iso" => DateFormatHint.Iso,
            "mdy" => DateFormatHint.MonthDayYear,
            "dmy" => DateFormatHint.DayMonthYear,
            _ => DateFormatHint.Auto,
        };

        var parse = CsvTransactionParser.Parse(request.Csv, convention, dateFormat);
        if (parse.FatalError is not null)
            return (null, null, BadRequest(new { code = "unreadable_file", message = parse.FatalError }));

        var existing = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId && t.AccountId == account.AccountId)
            .ToListAsync();
        return (TransactionImportPlanner.Plan(parse.Rows, existing, account.AccountId), parse, null);
    }
}

public record ImportAccountDto(string AccountId, string Name, string? Nickname, string? InstitutionName, string Type, bool IsManual);

public record ManualAccountRequest(string? Name, string? Type, string? InstitutionName, decimal? CurrentBalance, decimal? CreditLimit);

public record ImportRequest(string? Csv, string? AccountId, string? Convention, string? DateFormat);

public record ImportRowDto(int Line, DateOnly Date, string Description, decimal Amount, bool Duplicate);

public record ImportErrorDto(int Line, string Message);

public record ImportPreviewDto(
    IReadOnlyList<ImportRowDto> Rows, IReadOnlyList<ImportErrorDto> Errors, int NewCount, int DuplicateCount, int ErrorCount);

public record ImportResultDto(int Imported, int Duplicates, int Errors);
