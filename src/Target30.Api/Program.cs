using System.Threading.RateLimiting;
using Going.Plaid;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Target30.Api;
using Target30.Api.Billing;
using Target30.Api.Data;
using Target30.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddPlaid(builder.Configuration.GetSection("Plaid"));

// Chaves que criptografam os access tokens do Plaid no banco. PERDER esta pasta = perder os
// tokens (é preciso reconectar os bancos): guarde uma cópia SEPARADA do banco e dos backups.
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (string.IsNullOrWhiteSpace(keysDirectory))
    keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "keys");
builder.Services.AddDataProtection()
    .SetApplicationName("Target30")
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

builder.Services.AddDbContext<Target30DbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<PlaidSyncService>();
builder.Services.AddScoped<CashFlowService>();

builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection("Billing"));
builder.Services.AddScoped<BillingService>();
builder.Services.AddHttpClient<IStripeGateway, StripeGateway>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<PlaidBackgroundService>();

// Limite global por IP — o app guarda dados financeiros reais, então mesmo sendo uso pessoal
// vale ter uma trava básica contra abuso/força bruta se algum dia ficar exposto na internet.
// Sem fila (queue 0): quem estourar recebe 429 na hora em vez de esperar.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "target30_auth";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        // API pura: em vez de redirecionar pra uma página de login (padrão do cookie auth),
        // devolve 401/403 puro pro frontend decidir o que fazer.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

var app = builder.Build();

TokenCrypto.Configure(new TokenProtector(
    app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Target30.PlaidAccessToken")));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
    db.Database.Migrate();
    await TokenEncryptionMigration.RunAsync(db);
}

app.UseHttpsRedirection();

// Modo "um processo só": se o build do Angular estiver em wwwroot (scripts/publish-local.ps1),
// a própria API serve o app — uma origem, um endereço — o que facilita expor por um túnel
// (Tailscale/Cloudflare) pra usar no celular. Sem wwwroot, nada muda (dev usa ng serve).
var serveFrontend = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"));
// Arquivos estáticos ANTES do roteamento: o middleware ignora requisições em que o roteamento já
// escolheu um endpoint, e o fallback do SPA casa com qualquer caminho.
if (serveFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseRouting();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("AngularDev");
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (serveFrontend)
    app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();

// Torna a classe Program acessível pro WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
