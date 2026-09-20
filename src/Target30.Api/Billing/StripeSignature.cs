using System.Security.Cryptography;
using System.Text;

namespace Target30.Api.Billing;

// Verificação do cabeçalho Stripe-Signature ("t=timestamp,v1=hmac[,v1=...]"): HMAC-SHA256 de
// "{t}.{corpo}" com o segredo do webhook, comparado em tempo constante, com tolerância de tempo
// contra replay.
public static class StripeSignature
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static string Compute(string payload, long timestamp, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
    }

    public static string BuildHeader(string payload, long timestamp, string secret) =>
        $"t={timestamp},v1={Compute(payload, timestamp, secret)}";

    public static bool Verify(string payload, string? header, string secret, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(secret))
            return false;

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
                continue;

            if (kv[0] == "t" && long.TryParse(kv[1], out var t))
                timestamp = t;
            else if (kv[0] == "v1")
                signatures.Add(kv[1]);
        }

        if (timestamp is null || signatures.Count == 0)
            return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (age.Duration() > (tolerance ?? DefaultTolerance))
            return false;

        var expected = Encoding.UTF8.GetBytes(Compute(payload, timestamp.Value, secret));
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(s.ToLowerInvariant())));
    }
}
