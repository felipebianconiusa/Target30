using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Controllers;
using Target30.Api.Data;
using Target30.Api.Models;
using Target30.Api.Services;

namespace Target30.Api.Tests;

public class DatabaseBackupTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime, IDisposable
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "t30-backup-" + Guid.NewGuid().ToString("N"));

    public DatabaseBackupTests(Target30WebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static readonly DateTime Now = new(2026, 9, 19, 12, 30, 45, DateTimeKind.Utc);

    [Fact]
    public void File_names_round_trip_through_the_parser()
    {
        var name = DatabaseBackup.FileNameFor(Now);

        Assert.Equal("target30-20260919-123045.db", name);
        Assert.Equal(Now, DatabaseBackup.TryParseFileName(name));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("target30.db")]
    [InlineData("target30-oops.db")]
    [InlineData("other-20260919-123045.db")]
    public void The_parser_ignores_files_it_did_not_create(string name)
    {
        Assert.Null(DatabaseBackup.TryParseFileName(name));
    }

    [Fact]
    public void A_backup_is_due_when_there_is_none_or_the_last_one_is_older_than_the_interval()
    {
        var day = TimeSpan.FromHours(24);

        Assert.True(DatabaseBackup.IsDue(null, Now, day));
        Assert.True(DatabaseBackup.IsDue(Now.AddHours(-25), Now, day));
        Assert.False(DatabaseBackup.IsDue(Now.AddHours(-3), Now, day));
    }

    [Fact]
    public void Retention_keeps_the_newest_files_and_returns_the_rest_for_deletion()
    {
        var files = Enumerable.Range(0, 5).Select(i => new BackupFile($"f{i}", Now.AddDays(-i))).ToList();

        var doomed = DatabaseBackup.FilesToDelete(files, 3);

        Assert.Equal(["f3", "f4"], doomed.Select(f => f.Path));
    }

    [Fact]
    public async Task Creates_a_real_consistent_copy_and_prunes_only_its_own_files()
    {
        await _factory.SeedAsync(db => db.PlaidItems.Add(new PlaidItem
        {
            UserId = "u", ItemId = "item-1", AccessToken = "tok", InstitutionName = "Bank",
        }));
        Directory.CreateDirectory(_dir);
        var stranger = Path.Combine(_dir, "keep-me.txt");
        await File.WriteAllTextAsync(stranger, "not a backup");

        BackupFile first, second;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
            first = await DatabaseBackup.CreateAsync(db, _dir, Now.AddDays(-1));
            second = await DatabaseBackup.CreateAsync(db, _dir, Now);
        }

        using (var conn = new SqliteConnection($"Data Source={second.Path};Mode=ReadOnly;Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM PlaidItems WHERE ItemId = 'item-1'";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }

        DatabaseBackup.Prune(_dir, 1);

        Assert.False(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));
        Assert.True(File.Exists(stranger));
    }

    [Fact]
    public async Task The_status_endpoint_reports_the_backup_settings()
    {
        var client = _factory.CreateClient();

        var status = await client.GetFromJsonAsync<AutoBackupStatusDto>("/api/backup/auto-status", JsonDefaults.Options);

        Assert.True(status!.Enabled);
        Assert.Equal(14, status.KeepCount);
    }
}
