namespace Target30.Api.Models;

public class UserSettings
{
    public string UserId { get; set; } = null!;
    public decimal GlobalTargetUtilizationPercent { get; set; } = 30m;
    public int NotifyDaysBeforeClosing { get; set; } = 3;
}
