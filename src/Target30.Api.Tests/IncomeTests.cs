using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class IncomeDetectorTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static List<PlaidTransaction> Inflows(decimal amount, params DateOnly[] dates) =>
        dates.Select((d, i) => new PlaidTransaction
        {
            UserId = "u", PlaidTransactionId = $"t{i}", AccountId = "a", ItemId = "i", Date = d, Amount = -amount, Name = "x",
        }).ToList();

    [Theory]
    [InlineData("Zelle payment from LAYSE SIMOES BIANCONI Conf# oguhieaea", "Zelle payment from LAYSE SIMOES BIANCONI")]
    [InlineData("Zelle payment from LAYSE SIMOES BIANCONI for \"Happy Dads Day\"; Conf# q", "Zelle payment from LAYSE SIMOES BIANCONI")]
    [InlineData("Felipe Bianconi DES:WISE ID:XXXXX23237 INDN:Felipe Bianconi CO ID:XXXXX3521 WEB", "Felipe Bianconi DES:WISE")]
    [InlineData("Payroll  Acme", "Payroll Acme")]
    public void Normalize_drops_the_parts_that_change_on_every_transaction(string raw, string expected)
    {
        Assert.Equal(expected, IncomeDetector.Normalize(raw));
    }

    [Fact]
    public void Detects_a_weekly_income()
    {
        var txs = Inflows(850m, new(2026, 8, 28), new(2026, 9, 4), new(2026, 9, 11), new(2026, 9, 18));

        var found = IncomeDetector.Detect("Zelle from Layse", txs, Today);

        Assert.Equal(IncomeFrequency.Weekly, found!.Frequency);
        Assert.Equal(850m, found.Amount);
        Assert.Equal(new DateOnly(2026, 9, 18), found.LastDate);
    }

    [Fact]
    public void Detects_a_monthly_income_even_when_the_day_drifts_a_little()
    {
        var txs = Inflows(4456m, new(2026, 7, 15), new(2026, 8, 14), new(2026, 9, 15));

        Assert.Equal(IncomeFrequency.Monthly, IncomeDetector.Detect("Wise", txs, Today)!.Frequency);
    }

    [Fact]
    public void Uses_the_median_of_recent_amounts_so_one_odd_value_does_not_distort_it()
    {
        var txs = Inflows(850m, new(2026, 9, 4), new(2026, 9, 11), new(2026, 9, 18));
        txs[1].Amount = -900m;

        Assert.Equal(850m, IncomeDetector.Detect("x", txs, Today)!.Amount);
    }

    [Fact]
    public void Needs_at_least_three_occurrences()
    {
        Assert.Null(IncomeDetector.Detect("x", Inflows(850m, new(2026, 9, 11), new(2026, 9, 18)), Today));
    }

    [Fact]
    public void Ignores_irregular_gaps()
    {
        Assert.Null(IncomeDetector.Detect("x", Inflows(500m, new(2026, 7, 1), new(2026, 7, 4), new(2026, 8, 20), new(2026, 9, 18)), Today));
    }

    [Fact]
    public void Ignores_an_income_that_stopped_showing_up()
    {
        var txs = Inflows(850m, new(2026, 6, 5), new(2026, 6, 12), new(2026, 6, 19));

        Assert.Null(IncomeDetector.Detect("x", txs, Today));
    }

    [Fact]
    public void Ignores_tiny_amounts_like_cashback()
    {
        Assert.Null(IncomeDetector.Detect("x", Inflows(10m, new(2026, 9, 4), new(2026, 9, 11), new(2026, 9, 18)), Today));
    }

    [Fact]
    public void Weekly_occurrences_continue_from_the_anchor_date()
    {
        var income = new RecurringIncome { Frequency = IncomeFrequency.Weekly, AnchorDate = new DateOnly(2026, 9, 18) };

        var dates = IncomeDetector.Occurrences(income, Today, Today.AddDays(20)).ToList();

        Assert.Equal([new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 9)], dates);
    }

    [Fact]
    public void Biweekly_occurrences_skip_every_other_week()
    {
        var income = new RecurringIncome { Frequency = IncomeFrequency.Biweekly, AnchorDate = new DateOnly(2026, 9, 4) };

        var dates = IncomeDetector.Occurrences(income, Today, Today.AddDays(30)).ToList();

        Assert.Equal([new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 16)], dates);
    }

    [Fact]
    public void Monthly_occurrences_use_the_day_of_the_anchor()
    {
        var income = new RecurringIncome { Frequency = IncomeFrequency.Monthly, AnchorDate = new DateOnly(2026, 9, 15) };

        var dates = IncomeDetector.Occurrences(income, Today, Today.AddDays(60)).ToList();

        Assert.Equal([new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 15)], dates);
    }
}

