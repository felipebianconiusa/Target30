using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Billing;

// Junta as regras puras (SubscriptionAccess) com o banco: cria o teste grátis quando o usuário
// aparece pela primeira vez com a cobrança ligada.
public class BillingService
{
    private readonly Target30DbContext _db;

    public BillingService(Target30DbContext db, IOptions<BillingOptions> options)
    {
        _db = db;
        Options = options.Value;
    }

    public BillingOptions Options { get; }

    public async Task<(Subscription? Subscription, AccessResult Access)> EvaluateAsync(string userId, string? email)
    {
        var now = DateTime.UtcNow;
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);

        if (subscription is null && Options.Enabled && !SubscriptionAccess.IsExempt(Options, email))
        {
            subscription = SubscriptionAccess.NewTrial(userId, Options, now);
            _db.Subscriptions.Add(subscription);
            await _db.SaveChangesAsync();
        }

        return (subscription, SubscriptionAccess.Evaluate(Options, subscription, email, now));
    }

    // Usuários que podem gerar custo agora (sincronizar com o Plaid, receber alertas). Com a
    // cobrança desligada, todos.
    public async Task<HashSet<string>> UsersWithAccessAsync()
    {
        var userIds = await _db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync();
        var emails = await _db.UserSettings.Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, s => s.Email);

        var allowed = new HashSet<string>();
        foreach (var userId in userIds)
        {
            var (_, access) = await EvaluateAsync(userId, emails.GetValueOrDefault(userId));
            if (access.HasAccess)
                allowed.Add(userId);
        }
        return allowed;
    }
}
