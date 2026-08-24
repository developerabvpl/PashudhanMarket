using Serilog.Context;
using UPBazaar.Infrastructure.Correlation;

namespace UPBazaar.Api.Middleware;

/// <summary>
/// Gives every request a correlation id, echoes it back, and pushes it onto the log context.
///
/// An inbound id is honoured so a call arriving from the storefront or another service keeps
/// one identifier end to end; otherwise a fresh one is minted. Every log line, audit row and
/// outbox message written during the request carries it, which is what turns "a user reported
/// an error at 14:32" into a single query.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Resolve(context);

        context.Items[CorrelationContext.ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;

        // Set before the response starts: once headers are sent it is too late to add one.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Takes the inbound header when it is present and sane. A caller-supplied value ends up
    /// in logs and in the database, so it is length-capped and stripped of anything that is
    /// not safe to echo.
    /// </summary>
    private static string Resolve(HttpContext context)
    {
        var inbound = context.Request.Headers[CorrelationContext.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(inbound))
        {
            return Guid.CreateVersion7().ToString("n");
        }

        var cleaned = new string([.. inbound
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            .Take(MaxLength)]);

        return cleaned.Length == 0 ? Guid.CreateVersion7().ToString("n") : cleaned;
    }
}

/// <summary>Registration helper for <see cref="CorrelationIdMiddleware"/>.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>Adds correlation id handling. Register before request logging.</summary>
    /// <param name="app">Application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
