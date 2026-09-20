using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Features.Consumption;
using WaterBilling.Api.Features.Pricing;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Billing;
using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;
using WaterBilling.Domain.Pricing;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Invoices;

/// <summary>
/// Generates invoices for a billing period.
/// <para>
/// The contract (ADR-0007): re-running a period cannot double-bill, because a unique
/// index on <c>(meter_id, period_start)</c> makes it impossible at the storage layer
/// rather than merely unlikely in this code. One meter failing does not abort the
/// run. Every meter gets an outcome with a reason, because a billing run that
/// silently skips meters is how revenue goes missing unnoticed.
/// </para>
/// </summary>
public sealed class BillingService(
    WaterBillingDbContext db,
    TariffResolver tariffResolver,
    BillingCalendar calendar,
    AuditService audit,
    TimeProvider timeProvider,
    ILogger<BillingService> logger)
{
    public async Task<BillingRun> GenerateAsync(
        DateRange period,
        Guid? triggeredByUserId,
        bool dryRun,
        bool issueImmediately,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var run = new BillingRun
        {
            PeriodStartUtc = period.Start,
            PeriodEndUtc = period.End,
            TriggeredByUserId = triggeredByUserId,
            StartedAtUtc = now,
            IsDryRun = dryRun,
            Status = BillingRunStatus.Running
        };

        // Decommissioned meters are excluded: they cannot have consumed anything in a
        // period that ended after they were removed, and billing them would generate
        // zero-consumption invoices nobody should receive.
        var meters = await db.Meters
            .AsNoTracking()
            .Where(m => m.Status != MeterStatus.Decommissioned)
            // Ordered before the projection: EF cannot translate an OrderBy over a
            // projected struct, and a stable serial order makes run reports diffable.
            .OrderBy(m => m.SerialNumber)
            .Select(m => new MeterForBilling(
                m.Id,
                m.SerialNumber,
                m.CustomerId,
                m.PricingPlanId,
                m.Status,
                m.InstalledAtUtc))
            .ToListAsync(cancellationToken);

        run.MetersConsidered = meters.Count;

        // Invoice numbers are sequential within a period. Starting from what already
        // exists means a re-run after a partial failure continues the sequence instead
        // of colliding with numbers already issued.
        var sequence = await db.Invoices.CountAsync(i => i.PeriodStartUtc == period.Start, cancellationToken);

        foreach (var meter in meters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var item = await BillMeterAsync(run, meter, period, dryRun, issueImmediately, ++sequence, now, cancellationToken);
                run.Items.Add(item);

                switch (item.Outcome)
                {
                    case BillingOutcome.Generated:
                    case BillingOutcome.GeneratedWithoutData:
                        run.InvoicesGenerated++;
                        run.TotalBilledAmount += item.TotalAmount ?? 0m;
                        break;
                    case BillingOutcome.AlreadyBilled:
                    case BillingOutcome.Skipped:
                        run.Skipped++;
                        sequence--; // No invoice was issued, so the number is not consumed.
                        break;
                    case BillingOutcome.Failed:
                        run.Failed++;
                        sequence--;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad meter must not cost the utility a month of billing for
                // every other meter on the estate.
                logger.LogError(ex, "Billing failed for meter {Serial} ({MeterId}).", meter.SerialNumber, meter.Id);

                run.Items.Add(new BillingRunItem
                {
                    BillingRunId = run.Id,
                    MeterId = meter.Id,
                    Outcome = BillingOutcome.Failed,
                    Message = $"Unexpected error: {ex.Message}"
                });

                run.Failed++;
                sequence--;
            }
        }

        run.CompletedAtUtc = timeProvider.GetUtcNow();
        run.Status = run.Failed > 0 ? BillingRunStatus.CompletedWithErrors : BillingRunStatus.Completed;

        if (dryRun)
        {
            // A dry run computes and reports everything, then persists nothing —
            // including itself. The caller gets the full picture without committing
            // invoice numbers or leaving a run record that never produced invoices.
            db.ChangeTracker.Clear();
            return run;
        }

        db.BillingRuns.Add(run);
        audit.Record("billing_run.executed", nameof(BillingRun), run.Id, new
        {
            Period = period.ToString(),
            run.InvoicesGenerated,
            run.Skipped,
            run.Failed,
            run.TotalBilledAmount
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Billing run {RunId} for {Period}: {Generated} generated, {Skipped} skipped, {Failed} failed, {Total} billed.",
            run.Id, period, run.InvoicesGenerated, run.Skipped, run.Failed, run.TotalBilledAmount);

        return run;
    }

    private async Task<BillingRunItem> BillMeterAsync(
        BillingRun run,
        MeterForBilling meter,
        DateRange period,
        bool dryRun,
        bool issueImmediately,
        int sequence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = new BillingRunItem
        {
            BillingRunId = run.Id,
            MeterId = meter.Id,
            Outcome = BillingOutcome.Skipped
        };

        // Idempotency check first. The unique index is still the authority; this just
        // keeps the common re-run case from reaching the database as an exception.
        var existing = await db.Invoices
            .AsNoTracking()
            .Where(i => i.MeterId == meter.Id && i.PeriodStartUtc == period.Start)
            .Select(i => new { i.Id, i.InvoiceNumber, i.ConsumptionM3, i.TotalAmount })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            item.Outcome = BillingOutcome.AlreadyBilled;
            item.InvoiceId = existing.Id;
            item.ConsumptionM3 = existing.ConsumptionM3;
            item.TotalAmount = existing.TotalAmount;
            item.Message = $"Invoice {existing.InvoiceNumber} already covers this period. Nothing was changed.";
            return item;
        }

        if (meter.CustomerId is null)
        {
            item.Message = "No customer is assigned to this meter, so there is nobody to bill. Assign it before the next run.";
            return item;
        }

        if (meter.PricingPlanId is null)
        {
            item.Message = "No pricing plan is assigned to this meter. Assign a plan before the next run.";
            return item;
        }

        if (meter.InstalledAtUtc >= period.End)
        {
            item.Message = $"Meter was installed on {meter.InstalledAtUtc:yyyy-MM-dd}, after this period ended.";
            return item;
        }

        // ADR-0004: the version in force at period END prices the whole period.
        var tariff = await tariffResolver.ResolveAsync(meter.PricingPlanId.Value, period.End, cancellationToken);

        if (tariff is null)
        {
            item.Outcome = BillingOutcome.Failed;
            item.Message = $"The meter's pricing plan has no version in force at {period.End:yyyy-MM-dd}. Publish a version covering this period and re-run.";
            return item;
        }

        var consumption = await ConsumptionEndpoints.ComputeAsync(db, meter.Id, period, cancellationToken);
        var breakdown = PricingEngine.Price(tariff, consumption.ConsumptionM3);

        var invoice = BuildInvoice(run, meter, period, consumption, breakdown, sequence, issueImmediately, now);

        item.Outcome = consumption.Flags.HasFlag(ConsumptionFlags.NoData)
            // Deliberately NOT skipped: a silent gap in the invoice series is how a
            // dead meter goes unnoticed for a year. The standing charge is still due,
            // and the flag tells the operator to investigate.
            ? BillingOutcome.GeneratedWithoutData
            : BillingOutcome.Generated;

        item.InvoiceId = invoice.Id;
        item.ConsumptionM3 = consumption.ConsumptionM3;
        item.TotalAmount = invoice.TotalAmount;
        item.ConsumptionFlags = consumption.Flags;
        item.Message = DescribeOutcome(consumption);

        if (!dryRun)
        {
            db.Invoices.Add(invoice);
        }

        return item;
    }

    private Invoice BuildInvoice(
        BillingRun run,
        MeterForBilling meter,
        DateRange period,
        ConsumptionResult consumption,
        PriceBreakdown breakdown,
        int sequence,
        bool issueImmediately,
        DateTimeOffset now)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = calendar.InvoiceNumber(period, sequence),
            MeterId = meter.Id,
            // Snapshotted, so the invoice stays with whoever held the meter during the
            // period even if it is reassigned later.
            CustomerId = meter.CustomerId,
            PeriodStartUtc = period.Start,
            PeriodEndUtc = period.End,
            OpeningTotalM3 = consumption.OpeningTotalM3,
            ClosingTotalM3 = consumption.ClosingTotalM3,
            OpeningReadingAtUtc = consumption.OpeningReadingAtUtc,
            ClosingReadingAtUtc = consumption.ClosingReadingAtUtc,
            ConsumptionM3 = consumption.ConsumptionM3,
            ReadingCount = consumption.ReadingCount,
            ConsumptionFlags = consumption.Flags,
            PricingPlanId = breakdown.PlanId,
            PricingPlanVersionId = breakdown.PlanVersionId,
            PricingPlanVersionNumber = breakdown.PlanVersionNumber,
            PricingPlanName = breakdown.PlanName,
            FixedCharge = breakdown.FixedCharge,
            UsageCharge = breakdown.UsageCharge,
            TaxRatePercent = breakdown.TaxRatePercent,
            TaxAmount = breakdown.TaxAmount,
            TotalAmount = breakdown.Total,
            Currency = breakdown.Currency,
            BillingRunId = run.Id,
            Status = issueImmediately ? InvoiceStatus.Issued : InvoiceStatus.Draft,
            IssuedAtUtc = issueImmediately ? now : null,
            DueAtUtc = issueImmediately ? calendar.DueDateFor(period) : null
        };

        var order = 1;

        if (breakdown.FixedCharge > 0m)
        {
            invoice.LineItems.Add(new InvoiceLineItem
            {
                InvoiceId = invoice.Id,
                SortOrder = order++,
                Kind = InvoiceLineKind.FixedCharge,
                Description = "Standing charge",
                Amount = breakdown.FixedCharge
            });
        }

        // Every priced band is written onto the invoice. This is what lets a customer
        // disputing a bill two years from now see the exact bands and rates that
        // produced it, rather than a recomputation against today's tariff (ADR-0004).
        foreach (var line in breakdown.Lines)
        {
            invoice.LineItems.Add(new InvoiceLineItem
            {
                InvoiceId = invoice.Id,
                SortOrder = order++,
                Kind = InvoiceLineKind.Consumption,
                Description = line.Description,
                BandFromM3 = line.FromM3,
                BandToM3 = line.ToM3,
                UnitsM3 = line.UnitsM3,
                RatePerM3 = line.RatePerM3,
                Amount = line.Amount
            });
        }

        if (breakdown.TaxAmount > 0m)
        {
            invoice.LineItems.Add(new InvoiceLineItem
            {
                InvoiceId = invoice.Id,
                SortOrder = order,
                Kind = InvoiceLineKind.Tax,
                Description = $"Tax @ {breakdown.TaxRatePercent:0.##}%",
                Amount = breakdown.TaxAmount
            });
        }

        return invoice;
    }

    private static string DescribeOutcome(ConsumptionResult consumption)
    {
        if (consumption.Flags is ConsumptionFlags.None)
        {
            return $"Billed {consumption.ConsumptionM3:0.###} m3 from {consumption.ReadingCount} readings.";
        }

        var notes = new List<string>();

        if (consumption.Flags.HasFlag(ConsumptionFlags.NoData))
        {
            notes.Add("The meter reported nothing in this period; only the standing charge applies. Check whether it is offline.");
        }

        if (consumption.Flags.HasFlag(ConsumptionFlags.PartialPeriod))
        {
            notes.Add("No reading existed at the period start, so consumption is measured from the first reading inside it.");
        }

        if (consumption.Flags.HasFlag(ConsumptionFlags.InsufficientReadings))
        {
            notes.Add("Only one reading was available, so no consumption could be derived.");
        }

        if (consumption.Flags.HasFlag(ConsumptionFlags.MeterResetDuringPeriod))
        {
            notes.Add($"The register reset {consumption.ResetCount} time(s); consumption was summed across the discontinuity.");
        }

        if (consumption.Flags.HasFlag(ConsumptionFlags.StaleClosingReading))
        {
            notes.Add("The last reading is well before the period end; the meter may have stopped reporting.");
        }

        return string.Join(" ", notes);
    }

    private readonly record struct MeterForBilling(
        Guid Id,
        string SerialNumber,
        Guid? CustomerId,
        Guid? PricingPlanId,
        MeterStatus Status,
        DateTimeOffset InstalledAtUtc);
}
