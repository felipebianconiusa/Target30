namespace Target30.Api;

public static class DateMath
{
    public static DateOnly BuildClamped(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    // Todas as ocorrências de "dia X de cada mês" estritamente depois de `fromExclusive`
    // e até `toInclusive` (inclusive).
    public static IEnumerable<DateOnly> MonthlyOccurrences(int dayOfMonth, DateOnly fromExclusive, DateOnly toInclusive)
    {
        var cursor = new DateOnly(fromExclusive.Year, fromExclusive.Month, 1);
        var endCursor = new DateOnly(toInclusive.Year, toInclusive.Month, 1);
        while (cursor <= endCursor)
        {
            var candidate = BuildClamped(cursor.Year, cursor.Month, dayOfMonth);
            if (candidate > fromExclusive && candidate <= toInclusive)
                yield return candidate;
            cursor = cursor.AddMonths(1);
        }
    }
}
