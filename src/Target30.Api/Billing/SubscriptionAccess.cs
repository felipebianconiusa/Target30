using Target30.Api.Models;

namespace Target30.Api.Billing;

public record AccessResult(bool HasAccess, string Reason);

// Quem pode usar o que custa dinheiro (conectar bancos, sincronizar com o Plaid). Só regra
// pura: o resto do app pergunta aqui.
public static class SubscriptionAccess
{
    // Dias de tolerância depois do fim do período quando o pagamento falhou (o Stripe tenta de novo).
    public const int PastDueGraceDays = 3;

    public static bool IsExempt(BillingOptions options, string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && options.ExemptEmails.Any(e => string.Equals(e?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    public static AccessResult Evaluate(BillingOptions options, Subscription? subscription, string? email, DateTime nowUtc)
    {
        if (!options.Enabled)
            return new(true, "billing_disabled");

        if (IsExempt(options, email))
            return new(true, "exempt");

        if (subscription is null)
            return new(false, "no_subscription");

        return subscription.Status switch
        {
            SubscriptionStatus.Active => new(true, "active"),
            SubscriptionStatus.Trial when subscription.TrialEndsAt > nowUtc => new(true, "trial"),
            SubscriptionStatus.Trial => new(false, "trial_ended"),
            SubscriptionStatus.PastDue when subscription.CurrentPeriodEnd?.AddDays(PastDueGraceDays) > nowUtc => new(true, "past_due_grace"),
            SubscriptionStatus.PastDue => new(false, "past_due"),
            _ => new(false, "canceled"),
        };
    }

    public static Subscription NewTrial(string userId, BillingOptions options, DateTime nowUtc) => new()
    {
        UserId = userId,
        Status = SubscriptionStatus.Trial,
        TrialEndsAt = nowUtc.AddDays(Math.Max(options.TrialDays, 0)),
        CreatedAt = nowUtc,
        UpdatedAt = nowUtc,
    };
}
