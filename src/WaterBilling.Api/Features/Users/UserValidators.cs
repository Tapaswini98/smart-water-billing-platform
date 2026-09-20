using FluentValidation;
using WaterBilling.Domain.Users;

namespace WaterBilling.Api.Features.Users;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    /// <summary>
    /// Long enough to resist offline guessing, with no composition rules. Length is
    /// what actually buys entropy; forcing a symbol mostly produces "Password1!".
    /// </summary>
    public const int MinimumPasswordLength = 10;

    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(256);

        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(MinimumPasswordLength)
            .WithMessage($"Password must be at least {MinimumPasswordLength} characters.")
            .MaximumLength(256);

        RuleFor(r => r.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200);

        RuleFor(r => r.Role)
            .NotEmpty()
            .Must(role => Enum.TryParse<UserRole>(role, ignoreCase: true, out _))
            .WithMessage($"Role must be one of: {string.Join(", ", Enum.GetNames<UserRole>())}.");

        RuleFor(r => r.PhoneNumber).MaximumLength(32);
        RuleFor(r => r.BillingAddress).MaximumLength(1024);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.PhoneNumber).MaximumLength(32);
        RuleFor(r => r.BillingAddress).MaximumLength(1024);
    }
}
