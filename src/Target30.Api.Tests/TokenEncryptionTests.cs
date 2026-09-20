using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class TokenProtectorTests
{
    private static TokenProtector New() => new(new EphemeralDataProtectionProvider().CreateProtector("test"));

    [Fact]
    public void Round_trips_and_stores_something_that_is_not_the_token()
    {
        var protector = New();

        var stored = protector.Protect("access-production-secret");

        Assert.StartsWith(TokenProtector.Prefix, stored);
        Assert.DoesNotContain("secret", stored);
        Assert.Equal("access-production-secret", protector.Unprotect(stored));
    }

    [Fact]
    public void Protecting_twice_does_not_encrypt_twice()
    {
        var protector = New();
        var once = protector.Protect("tok");

        Assert.Equal(once, protector.Protect(once));
    }

    [Fact]
    public void A_legacy_plaintext_value_is_read_as_is()
    {
        Assert.Equal("access-old-plaintext", New().Unprotect("access-old-plaintext"));
    }

    [Fact]
    public void A_value_encrypted_with_another_key_cannot_be_read()
    {
        var stored = New().Protect("tok");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => New().Unprotect(stored));
    }
}

public class TokenEncryptionDatabaseTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;

    public TokenEncryptionDatabaseTests(Target30WebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> RawTokenAsync(string itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT AccessToken FROM PlaidItems WHERE ItemId = $id";
        var p = cmd.CreateParameter();
        p.ParameterName = "$id";
        p.Value = itemId;
        cmd.Parameters.Add(p);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task The_token_is_encrypted_in_the_database_but_read_back_in_plain_text()
    {
        await _factory.SeedAsync(db => db.PlaidItems.Add(new PlaidItem
        {
            UserId = "u", ItemId = "item-1", AccessToken = "access-production-abc", InstitutionName = "Bank",
        }));

        var raw = await RawTokenAsync("item-1");
        Assert.StartsWith("enc:v1:", raw);
        Assert.DoesNotContain("abc", raw);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
        Assert.Equal("access-production-abc", db.PlaidItems.Single(i => i.ItemId == "item-1").AccessToken);
    }

    [Fact]
    public async Task The_migration_encrypts_legacy_plaintext_tokens_and_leaves_the_rest_alone()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidItems.Add(new PlaidItem { UserId = "u", ItemId = "legacy", AccessToken = "placeholder", InstitutionName = "Old" });
            db.PlaidItems.Add(new PlaidItem { UserId = "u", ItemId = "modern", AccessToken = "access-modern", InstitutionName = "New" });
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
            // Simula o estado antigo do banco: token em texto puro, gravado direto por SQL.
            await db.Database.ExecuteSqlRawAsync("UPDATE PlaidItems SET AccessToken = 'access-legacy-plain' WHERE ItemId = 'legacy'");
        }
        Assert.Equal("access-legacy-plain", await RawTokenAsync("legacy"));
        var modernBefore = await RawTokenAsync("modern");

        int migrated;
        using (var scope = _factory.Services.CreateScope())
        {
            migrated = await TokenEncryptionMigration.RunAsync(scope.ServiceProvider.GetRequiredService<Target30DbContext>());
        }

        Assert.Equal(1, migrated);
        Assert.StartsWith("enc:v1:", await RawTokenAsync("legacy"));
        Assert.Equal(modernBefore, await RawTokenAsync("modern"));

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<Target30DbContext>();
        Assert.Equal("access-legacy-plain", db2.PlaidItems.Single(i => i.ItemId == "legacy").AccessToken);
        Assert.Equal(0, await TokenEncryptionMigration.RunAsync(db2));
    }
}
