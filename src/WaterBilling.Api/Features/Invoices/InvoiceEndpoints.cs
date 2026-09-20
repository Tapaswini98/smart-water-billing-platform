using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Billing;
using WaterBilling.Domain.Common;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Invoices;

public static class InvoiceEndpoints
{
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invoices").WithTags("Invoices");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser)
            .WithName("ListInvoices")
            .WithSummary("List invoices")
            .WithDescription("Admins see every invoice. A customer sees only their own — the filter is applied server-side and cannot be widened by a query parameter.")
            .Produces<InvoicePageResponse>();

        group.MapGet("/{invoiceId:guid}", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser)
            .WithName("GetInvoice")
            .WithSummary("Get an invoice with its full breakdown")
            .WithDescription("Returns every line item as stored at generation: band, units, rate and amount. Nothing is recomputed, so the invoice reads the same today as on the day it was issued.")
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{invoiceId:guid}/payments", RecordPaymentAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser)
            .WithName("RecordInvoicePayment")
            .WithSummary("Pay an invoice")
            .WithDescription("Records a payment against a mocked provider, with every attempt logged. Idempotent on the caller-supplied key, so a retried checkout cannot charge twice.")
            .WithValidation<RecordPaymentRequest>()
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var admin = app.MapGroup("/api/v1/billing-runs")
            .WithTags("Billing")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        admin.MapPost("/", GenerateAsync)
            .WithName("GenerateInvoices")
            .WithSummary("Generate invoices for a period")
            .WithDescription(
                "Defaults to the previous calendar month. Idempotent: a unique index on " +
                "(meter_id, period_start) means re-running cannot double-bill, and already-billed " +
                "meters are reported rather than treated as errors. Meters with no readings are " +
                "billed the standing charge and flagged, never silently skipped. Pass dryRun to " +
                "compute and report everything without persisting.")
            .WithValidation<GenerateInvoicesRequest>()
            .Produces<BillingRunResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        admin.MapGet("/", ListRunsAsync)
            .WithName("ListBillingRuns")
            .WithSummary("List billing runs")
            .Produces<IReadOnlyList<BillingRunResponse>>();

        admin.MapGet("/{runId:guid}", GetRunAsync)
            .WithName("GetBillingRun")
            .WithSummary("Get a billing run with per-meter outcomes")
            .Produces<BillingRunResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Created<BillingRunResponse>, ProblemHttpResult>> GenerateAsync(
        GenerateInvoicesRequest request,
        ClaimsPrincipal principal,
        BillingService billing,
        BillingCalendar calendar,
        WaterBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var period = request.PeriodStartUtc is { } start && request.PeriodEndUtc is { } end
            ? new DateRange(start, end)
            : calendar.PreviousMonth();

        if (period.End <= period.Start)
        {
            return ApiProblems.UnprocessableEntity(
                "Invalid billing period",
                $"The period end ({period.End:O}) must be after its start ({period.Start:O}).",
                "invalid-period");
        }

        if (period.Start >= DateTimeOffset.UtcNow)
        {
            return ApiProblems.UnprocessableEntity(
                "Billing period is in the future",
                "A period that has not started cannot be billed — there is no consumption to measure yet.",
                "future-period");
        }

        var run = await billing.GenerateAsync(
            period,
            principal.GetUserId(),
            request.DryRun,
            request.IssueImmediately,
            cancellationToken);

        var response = await ToRunResponseAsync(run, db, request.DryRun, cancellationToken);

        return TypedResults.Created(
            request.DryRun ? null : $"/api/v1/billing-runs/{run.Id}",
            response);
    }

    private static async Task<Ok<IReadOnlyList<BillingRunResponse>>> ListRunsAsync(
        WaterBillingDbContext db,
        CancellationToken cancellationToken,
        int limit = 20)
    {
        var runs = await db.BillingRuns
            .AsNoTracking()
            .Include(r => r.Items)
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        var responses = new List<BillingRunResponse>(runs.Count);
        foreach (var run in runs)
        {
            responses.Add(await ToRunResponseAsync(run, db, isDryRun: run.IsDryRun, cancellationToken));
        }

        return TypedResults.Ok<IReadOnlyList<BillingRunResponse>>(responses);
    }

