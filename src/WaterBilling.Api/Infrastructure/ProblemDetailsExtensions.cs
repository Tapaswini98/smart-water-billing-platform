using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Every error this API returns is an RFC 9457 problem document, with a stable
/// <c>type</c> URI a client can branch on. Free-text messages change; URIs do not.
/// </summary>
public static class ApiProblems
{
    public const string BaseUri = "https://docs.waterbilling.local/problems/";

    public static ProblemHttpResult NotFound(string detail, string resource) =>
        TypedResults.Problem(
            title: $"{resource} not found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound,
            type: BaseUri + "not-found");

    public static ProblemHttpResult Conflict(string title, string detail, string problemType) =>
        TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            type: BaseUri + problemType);

    public static ProblemHttpResult Forbidden(string detail) =>
        TypedResults.Problem(
            title: "Access denied",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            type: BaseUri + "forbidden");

    public static ProblemHttpResult UnprocessableEntity(string title, string detail, string problemType) =>
        TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: BaseUri + problemType);

    public static ValidationProblem Validation(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(
            errors: errors,
            title: "One or more validation errors occurred.",
            type: BaseUri + "validation-failed");
}
