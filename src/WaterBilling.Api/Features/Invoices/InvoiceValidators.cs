using FluentValidation;

namespace WaterBilling.Api.Features.Invoices;

public sealed class GenerateInvoicesRequestValidator : AbstractValidator<GenerateInvoicesRequest>
{
    public GenerateInvoicesRequestValidator()
    {
        // Both or neither: half a period is ambiguous, and guessing the other half
        // would quietly bill a span nobody asked for.
        RuleFor(r => r)
            .Must(r => (r.PeriodStartUtc is null) == (r.PeriodEndUtc is null))
            .WithMessage("Supply both periodStartUtc and periodEndUtc, or neither to bill the previous calendar month.")
            .OverridePropertyName(nameof(GenerateInvoicesRequest.PeriodStartUtc));

        RuleFor(r => r.PeriodEndUtc)
            .GreaterThan(r => r.PeriodStartUtc!.Value)
            .When(r => r.PeriodStartUtc.HasValue && r.PeriodEndUtc.HasValue)
            .WithMessage("The period end must be after its start.");
    }
}

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(r => r.Amount)
            .GreaterThan(0m).WithMessage("Payment amount must be greater than zero.")
            .LessThanOrEqualTo(10_000_000m).WithMessage("Payment amount exceeds the permitted maximum.");

        RuleFor(r => r.Provider)
            .NotEmpty().WithMessage("Provider is required.")
            .MaximumLength(64);

        RuleFor(r => r.ProviderReference).MaximumLength(128);
        RuleFor(r => r.IdempotencyKey).MaximumLength(128);
    }
}
