using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WaterBilling.Domain.Pricing;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Last line of defence. Turns anything that escapes a handler into a problem
/// document, and — importantly — never leaks a stack trace outside Development.
/// An unhandled exception reaching here is a bug, so it is logged at Error with the
/// trace identifier the caller sees, making the two joinable in support.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, type) = Classify(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path} (trace {TraceId}).",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);
        }
        else
        {
            logger.LogWarning(
                "Request failed on {Method} {Path}: {Message}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = ApiProblems.BaseUri + type,
                Detail = status >= StatusCodes.Status500InternalServerError && !environment.IsDevelopment()
                    ? "An unexpected error occurred. Quote the traceId when reporting this."
                    : exception.Message
            }
        });
    }

    private static (int Status, string Title, string Type) Classify(Exception exception) => exception switch
    {
        InvalidTariffException => (StatusCodes.Status422UnprocessableEntity, "Pricing plan is invalid", "invalid-tariff"),
        ArgumentOutOfRangeException => (StatusCodes.Status400BadRequest, "Value out of range", "value-out-of-range"),
        ArgumentException => (StatusCodes.Status400BadRequest, "Invalid argument", "invalid-argument"),
        InvalidOperationException => (StatusCodes.Status409Conflict, "Operation not valid in the current state", "invalid-state"),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access denied", "forbidden"),
        // 499 is nginx's non-standard "client closed request"; it is not in StatusCodes.
        OperationCanceledException => (499, "Request cancelled", "request-cancelled"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "internal-error")
    };
}