public class IncomeEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IncomeEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_list_and_delete_an_income()
    {
        var created = await (await _client.PostAsJsonAsync("/api/income",
            new IncomeRequest("Zelle from Layse", 850m, "weekly", new DateOnly(2026, 9, 18)), JsonDefaults.Options))
            .Content.ReadFromJsonAsync<IncomeDto>(JsonDefaults.Options);

        var list = await _client.GetFromJsonAsync<List<IncomeDto>>("/api/income", JsonDefaults.Options);
        Assert.Equal("Zelle from Layse", Assert.Single(list!).Description);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/income/{created!.Id}")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<List<IncomeDto>>("/api/income", JsonDefaults.Options))!);
    }

    [Fact]
    public async Task Create_rejects_an_invalid_frequency_or_amount()
    {
        var badFrequency = await _client.PostAsJsonAsync("/api/income",
            new IncomeRequest("x", 10m, "daily", new DateOnly(2026, 9, 18)), JsonDefaults.Options);
        var badAmount = await _client.PostAsJsonAsync("/api/income",
            new IncomeRequest("x", 0m, "weekly", new DateOnly(2026, 9, 18)), JsonDefaults.Options);

        Assert.Equal(HttpStatusCode.BadRequest, badFrequency.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badAmount.StatusCode);
    }

    [Fact]
    public async Task Cannot_delete_someone_elses_income()
    {
        await _factory.SeedAsync(db => db.RecurringIncomes.Add(new RecurringIncome
        {
            Id = 999, UserId = "someone-else", Description = "x", Amount = 10m, Frequency = "weekly", AnchorDate = new DateOnly(2026, 9, 18),
        }));

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/income/999")).StatusCode);
    }

    [Fact]
    public async Task Detected_suggests_a_weekly_zelle_and_leaves_out_the_ones_already_added()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "checking", Name = "Checking",
                Type = "Depository", CurrentBalance = 1000m,
            });
            for (var week = 1; week <= 4; week++)
                db.PlaidTransactions.Add(new PlaidTransaction
                {
                    UserId = TestAuthHandler.TestUserId, PlaidTransactionId = $"z{week}", AccountId = "checking", ItemId = "i",
                    Amount = -850m, Date = today.AddDays(-7 * week), Name = $"Zelle payment from LAYSE Conf# abc{week}",
                });
        });

        var detected = await _client.GetFromJsonAsync<List<DetectedIncome>>("/api/income/detected", JsonDefaults.Options);
        var suggestion = Assert.Single(detected!);
        Assert.Equal("Zelle payment from LAYSE", suggestion.Description);
        Assert.Equal("weekly", suggestion.Frequency);

        await _client.PostAsJsonAsync("/api/income",
            new IncomeRequest(suggestion.Description, suggestion.Amount, suggestion.Frequency, suggestion.LastDate), JsonDefaults.Options);
        Assert.Empty((await _client.GetFromJsonAsync<List<DetectedIncome>>("/api/income/detected", JsonDefaults.Options))!);
    }

    [Fact]
    public async Task CashFlow_projects_the_confirmed_income_as_money_in()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "checking", Name = "Checking",
                Type = "Depository", CurrentBalance = 100m,
            });
            db.RecurringIncomes.Add(new RecurringIncome
            {
                UserId = TestAuthHandler.TestUserId, Description = "Zelle from Layse", Amount = 850m,
                Frequency = "weekly", AnchorDate = today.AddDays(-3),
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow?pastDays=0&futureDays=14", JsonDefaults.Options);

        var incomes = response!.Entries.Where(e => e.Description == "Zelle from Layse").ToList();
        Assert.Equal(2, incomes.Count);
        Assert.All(incomes, e => Assert.Equal(850m, e.Amount));
        Assert.Equal(1800m, response.Entries[^1].Balance);
    }
}
