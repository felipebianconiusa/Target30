namespace Target30.Api.Models;

public class PlaidItem
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string ItemId { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string? InstitutionName { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public string? NextCursor { get; set; }
}
