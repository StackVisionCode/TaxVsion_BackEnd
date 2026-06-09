namespace BuildingBlocks;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("A result marked as successful cannot have an associated error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("A result marked as failure must have an associated error.");

        IsSuccess = isSuccess;
        Error = error;
    }
    // Factory methods for creating success and failure results
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);



    // Factory methods for creating success and failure results with a value

    public static Result<T> Success<T>(T value) => new(true, Error.None, value);
    public static Result<T> Failure<T>(Error error) => new(false, error, default!);



}

// Generic version of Result to hold a value in case of success
public sealed class Result<T> : Result
{
    private readonly T _value;
    internal Result(bool isSuccess, Error error, T value) : base(isSuccess, error) => _value = value;


    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access the value of a failed result.");
}