    private static async Task<Results<Ok<BillingRunResponse>, ProblemHttpResult>> GetRunAsync(
        Guid runId,
        WaterBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var run = await db.BillingRuns
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        return run is null
            ? ApiProblems.NotFound($"No billing run exists with id {runId}.", "Billing run")
            : TypedResults.Ok(await ToRunResponseAsync(run, db, run.IsDryRun, cancellationToken));
    }

    private static async Task<Ok<InvoicePageResponse>> ListAsync(
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        CancellationToken cancellationToken,
        Guid? meterId = null,
        Guid? customerId = null,
        string? status = null,
        int page = 1,
        int pageSize = 25)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Invoices.AsNoTracking();

        // Narrow to the caller BEFORE applying their filters, so a customerId
        // parameter can never widen what a customer sees.
        if (!principal.IsAdmin())
        {
            var callerId = principal.GetUserId();
            query = query.Where(i => i.CustomerId == callerId);
        }
        else if (customerId is { } filterCustomerId)
        {
            query = query.Where(i => i.CustomerId == filterCustomerId);
        }

        if (meterId is { } filterMeterId)
        {
            query = query.Where(i => i.MeterId == filterMeterId);
        }

        if (status is not null && Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(i => i.Status == parsedStatus);
        }

        var total = await query.CountAsync(cancellationToken);

