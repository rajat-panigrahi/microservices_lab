namespace StrategyOps.BuildingBlocks.Results;

public enum ResultStatus
{
    Ok,
    Created,
    Invalid,
    NotFound,
    Conflict,
    Unavailable
}

public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

/// <summary>
/// A handler's answer, free of HTTP concepts. The endpoint translates it (see
/// <c>Api/ResultExtensions</c>), which is what lets a slice handler be unit tested
/// without spinning up a web host.
/// </summary>
public class Result
{
    protected Result(ResultStatus status, Error error)
    {
        Status = status;
        Error = error;
    }

    public ResultStatus Status { get; }

    public Error Error { get; }

    public bool IsSuccess => Status is ResultStatus.Ok or ResultStatus.Created;

    public static Result Ok() => new(ResultStatus.Ok, Error.None);

    public static Result Invalid(string code, string message) => new(ResultStatus.Invalid, new Error(code, message));

    public static Result NotFound(string code, string message) => new(ResultStatus.NotFound, new Error(code, message));

    public static Result Conflict(string code, string message) => new(ResultStatus.Conflict, new Error(code, message));

    public static Result Unavailable(string code, string message) => new(ResultStatus.Unavailable, new Error(code, message));
}

public sealed class Result<T> : Result
{
    private Result(ResultStatus status, Error error, T? value)
        : base(status, error) => Value = value;

    public T? Value { get; }

    public static Result<T> Ok(T value) => new(ResultStatus.Ok, Error.None, value);

    public static Result<T> Created(T value) => new(ResultStatus.Created, Error.None, value);

    public static new Result<T> Invalid(string code, string message) => new(ResultStatus.Invalid, new Error(code, message), default);

    public static new Result<T> NotFound(string code, string message) => new(ResultStatus.NotFound, new Error(code, message), default);

    public static new Result<T> Conflict(string code, string message) => new(ResultStatus.Conflict, new Error(code, message), default);

    public static new Result<T> Unavailable(string code, string message) => new(ResultStatus.Unavailable, new Error(code, message), default);
}
