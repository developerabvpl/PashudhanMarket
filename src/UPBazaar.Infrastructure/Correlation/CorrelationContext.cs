using Microsoft.AspNetCore.Http;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Infrastructure.Correlation;

/// <summary>
/// Reads the correlation id the middleware stamped onto the request.
///
/// Falls back to the ambient trace id, then to a fresh value, so background work started
/// outside a request still gets something to log against rather than an empty string.
/// </summary>
public sealed class CorrelationContext(IHttpContextAccessor httpContextAccessor) : ICorrelationContext
{
    /// <summary>Header the API reads on the way in and echoes on the way out.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>HttpContext item key, so the middleware and this reader agree.</summary>
    public const string ItemKey = "upbazaar.correlation-id";

    /// <inheritdoc />
    public string CorrelationId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;

            if (context?.Items.TryGetValue(ItemKey, out var value) == true && value is string id)
            {
                return id;
            }

            return System.Diagnostics.Activity.Current?.TraceId.ToString()
                ?? Guid.CreateVersion7().ToString("n");
        }
    }
}
