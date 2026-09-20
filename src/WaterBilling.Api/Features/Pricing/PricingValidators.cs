using FluentValidation;
using WaterBilling.Domain.Pricing;

namespace WaterBilling.Api.Features.Pricing;

public sealed class PublishVersionRequestValidator : AbstractValidator<PublishVersionRequest>
{
    public PublishVersionRequestValidator()
    {
        RuleFor(r => r.Mode)
            .NotEmpty()
            .Must(mode => Enum.TryParse<PricingMode>(mode, ignoreCase: true, out _))
            .WithMessage($"Mode must be one of: {string.Join(", ", Enum.GetNames<PricingMode>())}.");

        RuleFor(r => r.SlabMode)
            .Must(mode => mode is null || Enum.TryParse<SlabMode>(mode, ignoreCase: true, out _))
            .WithMessage($"Slab mode must be one of: {string.Join(", ", Enum.GetNames<SlabMode>())}.");

        RuleFor(r => r.FixedCharge)
            .GreaterThanOrEqualTo(0m).WithMessage("Fixed charge cannot be negative.");

        RuleFor(r => r.TaxRatePercent)
            .InclusiveBetween(0m, 100m).WithMessage("Tax rate must be between 0 and 100 percent.");

        When(IsFlatRate, () =>
        {
            RuleFor(r => r.RatePerM3)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("Rate per m3 cannot be negative.");
        });

        When(r => !IsFlatRate(r), () =>
        {
            RuleFor(r => r.Slabs)
                .NotNull().WithMessage("A slab plan must define at least one band.")
                .Must(slabs => slabs is { Count: > 0 })
                .WithMessage("A slab plan must define at least one band.");

            // Structural band rules (contiguity, an open-ended last band) live in the
            // domain's TariffValidator so that the API and the pricing engine cannot
            // disagree about what a valid tariff is. This only catches shape errors
            // that would make the domain check meaningless.
            RuleForEach(r => r.Slabs!).ChildRules(slab =>
            {
                slab.RuleFor(s => s.FromM3)
                    .GreaterThanOrEqualTo(0m).WithMessage("Band start cannot be negative.");
                slab.RuleFor(s => s.RatePerM3)
                    .GreaterThanOrEqualTo(0m).WithMessage("Band rate cannot be negative.");
            });
        });
    }

    private static bool IsFlatRate(PublishVersionRequest request) =>
        string.Equals(request.Mode, nameof(PricingMode.FlatRate), StringComparison.OrdinalIgnoreCase);
}

public sealed class CreatePricingPlanRequestValidator : AbstractValidator<CreatePricingPlanRequest>
{
    public CreatePricingPlanRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Plan name is required.")
            .MaximumLength(128);

        RuleFor(r => r.Description).MaximumLength(1024);

        RuleFor(r => r.Currency)
            .Length(3)
            .When(r => !string.IsNullOrEmpty(r.Currency))
            .WithMessage("Currency must be a three-letter ISO 4217 code, e.g. INR.");

        RuleFor(r => r.InitialVersion)
            .NotNull().WithMessage("An initial version is required — a plan with no rates cannot bill anything.")
            .SetValidator(new PublishVersionRequestValidator()!);
    }
}

public sealed class PriceQuoteRequestValidator : AbstractValidator<PriceQuoteRequest>
{
    public PriceQuoteRequestValidator()
    {
        RuleFor(r => r.ConsumptionM3)
            .GreaterThanOrEqualTo(0m).WithMessage("Consumption cannot be negative.")
            .LessThanOrEqualTo(1_000_000m).WithMessage("Consumption exceeds any plausible billing period.");
    }
}
