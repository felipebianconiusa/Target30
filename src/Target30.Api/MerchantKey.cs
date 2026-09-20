using System.Text.RegularExpressions;

namespace Target30.Api;

public static partial class MerchantKey
{
    // Chave estável do estabelecimento: minúscula, sem números/# (datas, ids, confirmações) e sem
    // sufixos como "Conf#..." — "CHECKCARD 0916 Cherry Technol" e "CHECKCARD 0917 Cherry Technol"
    // viram a mesma. Vazia = não dá pra identificar o estabelecimento (não cria regra).
    public static string From(string? merchantName, string name)
    {
        var raw = string.IsNullOrWhiteSpace(merchantName) ? name : merchantName;
        var key = NoiseRegex().Replace(IncomeDetector.Normalize(raw).ToLowerInvariant(), " ");
        key = string.Join(' ', key.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return key.Length > 60 ? key[..60] : key;
    }

    // Categoria da regra que vale pra esse estabelecimento, se houver.
    public static string? RuleFor(IReadOnlyDictionary<string, string> rules, string? merchantName, string name)
    {
        if (rules.Count == 0)
            return null;

        var key = From(merchantName, name);
        return key.Length > 0 && rules.TryGetValue(key, out var category) ? category : null;
    }

    [GeneratedRegex(@"[#\d]+")]
    private static partial Regex NoiseRegex();
}