        var invoices = await query
            .Include(i => i.LineItems.OrderBy(l => l.SortOrder))
            .Include(i => i.Meter)
            .Include(i => i.Customer)
            .OrderByDescending(i => i.PeriodStartUtc)
            .ThenBy(i => i.InvoiceNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new InvoicePageResponse(
            page, pageSize, total, [.. invoices.Select(ToResponse)]));
    }

    private static async Task<Results<Ok<InvoiceResponse>, ProblemHttpResult>> GetAsync(
        Guid invoiceId,
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(i => i.LineItems.OrderBy(l => l.SortOrder))
            .Include(i => i.Meter)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return ApiProblems.NotFound($"No invoice exists with id {invoiceId}.", "Invoice");
        }

        if (!principal.IsAdmin() && invoice.CustomerId != principal.GetUserId())
        {
            return ApiProblems.Forbidden("This invoice belongs to another account.");
        }

        return TypedResults.Ok(ToResponse(invoice));
    }

    private static async Task<Results<Created<PaymentResponse>, ProblemHttpResult>> RecordPaymentAsync(
        Guid invoiceId,
        RecordPaymentRequest request,
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return ApiProblems.NotFound($"No invoice exists with id {invoiceId}.", "Invoice");
        }

        var callerId = principal.GetUserId();

        if (!principal.IsAdmin() && invoice.CustomerId != callerId)
        {
            return ApiProblems.Forbidden("This invoice belongs to another account.");
        }

        if (invoice.Status is InvoiceStatus.Void)
        {
            return ApiProblems.Conflict(
                "Invoice is void",
                "A voided invoice cannot be paid. If this was a mistake, re-issue the invoice.",
                "invoice-void");
        }

        // A retried checkout must be recognised as the same payment, not charged
        // twice. A unique index backs this up if two retries race.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var replay = await db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, cancellationToken);

            if (replay is not null)
            {
                return TypedResults.Created(
                    $"/api/v1/invoices/{invoiceId}/payments/{replay.Id}",
                    ToPaymentResponse(replay, invoice));
            }
        }

        if (request.Amount > invoice.AmountDue)
        {
            return ApiProblems.UnprocessableEntity(
                "Payment exceeds the amount due",
                $"{request.Amount:0.00} {invoice.Currency} was offered against an outstanding balance of {invoice.AmountDue:0.00}. Overpayment is not supported.",
                "overpayment");
        }

        var now = timeProvider.GetUtcNow();

        var payment = new Payment
        {
            InvoiceId = invoice.Id,
            PaidByUserId = callerId,
            Amount = request.Amount,
            Currency = invoice.Currency,
            // A mocked provider: no real gateway credentials exist in this repository,
            // and none should. The recording, idempotency and event log are the parts
            // worth demonstrating; the network call to a real PSP is not.
            Provider = string.IsNullOrWhiteSpace(request.Provider) ? "mock" : request.Provider,
            ProviderReference = request.ProviderReference ?? $"mock_{Guid.CreateVersion7():N}",
            IdempotencyKey = request.IdempotencyKey,
            Status = PaymentStatus.Succeeded,
            InitiatedAtUtc = now,
            CompletedAtUtc = now
        };

        payment.Events.Add(new PaymentEvent
        {
            PaymentId = payment.Id,
            EventType = "payment.succeeded",
            OccurredAtUtc = now,
            PayloadJson = $$"""{"provider":"{{payment.Provider}}","reference":"{{payment.ProviderReference}}","amount":{{request.Amount}}}"""
        });

        db.Payments.Add(payment);

        invoice.AmountPaid += request.Amount;

        if (invoice.AmountDue <= 0m)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAtUtc = now;
        }

        audit.Record("payment.recorded", nameof(Payment), payment.Id, new
        {
            invoice.InvoiceNumber,
            request.Amount,
            payment.Provider
        });

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/invoices/{invoiceId}/payments/{payment.Id}",
            ToPaymentResponse(payment, invoice));
    }

    private static async Task<BillingRunResponse> ToRunResponseAsync(
        BillingRun run,
        WaterBillingDbContext db,
        bool isDryRun,
        CancellationToken cancellationToken)
    {
        var meterIds = run.Items.Select(i => i.MeterId).Distinct().ToArray();

        var serials = await db.Meters
            .AsNoTracking()
            .Where(m => meterIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.SerialNumber, cancellationToken);

        var invoiceIds = run.Items.Where(i => i.InvoiceId is not null).Select(i => i.InvoiceId!.Value).ToArray();

        var invoiceNumbers = invoiceIds.Length == 0
            ? []
            : await db.Invoices
                .AsNoTracking()
                .Where(i => invoiceIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, cancellationToken);

        return new BillingRunResponse(
            run.Id,
            run.PeriodStartUtc,
            run.PeriodEndUtc,
            run.Status.ToString(),
            isDryRun,
            run.MetersConsidered,
            run.InvoicesGenerated,
            run.Skipped,
            run.Failed,
            run.TotalBilledAmount,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            [.. run.Items.Select(item => new BillingRunItemResponse(
                item.MeterId,
                serials.GetValueOrDefault(item.MeterId, "unknown"),
                item.Outcome.ToString(),
                item.InvoiceId,
                item.InvoiceId is { } id ? invoiceNumbers.GetValueOrDefault(id) : null,
                item.ConsumptionM3,
                item.TotalAmount,
                DescribeFlags(item.ConsumptionFlags),
                item.Message))]);
    }

    private static InvoiceResponse ToResponse(Invoice invoice) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.MeterId,
        invoice.Meter?.SerialNumber ?? "unknown",
        invoice.CustomerId,
        invoice.Customer?.FullName,
        invoice.PeriodStartUtc,
        invoice.PeriodEndUtc,
        invoice.OpeningTotalM3,
        invoice.ClosingTotalM3,
        invoice.OpeningReadingAtUtc,
        invoice.ClosingReadingAtUtc,
        invoice.ConsumptionM3,
        invoice.ReadingCount,
        DescribeFlags(invoice.ConsumptionFlags),
        invoice.PricingPlanName,
        invoice.PricingPlanVersionNumber,
        invoice.FixedCharge,
        invoice.UsageCharge,
        invoice.TaxRatePercent,
        invoice.TaxAmount,
        invoice.TotalAmount,
        invoice.AmountPaid,
        invoice.AmountDue,
        invoice.Currency,
        invoice.Status.ToString(),
        invoice.IssuedAtUtc,
        invoice.DueAtUtc,
        invoice.PaidAtUtc,
        [.. invoice.LineItems
            .OrderBy(l => l.SortOrder)
            .Select(l => new InvoiceLineResponse(
                l.SortOrder,
                l.Kind.ToString(),
                l.Description,
                l.BandFromM3,
                l.BandToM3,
                l.UnitsM3,
                l.RatePerM3,
                l.Amount))]);

    private static PaymentResponse ToPaymentResponse(Payment payment, Invoice invoice) => new(
        payment.Id,
        payment.InvoiceId,
        payment.Amount,
        payment.Currency,
        payment.Status.ToString(),
        payment.Provider,
        payment.ProviderReference,
        payment.InitiatedAtUtc,
        payment.CompletedAtUtc,
        payment.FailureReason,
        invoice.AmountDue,
        invoice.Status.ToString());

    private static IReadOnlyList<string> DescribeFlags(ConsumptionFlags flags) =>
        flags is ConsumptionFlags.None
            ? []
            : [.. Enum.GetValues<ConsumptionFlags>()
                .Where(f => f != ConsumptionFlags.None && flags.HasFlag(f))
                .Select(f => f.ToString())];
}
