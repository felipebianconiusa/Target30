using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    private readonly Target30DbContext _db;

    public BackupController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Backup completo dos seus dados em JSON — nunca inclui o AccessToken do Plaid (é uma
    // credencial, não um dado seu) nem IDs internos do banco, só o que faz sentido pra você
    // ler ou reimportar em outro lugar.
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var items = await _db.PlaidItems
            .Where(i => i.UserId == CurrentUserId)
            .Select(i => new { i.ItemId, i.InstitutionName, i.ConnectedAt, i.LastSyncedAt })
            .ToListAsync();

        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId)
            .Select(a => new
            {
                a.AccountId,
                a.Name,
                a.OfficialName,
                a.InstitutionName,
                a.Type,
                a.Subtype,
                a.CurrentBalance,
                a.AvailableBalance,
                a.CreditLimit,
                a.ManualCreditLimit,
                a.IsoCurrencyCode,
                a.LastStatementBalance,
                a.LastStatementIssueDate,
                a.NextPaymentDueDate,
                a.ManualNextPaymentDueDate,
                a.MinimumPaymentAmount,
                a.IsOverdue,
                a.StatementClosingDay,
                a.TargetUtilizationPercent,
            })
            .ToListAsync();

        var transactions = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId)
            .OrderBy(t => t.Date)
            .Select(t => new
            {
                t.AccountId,
                t.InstitutionName,
                t.Amount,
                t.IsoCurrencyCode,
                t.Date,
                t.Name,
                t.MerchantName,
                t.Pending,
                t.Category,
            })
            .ToListAsync();

        var bills = await _db.RecurringBills
            .Where(b => b.UserId == CurrentUserId)
            .Select(b => new { b.Description, b.Amount, b.DayOfMonth, b.IsActive })
            .ToListAsync();

        var balanceHistory = await _db.CardBalanceSnapshots
            .Where(s => s.UserId == CurrentUserId)
            .OrderBy(s => s.Date)
            .Select(s => new { s.AccountId, s.Date, s.Balance, s.Limit, s.UtilizationPercent })
            .ToListAsync();

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);

        var backup = new
        {
            ExportedAt = DateTime.UtcNow,
            Settings = settings is null ? null : new
            {
                settings.GlobalTargetUtilizationPercent,
                settings.NotifyDaysBeforeClosing,
                settings.NotificationsEnabled,
                settings.WeeklyDigestEnabled,
            },
            Items = items,
            Accounts = accounts,
            Transactions = transactions,
            RecurringBills = bills,
            BalanceHistory = balanceHistory,
        };

        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"target30-backup-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.json";
        return File(bytes, "application/json", fileName);
    }
}
