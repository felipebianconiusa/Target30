namespace Target30.Api;

// O /transactions/refresh do Plaid é cobrado por chamada — esta trava impede clique duplo ou
// repetido de custar dinheiro à toa. Os dados só mudam quando o banco tem algo novo, então
// insistir em menos de alguns minutos quase nunca traz nada.
public static class PlaidRefreshGuard
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    // Quanto falta pra poder pedir de novo; null = pode pedir agora.
    public static TimeSpan? RemainingCooldown(DateTime? lastRequestedUtc, DateTime nowUtc)
    {
        if (lastRequestedUtc is null)
            return null;

        var remaining = lastRequestedUtc.Value + Cooldown - nowUtc;
        return remaining > TimeSpan.Zero ? remaining : null;
    }
}
