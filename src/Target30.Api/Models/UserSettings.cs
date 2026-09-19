namespace Target30.Api.Models;

public class UserSettings
{
    public string UserId { get; set; } = null!;
    public decimal GlobalTargetUtilizationPercent { get; set; } = 30m;
    public int NotifyDaysBeforeClosing { get; set; } = 3;

    // Preenchido/atualizado a cada login com Google — usado pra mandar os alertas por email.
    public string? Email { get; set; }
    public bool NotificationsEnabled { get; set; } = true;

    // Resumo semanal (visão geral de todos os cartões) — independente do alerta de fechamento.
    public bool WeeklyDigestEnabled { get; set; } = true;
    public DateOnly? LastDigestSentDate { get; set; }
}
