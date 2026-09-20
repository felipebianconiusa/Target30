namespace Target30.Api;

// Lista de emails que podem entrar (Authentication:AllowedEmails). Sem lista configurada, qualquer
// conta Google entra (comportamento de desenvolvimento local, onde só você acessa). Com a lista, só
// os emails dela — e verificados pelo Google. É o que impede um estranho de logar e usar as
// credenciais do Plaid do dono se o app for exposto fora do localhost.
public static class AllowedEmails
{
    public static bool IsAllowed(IEnumerable<string>? allowed, string? email, bool emailVerified)
    {
        var list = allowed?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (list is null || list.Count == 0)
            return true;

        return emailVerified
            && !string.IsNullOrWhiteSpace(email)
            && list.Any(e => string.Equals(e.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
