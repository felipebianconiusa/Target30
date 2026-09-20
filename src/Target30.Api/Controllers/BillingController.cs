using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Billing;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BillingController : ControllerBase
{
    private readonly BillingService _billing;
    private readonly IStripeGateway _stripe;
    private readonly Target30DbContext _db;

    public BillingController(BillingService billing, IStripeGateway stripe, Target30DbContext db)
    {
        _billing = billing;
        _stripe = stripe;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string? CurrentEmail => User.FindFirstValue(ClaimTypes.Email);

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var (subscription, access) = await _billing.EvaluateAsync(CurrentUserId, CurrentEmail);
        var options = _billing.Options;

        return Ok(new BillingStatusDto(
            options.Enabled,
            access.HasAccess,
            access.Reason,
            subscription?.Status ?? SubscriptionStatus.None,
            subscription?.TrialEndsAt,
            subscription?.CurrentPeriodEnd,
            !string.IsNullOrWhiteSpace(subscription?.StripeCustomerId),
            options.Stripe.CheckoutConfigured,
            options.PriceLabel));
    }

    // Abre o pagamento da assinatura no Stripe Checkout.
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout()
    {
        var options = _billing.Options;
        if (!options.Enabled)
            return BadRequest(new { code = "billing_disabled" });
        if (!options.Stripe.CheckoutConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "stripe_not_configured" });

        var (subscription, access) = await _billing.EvaluateAsync(CurrentUserId, CurrentEmail);
        if (subscription?.Status == SubscriptionStatus.Active)
            return Conflict(new { code = "already_subscribed" });

        var baseUrl = BaseUrl(options);
        var url = await _stripe.CreateCheckoutUrlAsync(
            CurrentUserId, CurrentEmail, subscription?.StripeCustomerId,
            $"{baseUrl}/billing?checkout=success", $"{baseUrl}/billing?checkout=cancel");
        return Ok(new { url });
    }

    // Portal do cliente: trocar cartão, cancelar, ver faturas.
    [HttpPost("portal")]
    public async Task<IActionResult> Portal()
    {
        var options = _billing.Options;
        if (!options.Stripe.CheckoutConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "stripe_not_configured" });

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (string.IsNullOrWhiteSpace(subscription?.StripeCustomerId))
            return NotFound(new { code = "no_customer" });

        var url = await _stripe.CreatePortalUrlAsync(subscription.StripeCustomerId, $"{BaseUrl(options)}/billing");
        return Ok(new { url });
    }

    // Webhook do Stripe. Sem login: a autenticidade vem da assinatura do corpo (Stripe-Signature).
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var secret = _billing.Options.Stripe.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "webhook_not_configured" });

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        if (!StripeSignature.Verify(payload, Request.Headers["Stripe-Signature"].FirstOrDefault(), secret, DateTimeOffset.UtcNow))
            return BadRequest(new { code = "invalid_signature" });

        try
        {
            await BillingWebhookProcessor.ApplyAsync(_db, payload, DateTime.UtcNow);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { code = "invalid_payload" });
        }

        // Evento que não tratamos também é 200 (senão o Stripe fica reenviando).
        return Ok();
    }

    private string BaseUrl(BillingOptions options) =>
        (string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? $"{Request.Scheme}://{Request.Host}" : options.PublicBaseUrl).TrimEnd('/');
}

public record BillingStatusDto(
    bool Enabled,
    bool HasAccess,
    string Reason,
    string Status,
    DateTime? TrialEndsAt,
    DateTime? CurrentPeriodEnd,
    bool CanManage,
    bool CheckoutAvailable,
    string? PriceLabel);
