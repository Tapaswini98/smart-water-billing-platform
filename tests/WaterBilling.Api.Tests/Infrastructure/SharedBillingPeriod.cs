namespace WaterBilling.Api.Tests.Infrastructure;

/// <summary>
/// One billing period, shared by every test that calls <c>POST /billing-runs</c>.
/// <para>
/// <c>BillingCalendar.InvoiceNumber</c> formats as <c>INV-{yyyy-MM}-{sequence:D6}</c>,
/// unique per calendar month, with the sequence counted per exact period start
/// (<c>db.Invoices.CountAsync(i =&gt; i.PeriodStartUtc == period.Start)</c>). Two
/// tests using different days within the same month would each compute sequence 1
/// for their first invoice and collide on the same invoice number — a real
/// production invariant (one sequence per month), not a bug, but one the tests have
/// to respect. Sharing one period start lets the sequence increment naturally
/// across tests instead, which also mirrors production more closely: many meters,
/// one period, one sequence.
/// </para>
/// <para>
/// Because tests share a database, assertions must never depend on the run's
/// aggregate counts (<c>invoicesGenerated</c>, <c>skipped</c>, …) — other tests'
/// meters are legitimately present too. Assert on the specific item belonging to
/// the meter a test created instead.
/// </para>
/// </summary>
public static class SharedBillingPeriod
{
    // Relative to "now": readings can only be backdated 35 days
    // (IngestionService.MaxBackdating), so a fixed calendar date would eventually
    // drift outside that window and start failing for a reason that has nothing to
    // do with the code under test.
    public static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.Date.AddDays(-14);
    public static readonly DateTimeOffset End = DateTimeOffset.UtcNow.Date.AddDays(-7);
}
