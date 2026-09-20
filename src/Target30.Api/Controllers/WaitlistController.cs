using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Target30.Api.Billing;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

[ApiController]
[Route("api/waitlist")]
public class WaitlistController : ControllerBase
{
    public const string RateLimitPolicy = "waitlist";

    private readonly Target30DbContext _db;
    private readonly BillingOptions _billing;

    public WaitlistController(Target30DbContext db, IOptions<BillingOptions> billing)
    {
        _db = db;
        _billing = billing.Value;
    }

    // Cadastro público. Sempre responde 200 pra quem manda um email válido (não revela se já
    // estava na lista) e ignora silenciosamente robôs que preenchem o campo-isca "website".
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost]
    public async Task<IActionResult> Join([FromBody] WaitlistRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Website))
            return Ok();

        var email = NormalizeEmail(request.Email);
        if (email is null)
            return BadRequest(new { code = "invalid_email" });

        if (!await _db.WaitlistEntries.AnyAsync(w => w.Email == email))
        {
            var note = request.Note?.Trim();
            _db.WaitlistEntries.Add(new WaitlistEntry
            {
                Email = email,
                Note = string.IsNullOrEmpty(note) ? null : note[..Math.Min(note.Length, 300)],
                Language = request.Language is { Length: <= 5 } lang ? lang : null,
            });
            await _db.SaveChangesAsync();
        }

        return Ok();
    }

    // Lista completa — só pro dono do app (Billing:ExemptEmails).
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!SubscriptionAccess.IsExempt(_billing, User.FindFirstValue(ClaimTypes.Email)))
            return Forbid();

        var entries = await _db.WaitlistEntries.OrderBy(w => w.CreatedAt).ToListAsync();
        return Ok(entries.Select(w => new WaitlistEntryDto(w.Email, w.Note, w.Language, w.CreatedAt)));
    }

    [Authorize]
    [HttpGet("csv")]
    public async Task<IActionResult> Csv()
    {
        if (!SubscriptionAccess.IsExempt(_billing, User.FindFirstValue(ClaimTypes.Email)))
            return Forbid();

        var csv = new StringBuilder("Email,Nota,Idioma,Data\n");
        foreach (var w in await _db.WaitlistEntries.OrderBy(w => w.CreatedAt).ToListAsync())
            csv.Append(string.Join(',', Field(w.Email), Field(w.Note ?? ""), Field(w.Language ?? ""), w.CreatedAt.ToString("yyyy-MM-dd"))).Append('\n');

        return File(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "target30-lista-de-espera.csv");
    }

    // Aceita só um endereço simples (sem nome de exibição) e de tamanho razoável.
    public static string? NormalizeEmail(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 254)
            return null;

        return MailAddress.TryCreate(trimmed, out var parsed) && parsed.Address.Equals(trimmed, StringComparison.OrdinalIgnoreCase) && parsed.Host.Contains('.')
            ? trimmed.ToLowerInvariant()
            : null;
    }

    // Neutraliza fórmula em planilha (=, +, -, @) além do escape normal de CSV.
    private static string Field(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0]))
            value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

public record WaitlistRequest(string? Email, string? Note, string? Language, string? Website);

public record WaitlistEntryDto(string Email, string? Note, string? Language, DateTime CreatedAt);
