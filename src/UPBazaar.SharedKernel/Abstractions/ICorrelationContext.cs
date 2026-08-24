namespace UPBazaar.SharedKernel.Abstractions;

/// <summary>
/// The correlation id for the current request. Stamped by middleware, echoed in the response
/// header, attached to every log line, and copied onto audit rows and outbox messages so one
/// identifier ties a user action to everything it caused.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}
