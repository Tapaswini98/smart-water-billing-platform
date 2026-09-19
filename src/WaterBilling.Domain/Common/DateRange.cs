namespace WaterBilling.Domain.Common;

/// <summary>
/// A half-open billing period <c>[Start, End)</c>.
/// Half-open is deliberate: consecutive months tile exactly, with no gap at
/// midnight on the 1st and no reading counted into two invoices.
/// </summary>
public readonly record struct DateRange(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    public TimeSpan Duration => End - Start;

    /// <summary>
    /// The calendar month immediately before <paramref name="reference"/>, expressed
    /// in <paramref name="offset"/> (the utility's local billing offset — see ADR-0006).
    /// </summary>
    public static DateRange PreviousMonth(DateTimeOffset reference, TimeSpan offset)
    {
        var local = reference.ToOffset(offset);
        var firstOfThisMonth = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, offset);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
        return new DateRange(firstOfLastMonth, firstOfThisMonth);
    }

    public override string ToString() => $"{Start:yyyy-MM-dd} .. {End:yyyy-MM-dd}";
}
