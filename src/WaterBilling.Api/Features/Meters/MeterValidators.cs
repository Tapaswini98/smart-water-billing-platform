using FluentValidation;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Api.Features.Meters;

public sealed class CreateMeterRequestValidator : AbstractValidator<CreateMeterRequest>
{
    public CreateMeterRequestValidator()
    {
        RuleFor(r => r.SerialNumber)
            .NotEmpty().WithMessage("Serial number is required — it is how field staff identify the unit.")
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("Serial number may contain only letters, digits, dots, hyphens and underscores.");

        RuleFor(r => r.Model).MaximumLength(128);
        RuleFor(r => r.LocationDescription).MaximumLength(512);

        RuleFor(r => r.InstalledAtUtc)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddDays(1))
            .When(r => r.InstalledAtUtc.HasValue)
            .WithMessage("Installation date cannot be in the future.");
    }
}

public sealed class UpdateMeterRequestValidator : AbstractValidator<UpdateMeterRequest>
{
    public UpdateMeterRequestValidator()
    {
        RuleFor(r => r.Status)
            .NotEmpty()
            .Must(status => Enum.TryParse<MeterStatus>(status, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<MeterStatus>())}.");

        RuleFor(r => r.Model).MaximumLength(128);
        RuleFor(r => r.LocationDescription).MaximumLength(512);
    }
}

public sealed class SupplyStateRequestValidator : AbstractValidator<SupplyStateRequest>
{
    public SupplyStateRequestValidator()
    {
        RuleFor(r => r.DesiredState)
            .NotEmpty()
            .Must(state => Enum.TryParse<SupplyState>(state, ignoreCase: true, out _))
            .WithMessage($"Desired state must be one of: {string.Join(", ", Enum.GetNames<SupplyState>())}.");

        // Cutting off a household's water is consequential and contestable, so the
        // reason is mandatory and ends up in the audit log.
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("A reason is required — supply changes are auditable actions.")
            .MaximumLength(512);
    }
}

public sealed class IssueApiKeyRequestValidator : AbstractValidator<IssueApiKeyRequest>
{
    public IssueApiKeyRequestValidator()
    {
        RuleFor(r => r.Label).MaximumLength(128);
        RuleFor(r => r.ExpiresInDays)
            .InclusiveBetween(1, 3650)
            .When(r => r.ExpiresInDays.HasValue)
            .WithMessage("Expiry must be between 1 and 3650 days.");
    }
}
