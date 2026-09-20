using System.Globalization;
using System.Text;

namespace Target30.Api.Import;

// Como o valor da coluna "amount" do CSV deve ser lido: no CSV do banco, gasto costuma vir
// negativo (ExpenseNegative); alguns bancos/cartões mandam gasto positivo (ExpensePositive).
public enum AmountConvention
{
    ExpenseNegative,
    ExpensePositive,
}

public enum DateFormatHint
{
    Auto,
    Iso,
    MonthDayYear,
    DayMonthYear,
}

// Linha já normalizada: Amount segue a convenção do Plaid (positivo = gasto, negativo = entrada).
public record ParsedRow(int Line, DateOnly Date, string Description, decimal Amount);

public record ParseError(int Line, string Message);

public record ParseResult(IReadOnlyList<ParsedRow> Rows, IReadOnlyList<ParseError> Errors, string? FatalError);

// Leitor de CSV de extrato (banco/cartão): acha as colunas pelo cabeçalho em pt/en, aceita vírgula,
// ponto-e-vírgula ou tab, aspas, BOM, valor com símbolo/parênteses e colunas separadas de débito/crédito.
public static class CsvTransactionParser
{
    public const int MaxRows = 5000;

    private static readonly string[] DateNames = ["date", "posteddate", "transactiondate", "postdate", "postingdate", "data", "datadatransacao", "datadolancamento", "datalancamento"];
    private static readonly string[] DescriptionNames = ["description", "name", "memo", "payee", "details", "merchant", "descricao", "historico", "estabelecimento", "lancamento"];
    private static readonly string[] AmountNames = ["amount", "valor", "value", "montante"];
    private static readonly string[] DebitNames = ["debit", "withdrawal", "withdrawals", "moneyout", "debito", "saida", "saidas"];
    private static readonly string[] CreditNames = ["credit", "deposit", "deposits", "moneyin", "credito", "entrada", "entradas"];

    public static ParseResult Parse(string csv, AmountConvention convention, DateFormatHint dateFormat)
    {
        var records = ReadRecords(csv);
        if (records.Count == 0)
            return Fatal("O arquivo está vazio.");

        var headerIndex = records.FindIndex(r => r.Fields.Count >= 2 && r.Fields.Any(f => !string.IsNullOrWhiteSpace(f)));
        if (headerIndex < 0)
            return Fatal("O arquivo está vazio.");

        var header = records[headerIndex].Fields.Select(Normalize).ToList();
        int dateCol = IndexOf(header, DateNames), descCol = IndexOf(header, DescriptionNames);
        int amountCol = IndexOf(header, AmountNames), debitCol = IndexOf(header, DebitNames), creditCol = IndexOf(header, CreditNames);

        if (dateCol < 0 || (amountCol < 0 && debitCol < 0 && creditCol < 0))
            return Fatal("Não reconheci as colunas. O cabeçalho precisa ter data e valor (ou débito/crédito). Encontrei: "
                + string.Join(", ", records[headerIndex].Fields.Select(f => f.Trim())));

        var rows = new List<ParsedRow>();
        var errors = new List<ParseError>();
        foreach (var record in records.Skip(headerIndex + 1))
        {
            if (record.Fields.All(string.IsNullOrWhiteSpace))
                continue;
            if (rows.Count + errors.Count >= MaxRows)
            {
                errors.Add(new ParseError(record.Line, $"Limite de {MaxRows} linhas por importação; o resto foi ignorado."));
                break;
            }

            string Field(int col) => col >= 0 && col < record.Fields.Count ? record.Fields[col].Trim() : "";

            if (!TryParseDate(Field(dateCol), dateFormat, out var date))
            {
                errors.Add(new ParseError(record.Line, $"Data inválida: \"{Field(dateCol)}\"."));
                continue;
            }

            decimal amount;
            if (amountCol >= 0 && !string.IsNullOrWhiteSpace(Field(amountCol)))
            {
                if (!TryParseAmount(Field(amountCol), out var value))
                {
                    errors.Add(new ParseError(record.Line, $"Valor inválido: \"{Field(amountCol)}\"."));
                    continue;
                }
                amount = convention == AmountConvention.ExpenseNegative ? -value : value;
            }
            else
            {
                var hasDebit = TryParseAmount(Field(debitCol), out var debit);
                var hasCredit = TryParseAmount(Field(creditCol), out var credit);
                if (!hasDebit && !hasCredit)
                {
                    errors.Add(new ParseError(record.Line, "Sem valor."));
                    continue;
                }
                // Débito = saída (gasto, positivo no Plaid); crédito = entrada (negativo).
                amount = (hasDebit ? Math.Abs(debit) : 0m) - (hasCredit ? Math.Abs(credit) : 0m);
            }

            var description = Field(descCol);
            if (description.Length == 0)
                description = "(sem descrição)";
            rows.Add(new ParsedRow(record.Line, date, description.Length > 200 ? description[..200] : description, amount));
        }

        return new ParseResult(rows, errors, null);
    }

