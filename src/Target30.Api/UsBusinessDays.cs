namespace Target30.Api;

// Feriados federais dos EUA (o calendário que bancos/ACH seguem pra processar pagamentos),
// com a regra de "observado": sábado -> sexta anterior, domingo -> segunda seguinte.
public static class UsBusinessDays
{
    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek != DayOfWeek.Saturday
        && date.DayOfWeek != DayOfWeek.Sunday
        && !GetHolidays(date.Year).Contains(date);

    // Se a data já é dia útil, devolve ela mesma; senão, o último dia útil anterior —
    // ou seja, "até quando dá pra pagar" quando o fechamento cai num fim de semana/feriado.
    public static DateOnly PreviousOrSameBusinessDay(DateOnly date)
    {
        while (!IsBusinessDay(date))
            date = date.AddDays(-1);
        return date;
    }

    private static HashSet<DateOnly> GetHolidays(int year) =>
        [
            ObservedFixed(year, 1, 1), // Ano novo
            NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3), // MLK Day
            NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3), // Washington's Birthday
            LastWeekdayOfMonth(year, 5, DayOfWeek.Monday), // Memorial Day
            ObservedFixed(year, 6, 19), // Juneteenth
            ObservedFixed(year, 7, 4), // Independence Day
            NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1), // Labor Day
            NthWeekdayOfMonth(year, 10, DayOfWeek.Monday, 2), // Columbus Day
            ObservedFixed(year, 11, 11), // Veterans Day
            NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4), // Thanksgiving
            ObservedFixed(year, 12, 25), // Natal
        ];

    private static DateOnly ObservedFixed(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        if (date.DayOfWeek == DayOfWeek.Saturday) return date.AddDays(-1);
        if (date.DayOfWeek == DayOfWeek.Sunday) return date.AddDays(1);
        return date;
    }

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return last.AddDays(-offset);
    }
}
