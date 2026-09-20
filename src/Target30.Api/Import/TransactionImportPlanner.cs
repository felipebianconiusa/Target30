using System.Security.Cryptography;
using System.Text;
using Target30.Api.Models;

namespace Target30.Api.Import;

public record PlannedRow(ParsedRow Row, string TransactionId, bool Duplicate);

// Decide, linha a linha, o que é novo e o que já existe na conta — pra reimportar o mesmo arquivo
// (ou um extrato que se sobrepõe ao que o Plaid já trouxe) não duplicar nada.
public static class TransactionImportPlanner
{
    public static string Normalize(string description) =>
        new(description.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // Id determinístico: reimportar o mesmo arquivo gera os mesmos ids. `occurrence` diferencia
    // linhas idênticas dentro do arquivo (duas compras iguais no mesmo dia).
    public static string TransactionId(string accountId, ParsedRow row, int occurrence)
    {
        var key = $"{accountId}|{row.Date:yyyy-MM-dd}|{row.Amount:F2}|{Normalize(row.Description)}|{occurrence}";
        return "csv:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    public static List<PlannedRow> Plan(IEnumerable<ParsedRow> rows, IReadOnlyCollection<PlaidTransaction> existing, string accountId)
    {
        var existingIds = existing.Select(e => e.PlaidTransactionId).ToHashSet();
        var consumed = new HashSet<int>(); // cada transação existente cobre no máximo uma linha do arquivo
        var seen = new Dictionary<string, int>();
        var planned = new List<PlannedRow>();

        foreach (var row in rows)
        {
            var baseKey = $"{row.Date:yyyy-MM-dd}|{row.Amount:F2}|{Normalize(row.Description)}";
            seen[baseKey] = seen.GetValueOrDefault(baseKey) + 1;
            var id = TransactionId(accountId, row, seen[baseKey]);

            var duplicate = existingIds.Contains(id);
            if (!duplicate)
            {
                var match = existing.FirstOrDefault(e =>
                    !consumed.Contains(e.Id) && e.Date == row.Date && Math.Abs(e.Amount - row.Amount) < 0.005m && SimilarText(e, row));
                if (match is not null)
                {
                    consumed.Add(match.Id);
                    duplicate = true;
                }
            }

            planned.Add(new PlannedRow(row, id, duplicate));
        }

        return planned;
    }

    // O Plaid costuma trocar a descrição bruta por um nome de estabelecimento: aceita igual ou contido.
    private static bool SimilarText(PlaidTransaction existing, ParsedRow row)
    {
        var fileText = Normalize(row.Description);
        foreach (var candidate in new[] { existing.Name, existing.MerchantName })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var text = Normalize(candidate);
            if (text.Length > 0 && fileText.Length > 0 && (text.Contains(fileText) || fileText.Contains(text)))
                return true;
        }
        return false;
    }
}
