using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Data;

namespace Target30.Api.Tests;

// Sobe a API inteira (mesmos controllers, mesmo pipeline) contra um SQLite em memória, com
// autenticação trocada por um usuário fixo — pra testar os controllers fim-a-fim sem tocar no
// banco real nem precisar de um login do Google de verdade.
public class Target30WebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            _connection.Open();

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
            _connection.Dispose();
    }
}
