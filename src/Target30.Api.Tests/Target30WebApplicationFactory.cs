using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Target30.Api.Data;

namespace Target30.Api.Tests;

// Sobe a API inteira (mesmos controllers, mesmo pipeline) contra um SQLite em memória, com
// autenticação trocada por um usuário fixo — pra testar os controllers fim-a-fim sem tocar no
// banco real nem precisar de um login do Google de verdade.
public class Target30WebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    // O host de teste também roda o PlaidBackgroundService (com backup automático): aponta a pasta
    // de backup pra um diretório temporário pra não sujar (nem enganar) a pasta de backups do projeto.
    private readonly string _backupDirectory = Path.Combine(Path.GetTempPath(), "t30-factory-backups-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Backup:Directory", _backupDirectory);

        builder.ConfigureServices(services =>
        {
            _connection.Open();

            // O sync/alertas/backup em background não fazem parte dos testes de controller e
            // disputavam a mesma conexão SQLite em memória com o reset/seed de cada teste (falhas
            // intermitentes "no such table"). Os testes dessas peças chamam as classes direto.
            foreach (var hosted in services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(Target30.Api.Services.PlaidBackgroundService))
                .ToList())
                services.Remove(hosted);

            var dbContextOptions = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<Target30DbContext>));
            if (dbContextOptions is not null)
                services.Remove(dbContextOptions);

            services.AddDbContext<Target30DbContext>(options => options.UseSqlite(_connection));

            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Não criamos o schema aqui: o próprio Program.cs já roda Database.Migrate() ao
            // montar o host (contra essa mesma conexão SQLite em memória), então o schema de
            // teste fica idêntico ao de produção sem duplicar a criação das tabelas.
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task SeedAsync(Action<Target30DbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(_backupDirectory))
                Directory.Delete(_backupDirectory, true);
        }
    }
}
