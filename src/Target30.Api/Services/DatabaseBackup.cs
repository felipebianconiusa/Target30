using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;

namespace Target30.Api.Services;

public record BackupFile(string Path, DateTime CreatedUtc);

// Backup automático do banco SQLite (cópia consistente via VACUUM INTO, sem parar a API).
// ATENÇÃO: a cópia inclui os access tokens do Plaid, exatamente como o banco — trate a pasta de
// backup com o mesmo cuidado (fora do git, fora de pastas sincronizadas na nuvem).
public static class DatabaseBackup
{
    public const string FilePrefix = "target30-";
    public const string FileSuffix = ".db";

    public static string FileNameFor(DateTime utc) =>
        $"{FilePrefix}{utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{FileSuffix}";

    // Só reconhece arquivos que nós mesmos criamos (nome + extensão), nunca apaga o resto da pasta.
    public static DateTime? TryParseFileName(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix) || !fileName.EndsWith(FileSuffix))
            return null;

        var stamp = fileName[FilePrefix.Length..^FileSuffix.Length];
        return DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc
            : null;
    }

    public static bool IsDue(DateTime? lastBackupUtc, DateTime nowUtc, TimeSpan interval) =>
        lastBackupUtc is null || nowUtc - lastBackupUtc.Value >= interval;

    // Mantém os `keep` mais recentes; devolve o que deve ser apagado.
    public static IReadOnlyList<BackupFile> FilesToDelete(IEnumerable<BackupFile> files, int keep) =>
        files.OrderByDescending(f => f.CreatedUtc).Skip(Math.Max(keep, 1)).ToList();

    public static List<BackupFile> List(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory)
            .Select(path => (path, created: TryParseFileName(Path.GetFileName(path))))
            .Where(x => x.created is not null)
            .Select(x => new BackupFile(x.path, x.created!.Value))
            .OrderByDescending(f => f.CreatedUtc)
            .ToList();
    }

    public static async Task<BackupFile> CreateAsync(Target30DbContext db, string directory, DateTime nowUtc)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(Path.GetFullPath(directory), FileNameFor(nowUtc));
        if (File.Exists(path))
            File.Delete(path);

        // VACUUM INTO não aceita parâmetro: escapa a aspa simples do caminho.
        var escaped = path.Replace("'", "''");
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escaped}'");
        return new BackupFile(path, nowUtc);
    }

    public static void Prune(string directory, int keep)
    {
        foreach (var file in FilesToDelete(List(directory), keep))
            File.Delete(file.Path);
    }
}
