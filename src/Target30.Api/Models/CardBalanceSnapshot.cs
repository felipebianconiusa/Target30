namespace Target30.Api.Models;

// Um ponto por dia por cartão (capturado durante o sync), pra desenhar o histórico de
// utilização ao longo do tempo na tela de Cartões.
public class CardBalanceSnapshot
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string AccountId { get; set; } = null!;
    public DateOnly Date { get; set; }
    public decimal Balance { get; set; }
    public decimal? Limit { get; set; }
    public decimal? UtilizationPercent { get; set; }
}
