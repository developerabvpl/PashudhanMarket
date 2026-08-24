namespace UPBazaar.SharedKernel.Results;

/// <summary>
/// The outcome of a command or query. Handlers return this rather than throwing for expected
/// failures, and the API maps it to a status code in one place.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    /// <summary>Per-property messages, surfaced as an RFC 7807 validation problem.</summary>
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; init; }
        = new Dictionary<string, string[]>();

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    public static Result ValidationFailure(IReadOnlyDictionary<string, string[]> errors) =>
        new(false, Error.Validation("validation.failed", "One or more validation errors occurred."))
        {
            ValidationErrors = errors,
        };

    public static Result<TValue> ValidationFailure<TValue>(IReadOnlyDictionary<string, string[]> errors) =>
        new(default, false, Error.Validation("validation.failed", "One or more validation errors occurred."))
        {
            ValidationErrors = errors,
        };
}

/// <summary>A <see cref="Result"/> that carries a value when it succeeds.</summary>
/// <typeparam name="TValue">Type produced on success.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The value. Reading it on a failed result is a programming error and throws.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
