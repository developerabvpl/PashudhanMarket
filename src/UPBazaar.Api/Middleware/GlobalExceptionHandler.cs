using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace UPBazaar.Api.Middleware;

/// <summary>
/// Turns an unhandled exception into an RFC 7807 problem details response.
///
/// The body deliberately says nothing about what went wrong beyond a correlation id: stack
/// traces and exception messages leak schema and file paths. The detail lives in the log,
/// found by the same id the caller is holding.
/// </summary>
/// <remarks>
/// Registered as a singleton, so it reads the correlation id from the request rather than
/// injecting the scoped correlation context - a captive dependency the container rejects.
/// The middleware puts the same value on TraceIdentifier.
/// </remarks>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var correlationId = httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path} (correlation {CorrelationId})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);

        var (status, title) = Classify(exception);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = $"https://upbazaar.dev/errors/http.{status}",
                Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
                Extensions =
                {
                    ["correlationId"] = correlationId,
                },
            },
        });
    }

    /// <summary>
    /// Maps the few exception types that genuinely mean something other than "server fault".
    /// Everything else is a 500, because a surprise is not a client error.
    /// </summary>
    private static (int Status, string Title) Classify(Exception exception) => exception switch
    {
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "The request could not be read."),

        // Two changes to the same record at once, where the handler did not say what to do: the
        // later one lost, and trying again on fresh data is the answer, not a server fault.
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "It was changed by someone else at the same time. Reload and try again."),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "You do not have access to this resource."),
        OperationCanceledException => (StatusCodesExtra.ClientClosedRequest, "The request was cancelled."),
        NotSupportedException => (StatusCodes.Status501NotImplemented, "That operation is not supported yet."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };
}

/// <summary>Status codes ASP.NET Core does not name.</summary>
internal static class StatusCodesExtra
{
    /// <summary>nginx's code for a client that hung up before the response was written.</summary>
    public const int ClientClosedRequest = 499;
}
