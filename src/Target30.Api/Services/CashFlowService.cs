using Microsoft.EntityFrameworkCore;
using Target30.Api.Controllers;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Services;

// Monta o fluxo de caixa (o que já aconteceu + o esperado) de um usuário. Compartilhado entre a
// tela de Fluxo de Caixa (CashFlowController) e o alerta de saldo baixo em background, pra os
// dois sempre enxergarem exatamente a mesma projeção.
public class CashFlowService
{
    private readonly Target30DbContext _db;

    public CashFlowService(Target30DbContext db)
    {
        _db = db;
    }

    public async Task<(decimal StartingBalance, decimal CurrentBalance, List<CashFlowEntryDto> Rows)> BuildRowsAsync(
        string userId, int pastDays, int futureDays, DateOnly? todayOverride = null)
    {
        pastDays = Math.Clamp(pastDays, 0, 365);
        futureDays = Math.Clamp(futureDays, 0, 365);

        var today = todayOverride ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-pastDays);
        var endDate = today.AddDays(futureDays);

        var currentBalance = await _db.PlaidAccounts
            .Where(a => a.UserId == userId && a.Type == "Depository")
            .SumAsync(a => (decimal?)a.CurrentBalance) ?? 0m;

        var entries = new List<(DateOnly Date, string Description, decimal Amount, string Status, int SortPriority)>();

        // Só movimentos de contas de depósito: o saldo acima soma apenas elas, então compra no
        // cartão de crédito não pode descontar daqui (ela só sai da conta quando a fatura é paga,
        // e isso já aparece como saída na própria conta ou como o lançamento "Fatura" projetado).
        var depositoryAccountIds = _db.PlaidAccounts
            .Where(a => a.UserId == userId && a.Type == "Depository")
            .Select(a => a.AccountId);
        var transactions = await _db.PlaidTransactions
            .Where(t => t.UserId == userId && t.Date >= startDate && t.Date <= today
                && depositoryAccountIds.Contains(t.AccountId))
            .ToListAsync();
        foreach (var t in transactions)
            entries.Add((t.Date, t.MerchantName ?? t.Name, -t.Amount, t.Pending ? "Pending" : "Done", 0));

        var bills = await _db.RecurringBills
            .Where(b => b.UserId == userId && b.IsActive)
            .ToListAsync();
        foreach (var bill in bills)
        {
            foreach (var date in DateMath.MonthlyOccurrences(bill.DayOfMonth, today, endDate))
                entries.Add((date, bill.Description, -bill.Amount, "Pending", 1));
        }

        // Entradas recorrentes que o usuário confirmou (ex.: Zelle semanal): só as futuras — as de
        // hoje pra trás já são transações reais.
        var incomes = await _db.RecurringIncomes
            .Where(i => i.UserId == userId && i.IsActive)
            .ToListAsync();
        foreach (var income in incomes)
        {
            foreach (var date in IncomeDetector.Occurrences(income, today, endDate))
                entries.Add((date, income.Description, income.Amount, "Pending", 1));
        }

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId)
            ?? new UserSettings { UserId = userId };
        var cards = await _db.PlaidAccounts
            .Where(a => a.UserId == userId && a.Type == "Credit")
            .ToListAsync();
        foreach (var card in cards)
        {
            var p = CardMath.Compute(card, settings.GlobalTargetUtilizationPercent, today);

            // Lançamento no fechamento: quanto pagar pra bater a meta de utilização — sempre
            // aparece (mesmo R$0, se já estiver dentro da meta) pra você saber a data mesmo
            // que não precise pagar nada. Reflete o saldo ATUAL do cartão, então atualiza a
            // cada gasto novo. Sem limite cadastrado ainda (p.UtilizationPercent null) não dá
            // pra calcular "quanto pagar pra bater a meta", então pulamos esse lançamento.
            var payBeforeClosing = 0m;
            if (p.UtilizationPercent is not null && p.PaymentDeadline is { } deadline && deadline > today && deadline <= endDate)
            {
                payBeforeClosing = p.AmountToPay;
                entries.Add((deadline, $"{card.DisplayName} - Fechamento", -payBeforeClosing, "Pending", 1));
            }

            // Lançamento no vencimento: o que sobra do saldo do cartão depois do pagamento
            // antecipado acima (o mesmo dinheiro não sai da conta duas vezes) — também sempre
            // aparece, mesmo R$0, pra marcar a data.
            if (card.EffectiveNextPaymentDueDate is { } dueDate && dueDate > today && dueDate <= endDate)
                entries.Add((dueDate, $"{card.DisplayName} - Fatura", -Math.Max(p.Balance - payBeforeClosing, 0m), "Pending", 1));
        }

        var ordered = entries.OrderBy(e => e.Date).ThenBy(e => e.SortPriority).ToList();

        // O saldo atual já reflete tudo que já aconteceu até hoje — pra desenhar a "trilha" a
        // partir do início da janela, caminhamos pra trás a partir dele.
        var netEffectUpToToday = ordered.Where(e => e.Date <= today).Sum(e => e.Amount);
        var runningBalance = currentBalance - netEffectUpToToday;
        var startingBalance = runningBalance;

        var rows = new List<CashFlowEntryDto>();
        foreach (var e in ordered)
        {
            var before = runningBalance;
            runningBalance += e.Amount;
            rows.Add(new CashFlowEntryDto(e.Date, e.Description, e.Amount, runningBalance, e.Status, before));
        }

        return (startingBalance, currentBalance, rows);
    }
}
