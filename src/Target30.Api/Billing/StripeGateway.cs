using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Target30.Api.Billing;

public interface IStripeGateway
{
    // Devolve a URL do Checkout do Stripe (pagamento da assinatura).
    Task<string> CreateCheckoutUrlAsync(string userId, string? email, string? customerId, string successUrl, string cancelUrl);

    // Devolve a URL do portal do cliente (trocar cartão, cancelar, ver faturas).
    Task<string> CreatePortalUrlAsync(string customerId, string returnUrl);
}

// Fala com a API do Stripe por HTTP direto (sem SDK): só precisamos de duas chamadas.
public class StripeGateway : IStripeGateway
{
    private readonly HttpClient _http;
    private readonly BillingOptions _options;

    public StripeGateway(HttpClient http, IOptions<BillingOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress ??= new Uri("https://api.stripe.com/");
    }

    public async Task<string> CreateCheckoutUrlAsync(string userId, string? email, string? customerId, string successUrl, string cancelUrl)
    {
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = _options.Stripe.PriceId!,
            ["line_items[0][quantity]"] = "1",
            ["client_reference_id"] = userId,
            ["subscription_data[metadata][userId]"] = userId,
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["allow_promotion_codes"] = "true",
        };
        // Cliente que já existe no Stripe: reaproveita (não cria duplicado); senão pré-preenche o email.
        if (!string.IsNullOrWhiteSpace(customerId))
            form["customer"] = customerId;
        else if (!string.IsNullOrWhiteSpace(email))
            form["customer_email"] = email;

        return await PostForUrlAsync("v1/checkout/sessions", form);
    }

    public Task<string> CreatePortalUrlAsync(string customerId, string returnUrl) =>
        PostForUrlAsync("v1/billing_portal/sessions", new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["return_url"] = returnUrl,
        });

    private async Task<string> PostForUrlAsync(string path, Dictionary<string, string> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Stripe.SecretKey);

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Stripe respondeu {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Stripe não devolveu a URL.");
    }
}
