namespace UPBazaar.SharedKernel.Results;

/// <summary>
/// What kind of failure this is. The API layer maps each one to an HTTP status, so adding a
/// member here means deciding what it means over the wire.
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
}

/// <summary>
/// A machine-readable failure. <see cref="Code"/> is part of the contract and must stay
/// stable; <see cref="Message"/> is for humans and may change freely.
/// </summary>
/// <param name="Code">Dotted, stable identifier such as <c>catalog.product.not_found</c>.</param>
/// <param name="Message">Human-readable summary, suitable for a problem-details title.</param>
/// <param name="Type">How the API should present this failure.</param>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>The absence of an error. Carried by every successful result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
}
