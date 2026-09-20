using Microsoft.Extensions.Options;
using WaterBilling.Domain.Common;

namespace WaterBilling.Api.Infrastructure;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// The utility's local offset, used to decide which calendar month "previous
    /// month" means. A fixed offset rather than an IANA zone: the target deployments
    /// do not observe daylight saving, and this removes the DST edge cases entirely
    /// (ADR-0006).
    /// </summary>
    public double BillingOffsetHours { get; set; } = 5.5;

    public string InvoiceNumberPrefix { get; set; } = "INV";

    public int PaymentTermsDays { get; set; } = 14;
}

/// <summary>
/// Single source of truth for period boundaries. Every screen, every report and the
/// billing run itself resolve "last month" through here, so they cannot disagree
/// about where a month starts.
/// </summary>
public sealed class BillingCalendar(IOptions<BillingOptions> options, TimeProvider timeProvider)
{
    private readonly BillingOptions options = options.Value;

    public TimeSpan Offset => TimeSpan.FromHours(this.options.BillingOffsetHours);

    public DateRange PreviousMonth() => DateRange.PreviousMonth(timeProvider.GetUtcNow(), Offset);

    public DateRange CurrentMonth()
    {
        var previous = PreviousMonth();
        return new DateRange(previous.End, previous.End.AddMonths(1));
    }

    /// <summary>The month containing <paramref name="instant"/>, in billing-local time.</summary>
    public DateRange MonthContaining(DateTimeOffset instant)
    {
        var local = instant.ToOffset(Offset);
        var start = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, Offset);
        return new DateRange(start, start.AddMonths(1));
    }

    /// <summary>The last <paramref name="count"/> complete-or-current months, oldest first.</summary>
    public IReadOnlyList<DateRange> LastMonths(int count)
    {
        var current = CurrentMonth();
        var months = new List<DateRange>(count);

        for (var offset = count - 1; offset >= 0; offset--)
        {
            var start = current.Start.AddMonths(-offset);
            months.Add(new DateRange(start, start.AddMonths(1)));
        }

        return months;
    }

    public DateTimeOffset DueDateFor(DateRange period) => period.End.AddDays(this.options.PaymentTermsDays);

    /// <summary>
    /// <c>INV-2026-08-000117</c>. Period-scoped and zero-padded so invoice numbers
    /// sort chronologically as text, which is how they appear in exports and reports.
    /// </summary>
    public string InvoiceNumber(DateRange period, int sequence)
    {
        var local = period.Start.ToOffset(Offset);
        return $"{this.options.InvoiceNumberPrefix}-{local:yyyy-MM}-{sequence:D6}";
    }
}
