using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Billing;

// Traduz os eventos do webhook do Stripe em mudanças na Subscription do usuário. Idempotente:
// o Stripe pode reenviar o mesmo evento, e a ordem de chegada não é garantida.
public static class BillingWebhookProcessor
{
    public static string MapStatus(string? stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" => SubscriptionStatus.PastDue,
        _ => SubscriptionStatus.Canceled,
    };

    // Devolve true se o evento era de um tipo que tratamos e achamos o usuário.
    public static async Task<bool> ApplyAsync(Target30DbContext db, string json, DateTime nowUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var obj = root.GetProperty("data").GetProperty("object");

        switch (type)
        {
            case "checkout.session.completed":
            {
                var userId = Str(obj, "client_reference_id");
                if (userId is null)
                    return false;

                var sub = await GetOrCreateAsync(db, userId, nowUtc);
                sub.StripeCustomerId = Str(obj, "customer") ?? sub.StripeCustomerId;
                sub.StripeSubscriptionId = Str(obj, "subscription") ?? sub.StripeSubscriptionId;
                sub.Status = SubscriptionStatus.Active;
                sub.UpdatedAt = nowUtc;
                await db.SaveChangesAsync();
                return true;
            }

            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
            {
                var sub = await FindAsync(db, Str(obj, "customer"), MetadataUserId(obj));
                if (sub is null)
                    return false;

                sub.StripeCustomerId ??= Str(obj, "customer");
                sub.StripeSubscriptionId = Str(obj, "id") ?? sub.StripeSubscriptionId;
                sub.Status = type == "customer.subscription.deleted" ? SubscriptionStatus.Canceled : MapStatus(Str(obj, "status"));
                sub.CurrentPeriodEnd = PeriodEnd(obj) ?? sub.CurrentPeriodEnd;
                sub.UpdatedAt = nowUtc;
                await db.SaveChangesAsync();
                return true;
            }

            case "invoice.payment_failed":
            {
                var sub = await FindAsync(db, Str(obj, "customer"), null);
                if (sub is null)
                    return false;

                sub.Status = SubscriptionStatus.PastDue;
                sub.UpdatedAt = nowUtc;
                await db.SaveChangesAsync();
                return true;
            }

            default:
                return false;
        }
    }

    private static async Task<Subscription> GetOrCreateAsync(Target30DbContext db, string userId, DateTime nowUtc)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
        if (sub is null)
        {
            sub = new Subscription { UserId = userId, CreatedAt = nowUtc, UpdatedAt = nowUtc };
            db.Subscriptions.Add(sub);
        }
        return sub;
    }

    private static async Task<Subscription?> FindAsync(Target30DbContext db, string? customerId, string? userId)
    {
        if (customerId is not null)
        {
            var byCustomer = await db.Subscriptions.FirstOrDefaultAsync(s => s.StripeCustomerId == customerId);
            if (byCustomer is not null)
                return byCustomer;
        }

        return userId is null ? null : await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? MetadataUserId(JsonElement obj) =>
        obj.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? Str(m, "userId") : null;

    // O Stripe mudou onde fica o fim do período: antes na assinatura, agora em cada item.
    private static DateTime? PeriodEnd(JsonElement obj)
    {
        if (obj.TryGetProperty("current_period_end", out var top) && top.TryGetInt64(out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

        if (obj.TryGetProperty("items", out var items)
            && items.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("current_period_end", out var itemEnd)
            && itemEnd.TryGetInt64(out var itemUnix))
            return DateTimeOffset.FromUnixTimeSeconds(itemUnix).UtcDateTime;

        return null;
    }
}
