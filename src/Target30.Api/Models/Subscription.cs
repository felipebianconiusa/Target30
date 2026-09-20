namespace Target30.Api.Models;

// Situação de cobrança do usuário. Criada no primeiro login (teste grátis); depois é atualizada
// pelos webhooks do Stripe.
public class Subscription
{
    public string UserId { get; set; } = null!;

    // "trial" (teste grátis nosso), "active", "past_due", "canceled" ou "none".
    public string Status { get; set; } = SubscriptionStatus.Trial;

    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class SubscriptionStatus
{
    public const string Trial = "trial";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
    public const string None = "none";
}
