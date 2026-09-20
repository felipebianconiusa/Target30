namespace Target30.Api;

public enum FreshnessStatus
{
    Ok,
    Stale,
    NeedsAttention,
}

// Decide se os dados de um banco no Plaid estão velhos demais pra confiar. O Plaid busca no
// banco algumas vezes por dia; passar de um dia sem atualizar (ou falhar depois da última
// vez que deu certo) quer dizer que o que você vê no app pode não ser o que o banco mostra.
public static class DataFreshness
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);
    public static readonly TimeSpan FailingAfter = TimeSpan.FromHours(12);

    public static FreshnessStatus Evaluate(
        DateTimeOffset? lastSuccessfulUpdate, DateTimeOffset? lastFailedUpdate, string? errorCode, DateTimeOffset now)
    {
        // Erro no item (ex.: precisa reconectar/login) — nada vai atualizar até você agir.
        if (!string.IsNullOrEmpty(errorCode))
            return FreshnessStatus.NeedsAttention;

        // Sem nenhuma informação do Plaid não dá pra afirmar que está velho.
        if (lastSuccessfulUpdate is null)
            return FreshnessStatus.Ok;

        var age = now - lastSuccessfulUpdate.Value;
        if (age > StaleAfter)
            return FreshnessStatus.Stale;

        var failedSinceLastSuccess = lastFailedUpdate is not null && lastFailedUpdate > lastSuccessfulUpdate;
        return failedSinceLastSuccess && age > FailingAfter ? FreshnessStatus.Stale : FreshnessStatus.Ok;
    }

    public static string ToApiString(this FreshnessStatus status) => status switch
    {
        FreshnessStatus.Stale => "stale",
        FreshnessStatus.NeedsAttention => "attention",
        _ => "ok",
    };

    public static string BuildAlertBody(
        IEnumerable<(string Institution, FreshnessStatus Status, DateTimeOffset? LastSuccessfulUpdate)> problems,
        DateTimeOffset now)
    {
        var lines = problems.Select(p => p.Status == FreshnessStatus.NeedsAttention
            ? $"- {p.Institution}: a conexão precisa de atenção (reconectar em Contas)."
            : p.LastSuccessfulUpdate is { } last
                ? $"- {p.Institution}: o Plaid não atualiza há {(int)(now - last).TotalHours}h."
                : $"- {p.Institution}: o Plaid não está atualizando.");

        return "Target30 — dados possivelmente desatualizados:\n\n" + string.Join("\n", lines)
            + "\n\nO que aparece no app pode não ser o que o banco mostra. Em Contas você vê a última atualização do Plaid e pode pedir \"Atualizar agora\".";
    }

    // Um aviso por "episódio": não repete a cada sync enquanto o problema continua; quando o
    // banco volta ao normal o marcador é zerado e um problema futuro avisa de novo.
    public static bool ShouldAlert(FreshnessStatus status, DateTime? lastAlertSentAt) =>
        status != FreshnessStatus.Ok && lastAlertSentAt is null;
}
