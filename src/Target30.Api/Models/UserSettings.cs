namespace Target30.Api.Models;

public class UserSettings
{
    public string UserId { get; set; } = null!;
    public decimal GlobalTargetUtilizationPercent { get; set; } = 30m;
    public int NotifyDaysBeforeClosing { get; set; } = 3;

    // Preenchido/atualizado a cada login com Google — usado pra mandar os alertas por email.
    public string? Email { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
}
