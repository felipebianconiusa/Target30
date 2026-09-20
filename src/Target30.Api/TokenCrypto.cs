using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;

namespace Target30.Api;

// Criptografa valores sensíveis (access tokens do Plaid) antes de irem pro banco. O texto
// gravado começa com "enc:v1:" — valor sem esse prefixo é legado em texto puro (lido normalmente
// e regravado criptografado por TokenEncryptionMigration).
public class TokenProtector
{
    public const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector;

    public TokenProtector(IDataProtector protector) => _protector = protector;

    public string Protect(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal) ? value : Prefix + _protector.Protect(value);

    public string Unprotect(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal) ? _protector.Unprotect(value[Prefix.Length..]) : value;
}

// Ponto único de acesso usado pelo conversor do EF. Sem protetor configurado (ferramentas de
// linha de comando, scripts) passa o valor sem mexer — nunca grava nada criptografado que não
// consiga ler de volta.
public static class TokenCrypto
{
    private static TokenProtector? _current;

    public static void Configure(TokenProtector? protector) => _current = protector;

    public static string Protect(string value) => _current?.Protect(value) ?? value;

    public static string Unprotect(string value) => _current?.Unprotect(value) ?? value;
}

public static class TokenEncryptionMigration
{
    // Regrava (criptografados) os tokens que ainda estão em texto puro. Devolve quantos.
    public static async Task<int> RunAsync(Target30DbContext db)
    {
        var plaintextIds = await db.Database
            .SqlQueryRaw<int>("SELECT Id AS Value FROM PlaidItems WHERE AccessToken NOT LIKE 'enc:v1:%'")
            .ToListAsync();
        if (plaintextIds.Count == 0)
            return 0;

        var items = await db.PlaidItems.Where(i => plaintextIds.Contains(i.Id)).ToListAsync();
        foreach (var item in items)
            db.Entry(item).Property(i => i.AccessToken).IsModified = true;
        await db.SaveChangesAsync();

        await ScrubOldPagesAsync(db);
        return items.Count;
    }

    // O SQLite não apaga o conteúdo antigo de uma linha atualizada: o token em texto puro continua
    // nas páginas livres do arquivo e no WAL até o arquivo ser reconstruído. Fecha o WAL no
    // arquivo principal e reconstrói (VACUUM) pra não sobrar o texto puro. Falhar aqui não
    // impede o app de subir.
    public static async Task ScrubOldPagesAsync(Target30DbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)");
            await db.Database.ExecuteSqlRawAsync("VACUUM");
        }
        catch (Exception)
        {
            // best effort
        }
    }
}
