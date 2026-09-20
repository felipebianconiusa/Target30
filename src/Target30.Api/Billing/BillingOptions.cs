namespace Target30.Api.Billing;

// Configuração da seção "Billing". Com Enabled=false (padrão) o app se comporta como sempre:
// uso pessoal, sem limite nem cobrança.
public class BillingOptions
{
    public bool Enabled { get; set; }
    public int TrialDays { get; set; } = 14;
    public int MaxItemsPerUser { get; set; } = 10;

    // Emails que nunca são cobrados nem limitados (o dono do app).
    public string[] ExemptEmails { get; set; } = [];

    // Só texto de exibição (ex.: "US$ 9/mês"); o preço de verdade é o do Stripe (PriceId).
    public string? PriceLabel { get; set; }

    // Endereço público do app (pra montar as URLs de retorno do Stripe). Vazio = usa a origem da requisição.
    public string? PublicBaseUrl { get; set; }

    public StripeOptions Stripe { get; set; } = new();
}

public class StripeOptions
{
    public string? SecretKey { get; set; }
    public string? PriceId { get; set; }
    public string? WebhookSecret { get; set; }

    public bool CheckoutConfigured => !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(PriceId);
}
