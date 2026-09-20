using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Controllers;
using Target30.Api.Data;
using Target30.Api.Import;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class CsvTransactionParserTests
{
    private static ParseResult Parse(string csv, AmountConvention c = AmountConvention.ExpenseNegative, DateFormatHint d = DateFormatHint.Auto) =>
        CsvTransactionParser.Parse(csv, c, d);

    [Fact]
    public void Reads_a_typical_bank_csv_where_spending_is_negative()
    {
        var result = Parse("Date,Description,Amount\n2026-09-01,Grocery Store,-52.30\n2026-09-02,Payroll,2000.00\n");

        Assert.Null(result.FatalError);
        Assert.Equal(2, result.Rows.Count);
        // Convenção do Plaid: gasto positivo, entrada negativa.
        Assert.Equal((new DateOnly(2026, 9, 1), "Grocery Store", 52.30m), (result.Rows[0].Date, result.Rows[0].Description, result.Rows[0].Amount));
        Assert.Equal(-2000m, result.Rows[1].Amount);
    }

    [Fact]
    public void Honors_the_expense_positive_convention_used_by_some_cards()
    {
        var result = Parse("Date,Description,Amount\n2026-09-01,Coffee,4.50\n", AmountConvention.ExpensePositive);

        Assert.Equal(4.50m, result.Rows[0].Amount);
    }

    [Fact]
    public void Reads_separate_debit_and_credit_columns()
    {
        var result = Parse("Posted Date,Payee,Debit,Credit\n09/03/2026,Rent,900.00,\n09/04/2026,Refund,,25.00\n");

        Assert.Equal([900m, -25m], result.Rows.Select(r => r.Amount));
    }

    [Fact]
    public void Understands_portuguese_headers_semicolons_and_decimal_commas()
    {
        var result = Parse("Data;Descrição;Valor\n15/09/2026;Mercado;-1.234,56\n", d: DateFormatHint.DayMonthYear);

        Assert.Equal((new DateOnly(2026, 9, 15), 1234.56m), (result.Rows[0].Date, result.Rows[0].Amount));
    }

    [Fact]
    public void Handles_quotes_embedded_commas_bom_crlf_and_blank_lines()
    {
        var csv = "﻿Date,Description,Amount\r\n2026-09-01,\"Smith, John \"\"JJ\"\"\",-10.00\r\n\r\n2026-09-02,Plain,-5\r\n";

        var result = Parse(csv);

        Assert.Equal(["Smith, John \"JJ\"", "Plain"], result.Rows.Select(r => r.Description));
    }

    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("1,234", 1234)]
    [InlineData("(45.10)", -45.10)]
    [InlineData("-7", -7)]
    [InlineData("R$ 89,90", 89.90)]
    [InlineData("1.234.567", 1234567)]
    public void Parses_amounts_in_common_formats(string raw, double expected)
    {
        Assert.True(CsvTransactionParser.TryParseAmount(raw, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Rejects_unparseable_amounts(string raw)
    {
        Assert.False(CsvTransactionParser.TryParseAmount(raw, out _));
    }

    [Theory]
    [InlineData("2026-09-19", DateFormatHint.Auto, 2026, 9, 19)]
    [InlineData("09/19/2026", DateFormatHint.Auto, 2026, 9, 19)]
    [InlineData("19/09/2026", DateFormatHint.Auto, 2026, 9, 19)]   // 19 só pode ser dia
    [InlineData("03/04/2026", DateFormatHint.MonthDayYear, 2026, 3, 4)]
    [InlineData("03/04/2026", DateFormatHint.DayMonthYear, 2026, 4, 3)]
    [InlineData("9/5/26", DateFormatHint.MonthDayYear, 2026, 9, 5)]
    [InlineData("2026-09-19 10:30:00", DateFormatHint.Auto, 2026, 9, 19)]
    public void Parses_dates(string raw, DateFormatHint hint, int y, int m, int d)
    {
        Assert.True(CsvTransactionParser.TryParseDate(raw, hint, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Theory]
    [InlineData("31/02/2026", DateFormatHint.DayMonthYear)]
    [InlineData("hello", DateFormatHint.Auto)]
    [InlineData("2026-13-01", DateFormatHint.Iso)]
    public void Rejects_impossible_dates(string raw, DateFormatHint hint)
    {
        Assert.False(CsvTransactionParser.TryParseDate(raw, hint, out _));
    }

    [Fact]
    public void Reports_bad_rows_without_dropping_the_good_ones()
    {
        var result = Parse("Date,Description,Amount\n2026-09-01,Ok,-1\nnot-a-date,Bad,-2\n2026-09-03,Also bad,xyz\n");

        Assert.Single(result.Rows);
        Assert.Equal([3, 4], result.Errors.Select(e => e.Line));
    }

    [Fact]
    public void Says_what_it_found_when_the_columns_are_not_recognized()
    {
        var result = Parse("Foo,Bar\n1,2\n");

        Assert.Contains("Foo, Bar", result.FatalError);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void An_empty_file_is_a_fatal_error()
    {
        Assert.NotNull(Parse("   \n").FatalError);
    }
}

public class TransactionImportPlannerTests
{
    private static ParsedRow Row(int line, string date, string desc, decimal amount) =>
        new(line, DateOnly.Parse(date), desc, amount);

    private static PlaidTransaction Existing(int id, string date, string name, decimal amount, string? merchant = null, string? plaidId = null) => new()
    {
        Id = id, PlaidTransactionId = plaidId ?? $"plaid-{id}", Date = DateOnly.Parse(date), Name = name, MerchantName = merchant, Amount = amount,
    };

    [Fact]
    public void Ids_are_stable_so_reimporting_the_same_file_finds_everything_already_there()
    {
        var rows = new[] { Row(2, "2026-09-01", "Coffee", 4.5m), Row(3, "2026-09-01", "Coffee", 4.5m) };
        var first = TransactionImportPlanner.Plan(rows, [], "acc");

        Assert.All(first, p => Assert.False(p.Duplicate));
        Assert.NotEqual(first[0].TransactionId, first[1].TransactionId); // duas compras iguais no mesmo dia

        var existing = first.Select((p, i) => Existing(i + 1, "2026-09-01", "Coffee", 4.5m, plaidId: p.TransactionId)).ToList();
        var second = TransactionImportPlanner.Plan(rows, existing, "acc");

        Assert.All(second, p => Assert.True(p.Duplicate));
    }

    [Fact]
    public void A_row_matching_a_plaid_transaction_by_date_amount_and_similar_text_is_a_duplicate()
    {
        var existing = new[] { Existing(1, "2026-09-01", "Costco Wholesale #123", 52.30m, merchant: "Costco") };

        var plan = TransactionImportPlanner.Plan([Row(2, "2026-09-01", "COSTCO WHSE", 52.30m)], existing, "acc");

        Assert.True(plan[0].Duplicate);
    }

    [Fact]
    public void Same_amount_on_the_same_day_but_different_merchant_is_new()
    {
        var existing = new[] { Existing(1, "2026-09-01", "Costco", 20m) };

        var plan = TransactionImportPlanner.Plan([Row(2, "2026-09-01", "Shell Gas", 20m)], existing, "acc");

        Assert.False(plan[0].Duplicate);
    }

    [Fact]
    public void One_existing_transaction_only_covers_one_row_of_the_file()
    {
        var existing = new[] { Existing(1, "2026-09-01", "Coffee Shop", 4.5m) };

        var plan = TransactionImportPlanner.Plan(
            [Row(2, "2026-09-01", "Coffee Shop", 4.5m), Row(3, "2026-09-01", "Coffee Shop", 4.5m)], existing, "acc");

        Assert.Equal([true, false], plan.Select(p => p.Duplicate));
    }
}

public class ImportEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private const string Csv = "Date,Description,Amount\n2026-09-01,Grocery Store,-52.30\n2026-09-02,Payroll,2000.00\n";

    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ImportEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task SeedAccount(string id = "acc", string userId = TestAuthHandler.TestUserId) =>
        _factory.SeedAsync(db => db.PlaidAccounts.Add(new PlaidAccount
        {
            UserId = userId, ItemId = "plaid-item", AccountId = id, Name = "Checking", Type = "Depository", InstitutionName = "Bank",
        }));

    private Task<HttpResponseMessage> Post(string path, string csv = Csv, string accountId = "acc", string convention = "expense_negative", string? dateFormat = null) =>
        _client.PostAsJsonAsync($"/api/import/{path}", new ImportRequest(csv, accountId, convention, dateFormat), JsonDefaults.Options);

    private int Count(string accountId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<Target30DbContext>().PlaidTransactions.Count(t => t.AccountId == accountId);
    }

    [Fact]
    public async Task Preview_reports_new_rows_without_writing_anything()
    {
        await SeedAccount();

        var preview = await (await Post("preview")).Content.ReadFromJsonAsync<ImportPreviewDto>(JsonDefaults.Options);

        Assert.Equal((2, 0, 0), (preview!.NewCount, preview.DuplicateCount, preview.ErrorCount));
        Assert.Equal(0, Count("acc"));
    }

    [Fact]
    public async Task Commit_imports_with_the_plaid_sign_convention_and_the_accounts_institution()
    {
        await SeedAccount();

        var result = await (await Post("commit")).Content.ReadFromJsonAsync<ImportResultDto>(JsonDefaults.Options);

        Assert.Equal(2, result!.Imported);
        using var scope = _factory.Services.CreateScope();
        var txs = scope.ServiceProvider.GetRequiredService<Target30DbContext>().PlaidTransactions.OrderBy(t => t.Date).ToList();
        Assert.Equal([52.30m, -2000m], txs.Select(t => t.Amount));
        Assert.All(txs, t => Assert.Equal(("Bank", "plaid-item"), (t.InstitutionName, t.ItemId)));
        Assert.All(txs, t => Assert.StartsWith("csv:", t.PlaidTransactionId));
    }

    [Fact]
    public async Task Importing_the_same_file_twice_adds_nothing_the_second_time()
    {
        await SeedAccount();
        await Post("commit");

        var again = await (await Post("commit")).Content.ReadFromJsonAsync<ImportResultDto>(JsonDefaults.Options);

        Assert.Equal((0, 2), (again!.Imported, again.Duplicates));
        Assert.Equal(2, Count("acc"));
    }

    [Fact]
    public async Task A_category_rule_is_applied_to_imported_transactions()
    {
        await SeedAccount();
        await _factory.SeedAsync(db => db.CategoryRules.Add(new CategoryRule
        {
            UserId = TestAuthHandler.TestUserId, MerchantKey = "grocery store", Category = "FOOD_AND_DRINK",
        }));

        await Post("commit");

        using var scope = _factory.Services.CreateScope();
        var grocery = scope.ServiceProvider.GetRequiredService<Target30DbContext>().PlaidTransactions.Single(t => t.Name == "Grocery Store");
        Assert.Equal("FOOD_AND_DRINK", grocery.UserCategory);
    }

    [Fact]
    public async Task Cannot_import_into_another_users_account_or_an_unknown_one()
    {
        await SeedAccount("theirs", userId: "someone-else");

        Assert.Equal(HttpStatusCode.NotFound, (await Post("commit", accountId: "theirs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post("preview", accountId: "nope")).StatusCode);
        Assert.Equal(0, Count("theirs"));
    }

    [Fact]
    public async Task An_unreadable_file_returns_a_clear_error()
    {
        await SeedAccount();

        var response = await Post("preview", csv: "Foo,Bar\n1,2\n");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Foo, Bar", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("preview", csv: "")).StatusCode);
    }

    [Fact]
    public async Task Manual_accounts_can_be_created_listed_and_imported_into()
    {
        var created = await (await _client.PostAsJsonAsync("/api/import/accounts",
            new ManualAccountRequest("Nubank", "Credit", "Nubank", null, 3000m), JsonDefaults.Options))
            .Content.ReadFromJsonAsync<ImportAccountDto>(JsonDefaults.Options);

        Assert.True(created!.IsManual);
        var list = await _client.GetFromJsonAsync<List<ImportAccountDto>>("/api/import/accounts", JsonDefaults.Options);
        Assert.Contains(list!, a => a.AccountId == created.AccountId && a.IsManual);

        var result = await (await Post("commit", accountId: created.AccountId)).Content.ReadFromJsonAsync<ImportResultDto>(JsonDefaults.Options);
        Assert.Equal(2, result!.Imported);
    }

    [Fact]
    public async Task Manual_account_creation_validates_the_input()
    {
        var noName = await _client.PostAsJsonAsync("/api/import/accounts", new ManualAccountRequest(" ", "Credit", null, null, null), JsonDefaults.Options);
        var badType = await _client.PostAsJsonAsync("/api/import/accounts", new ManualAccountRequest("X", "Investment", null, null, null), JsonDefaults.Options);

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
    }
}
