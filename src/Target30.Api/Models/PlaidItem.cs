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

    // Atualizado a cada sync bem-sucedido (manual ou automático) — mostrado na tela de Contas
    // pra você confiar no que está rodando sozinho em background.
    public DateTime? LastSyncedAt { get; set; }

    // Quando foi o último "atualizar agora" (/transactions/refresh, cobrado por chamada pelo
    // Plaid). Guardado no banco pra a trava de segurança sobreviver a reinício da API.
    public DateTime? LastRefreshRequestedAt { get; set; }
}