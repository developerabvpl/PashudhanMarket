using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPBazaar.SharedKernel.Results;

namespace UPBazaar.Infrastructure.Api;

/// <summary>
/// The one place a <see cref="Result"/> becomes an HTTP response.
///
/// Controllers call these so that error mapping is identical across every module: an error
/// type decides the status code once, here, rather than in each handler.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Maps a valueless result: 204 on success, problem details on failure.</summary>
    public static ActionResult ToActionResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? new NoContentResult() : Problem(result);
    }

    /// <summary>Maps a result carrying a value: 200 with the body, or problem details.</summary>
    public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? new OkObjectResult(result.Value) : Problem(result);
    }

    /// <summary>Maps a creation result to 201 with a Location header.</summary>
    public static ActionResult<TValue> ToCreatedResult<TValue>(
        this Result<TValue> result,
        string routeName,
        Func<TValue, object> routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(routeValues);

        return result.IsSuccess
            ? new CreatedAtRouteResult(routeName, routeValues(result.Value), result.Value)
            : Problem(result);
    }

    private static ObjectResult Problem(Result result)
    {
        var status = result.Error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
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
