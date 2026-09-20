namespace Target30.Api.Models;

// Taxa de cashback/recompensa (% do valor da compra) de um cartão numa categoria. A categoria
// especial "BASE" vale pra qualquer compra sem taxa específica.
public class CardRewardRate
{
    public const string BaseCategory = "BASE";

    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public string Category { get; set; } = null!;
    public decimal RatePercent { get; set; }
}
