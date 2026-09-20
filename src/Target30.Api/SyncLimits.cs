namespace Target30.Api;

// O /liabilities/get do Plaid é cobrado por requisição depois da cota grátis, e o que ele traz
// (vencimento, pagamento mínimo, extrato) muda no máximo uma vez por mês: não precisa ser buscado
// a cada sync. Estas regras limitam a frequência por banco.
public static class LiabilitiesSchedule
{
    // Depois de um erro (banco sem suporte a Liabilities, produto não habilitado) espera bem mais,
    // pra não pagar por uma chamada que sempre falha.
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromDays(7);

    public static bool ShouldFetch(DateTime? nextAllowedAtUtc, DateTime nowUtc) =>
        nextAllowedAtUtc is null || nextAllowedAtUtc <= nowUtc;

    public static DateTime NextAfterSuccess(DateTime nowUtc, TimeSpan interval) => nowUtc + interval;

    public static DateTime NextAfterFailure(DateTime nowUtc) => nowUtc + FailureBackoff;
}

public static class SyncSchedule
{
    // Um banco que sincronizou há pouco não precisa sincronizar de novo só porque a API foi
    // reiniciada (reinícios em sequência, no desenvolvimento, gastavam chamadas à toa). O ciclo
    // normal continua rodando porque o limite é menor que o intervalo do timer.
    public static bool IsDue(DateTime? lastSyncedAtUtc, DateTime nowUtc, TimeSpan interval) =>
        lastSyncedAtUtc is null || nowUtc - lastSyncedAtUtc.Value >= TimeSpan.FromTicks((long)(interval.Ticks * 0.8));
}
