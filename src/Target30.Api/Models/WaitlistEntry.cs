namespace Target30.Api.Models;

// Interesse de quem ainda não tem conta (página inicial pública). O email é único e guardado em
// minúsculas.
public class WaitlistEntry
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string? Note { get; set; }
    public string? Language { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
