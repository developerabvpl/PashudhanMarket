using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Infrastructure.Api;

/// <summary>
/// The one place a Result becomes an HTTP response. Controllers call these so that error
/// mapping stays consistent across every module.
/// </summary>
public static class ResultExtensions
{
    public static ActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new NoContentResult() : Problem(result);

    public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess ? new OkObjectResult(result.Value) : Problem(result);

    public static ActionResult<TValue> ToCreatedResult<TValue>(
        this Result<TValue> result,
        string routeName,
        Func<TValue, object> routeValues) =>
        result.IsSuccess
            ? new CreatedAtRouteResult(routeName, routeValues(result.Value), result.Value)
            : Problem(result);

    private static ObjectResult Problem(Result result)
    {
        var status = result.Error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        if (result.ValidationErrors.Count > 0)
        {
            var validationProblem = new ValidationProblemDetails(
                result.ValidationErrors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
            {
                Status = status,
                Title = result.Error.Message,
                Type = $"https://upbazaar.dev/errors/{result.Error.Code}",
            };

            return new ObjectResult(validationProblem) { StatusCode = status };
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = result.Error.Message,
            Type = $"https://upbazaar.dev/errors/{result.Error.Code}",
            Extensions = { ["code"] = result.Error.Code },
        };

        return new ObjectResult(problem) { StatusCode = status };
    }
}
