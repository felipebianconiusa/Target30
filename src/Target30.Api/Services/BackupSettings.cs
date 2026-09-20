namespace Target30.Api.Services;

public record BackupSettings(bool Enabled, string Directory, TimeSpan Interval, int KeepCount)
{
    public static BackupSettings From(IConfiguration configuration, string contentRoot)
    {
        var directory = configuration["Backup:Directory"];
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(contentRoot, "backups");

        var enabled = !bool.TryParse(configuration["Backup:Enabled"], out var e) || e;
        var hours = double.TryParse(configuration["Backup:IntervalHours"], out var h) && h > 0 ? h : 24;
        var keep = int.TryParse(configuration["Backup:KeepCount"], out var k) && k > 0 ? k : 14;
        return new BackupSettings(enabled, directory, TimeSpan.FromHours(hours), keep);
    }
}
