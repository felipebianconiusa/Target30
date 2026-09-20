using System.Globalization;
using System.Security.Claims;
using System.Text;
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
public class CashFlowController : ControllerBase
{
    private readonly CashFlowService _cashFlow;
    private readonly Target30DbContext _db;

    public CashFlowController(CashFlowService cashFlow, Target30DbContext db)
    {
        _cashFlow = cashFlow;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Panorama estilo planilha: o que já aconteceu (Done/Pending do Plaid) + o que é esperado
    // (contas recorrentes + faturas de cartão a vencer), com saldo projetado dia a dia.
    // "Amount" aqui já vem com o sinal invertido do Plaid: positivo = entrada, negativo = saída
    // (assim bate com a leitura natural de uma planilha de fluxo de caixa).
    [HttpGet]
    public async Task<IActionResult> GetCashFlow([FromQuery] int pastDays = 30, [FromQuery] int futureDays = 45)
    {
        var (startingBalance, currentBalance, rows) = await BuildRowsAsync(pastDays, futureDays);

        var threshold = await _db.UserSettings
            .Where(s => s.UserId == CurrentUserId)
            .Select(s => (decimal?)s.LowBalanceThreshold)
            .FirstOrDefaultAsync() ?? 0m;
        var lowBalance = LowBalanceDetector.Find(rows, currentBalance, threshold, DateOnly.FromDateTime(DateTime.UtcNow));

        return Ok(new CashFlowResponseDto(startingBalance, currentBalance, rows, lowBalance, threshold));
    }

    // Mesmo cálculo da tela de Cash Flow, em CSV — pra baixar, guardar ou comparar com a
    // planilha antiga.
    [HttpGet("report")]
    public async Task<IActionResult> DownloadReport([FromQuery] int pastDays = 30, [FromQuery] int futureDays = 45)
    {
        var (_, _, rows) = await BuildRowsAsync(pastDays, futureDays);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", new[] { "Data", "Descricao", "SaldoAntes", "Valor", "SaldoDepois", "Situacao" }.Select(CsvField)));
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                CsvField(r.Date.ToString("yyyy-MM-dd")),
                CsvField(r.Description),
                CsvField(r.BalanceBefore.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(r.Amount.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(r.Balance.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(r.Status),
            }));
        }

        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return File(bytes, "text/csv", $"target30-fluxo-caixa-{today:yyyy-MM-dd}.csv");
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private Task<(decimal StartingBalance, decimal CurrentBalance, List<CashFlowEntryDto> Rows)> BuildRowsAsync(
        int pastDays, int futureDays) =>
        _cashFlow.BuildRowsAsync(CurrentUserId, pastDays, futureDays);
}
public record CashFlowEntryDto(DateOnly Date, string Description, decimal Amount, decimal Balance, string Status, decimal BalanceBefore);

public record CashFlowResponseDto(decimal StartingBalance, decimal CurrentBalance, IReadOnlyList<CashFlowEntryDto> Entries,
    LowBalanceWarning? LowBalance = null, decimal LowBalanceThreshold = 0m);