    private static ParseResult Fatal(string message) => new([], [], message);

    // ---- datas ----

    public static bool TryParseDate(string raw, DateFormatHint hint, out DateOnly date)
    {
        date = default;
        var s = raw.Trim().Trim('"');
        if (s.Length == 0)
            return false;

        // Ignora hora ("2026-09-19 10:30", "9/19/2026 10:30").
        var space = s.IndexOf(' ');
        if (space > 0) s = s[..space];
        var t = s.IndexOf('T');
        if (t > 0) s = s[..t];

        if (hint is DateFormatHint.Iso or DateFormatHint.Auto
            && DateOnly.TryParseExact(s, ["yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        if (hint == DateFormatHint.Iso)
            return false;

        var parts = s.Split('/', '-', '.');
        if (parts.Length != 3 || !parts.All(p => int.TryParse(p, out _)))
            return false;
        int a = int.Parse(parts[0]), b = int.Parse(parts[1]), y = int.Parse(parts[2]);
        if (y < 100) y += 2000;
        if (y < 1900 || y > 2200)
            return false;

        var monthFirst = hint switch
        {
            DateFormatHint.MonthDayYear => true,
            DateFormatHint.DayMonthYear => false,
            // Auto: se o primeiro número passa de 12 só pode ser dia; se o segundo passa, é mês/dia. Senão, mês/dia (EUA).
            _ => !(a > 12 && b <= 12),
        };
        var (month, day) = monthFirst ? (a, b) : (b, a);
        if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(y, month))
            return false;

        date = new DateOnly(y, month, day);
        return true;
    }

    // ---- valores ----

    public static bool TryParseAmount(string raw, out decimal value)
    {
        value = 0m;
        var s = raw.Trim();
        if (s.Length == 0)
            return false;

        var negative = false;
        if (s.StartsWith('(') && s.EndsWith(')'))
        {
            negative = true;
            s = s[1..^1];
        }
        if (s.EndsWith('-')) { negative = true; s = s[..^1]; }
        if (s.StartsWith('-')) { negative = true; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];

        var sb = new StringBuilder();
        foreach (var c in s)
            if (char.IsDigit(c) || c is '.' or ',')
                sb.Append(c);
        var digits = sb.ToString();
        if (digits.Length == 0)
            return false;

        var lastDot = digits.LastIndexOf('.');
        var lastComma = digits.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            // O último separador é o decimal; o outro é milhar.
            var decimalSep = lastDot > lastComma ? '.' : ',';
            var thousandSep = decimalSep == '.' ? ',' : '.';
            digits = digits.Replace(thousandSep.ToString(), "").Replace(decimalSep, '.');
        }
        else if (lastComma >= 0)
        {
            // "1.234,56" já tratado acima; "12,5" / "12,50" = decimal; "1,234" / "1,234,567" = milhar.
            digits = System.Text.RegularExpressions.Regex.IsMatch(digits, @"^\d{1,3}(,\d{3})+$") ? digits.Replace(",", "") : digits.Replace(',', '.');
        }
        else if (lastDot >= 0 && System.Text.RegularExpressions.Regex.IsMatch(digits, @"^\d{1,3}(\.\d{3}){2,}$"))
        {
            digits = digits.Replace(".", ""); // 1.234.567
        }

        if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return false;

        if (negative)
            value = -Math.Abs(value);
        return true;
    }

    // ---- leitura do CSV ----

    private record Record(int Line, List<string> Fields);

    private static List<Record> ReadRecords(string csv)
    {
        csv = csv.TrimStart('\uFEFF');
        var firstLine = csv.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(c => c == d));
        if (firstLine.Count(c => c == delimiter) == 0)
            delimiter = ',';

        var records = new List<Record>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        void EndField() { fields.Add(field.ToString()); field.Clear(); }
        void EndRecord()
        {
            EndField();
            records.Add(new Record(recordLine, fields));
            fields = [];
        }

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else { field.Append(c); if (c == '\n') line++; }
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) EndField();
            else if (c == '\r') { }
            else if (c == '\n')
            {
                EndRecord();
                line++;
                recordLine = line;
            }
            else field.Append(c);
        }
        if (field.Length > 0 || fields.Count > 0)
            EndRecord();

        return records;
    }

    private static string Normalize(string header)
    {
        var decomposed = header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    private static int IndexOf(List<string> header, string[] names)
    {
        foreach (var name in names)
        {
            var i = header.IndexOf(name);
            if (i >= 0) return i;
        }
        return -1;
    }
}
