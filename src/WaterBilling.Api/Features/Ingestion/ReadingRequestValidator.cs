using FluentValidation;

namespace WaterBilling.Api.Features.Ingestion;

/// <summary>
/// Shape-level validation only. Anything that needs history — duplicates, resets,
/// out-of-order arrival — belongs in <see cref="IngestionService"/>, because those
/// are billing decisions rather than malformed input.
/// </summary>
public sealed class ReadingRequestValidator : AbstractValidator<ReadingRequest>
{
    public ReadingRequestValidator()
    {
        RuleFor(r => r.ReadingAtUtc)
            .NotEmpty()
            .WithMessage("readingAtUtc is required. Send the time the meter took the reading, in ISO 8601 with an offset, e.g. 2026-09-19T10:15:00Z.");

        RuleFor(r => r.TotalM3)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("totalM3 is a cumulative register value and cannot be negative.")
            .LessThan(1_000_000_000m)
            .WithMessage("totalM3 exceeds any plausible register value; check the units (m3, not litres).");

        RuleFor(r => r.FlowM3PerHour)
            .GreaterThanOrEqualTo(0m)
            .When(r => r.FlowM3PerHour.HasValue)
            .WithMessage("flowM3PerHour cannot be negative. A reverse-flow condition should be reported as a separate alarm, not as negative flow.");

        RuleFor(r => r.FlowM3PerHour)
            .LessThanOrEqualTo(10_000m)
            .When(r => r.FlowM3PerHour.HasValue)
            .WithMessage("flowM3PerHour exceeds any plausible domestic or commercial meter rating; check the units (m3/hr, not litres/min).");
    }
}

public sealed class ReadingBatchRequestValidator : AbstractValidator<ReadingBatchRequest>
{
    /// <summary>
    /// Caps a single upload so one device cannot monopolise a connection. At the
    /// design cadence this is roughly ten days of buffered readings, which is more
    /// than any realistic store-and-forward window.
    /// </summary>
    public const int MaxBatchSize = 1000;

    public ReadingBatchRequestValidator()
    {
        RuleFor(b => b.Readings)
            .NotEmpty()
            .WithMessage("Send at least one reading.")
            .Must(r => r.Count <= MaxBatchSize)
            .WithMessage($"A batch may contain at most {MaxBatchSize} readings. Split larger uploads.");

        RuleForEach(b => b.Readings).SetValidator(new ReadingRequestValidator());

        RuleFor(b => b.Readings)
            .Must(r => r.Select(x => x.ReadingAtUtc).Distinct().Count() == r.Count)
            .When(r => r.Readings is { Count: > 0 })
            .WithMessage("The batch contains two readings with the same timestamp. Deduplicate before sending; the server cannot tell which one is authoritative.");
    }
}
