namespace Target30.Api.Models;

// "Sempre que for esse estabelecimento, use essa categoria": criada quando o usuário recategoriza
// uma transação escolhendo aplicar a todas do mesmo estabelecimento. O sync aplica às novas.
public class CategoryRule
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;

    // Ver MerchantKey.From — nome normalizado do estabelecimento.
    public string MerchantKey { get; set; } = null!;
    public string Category { get; set; } = null!;
}
