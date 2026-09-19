using FluentValidation;

namespace WaterBilling.Api.Features.Auth;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(256);

        RuleFor(r => r.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(256);
    }
}
