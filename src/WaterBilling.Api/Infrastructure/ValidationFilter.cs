using FluentValidation;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Runs the FluentValidation validator registered for <typeparamref name="TRequest"/>
/// before the handler sees it, and returns an RFC 9457 validation problem if it fails.
/// <para>
/// An endpoint filter rather than a line at the top of every handler: validation that
/// is opt-in gets forgotten, and "the handler is only reached with a valid request"
/// is a much easier invariant to reason about.
/// </para>
/// </summary>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return ApiProblems.Validation(errors);
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) =>
        builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
