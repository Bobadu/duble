using System;

namespace Duble.Core.Results;

/// <summary>
/// An operation that either succeeded or failed for a reason the caller can handle — a missing file, a texture
/// format that cannot be decoded, a locked target. Programmer errors keep throwing.
/// </summary>
public readonly struct Result
{
    Result(bool success, Error error)
    {
        IsSuccess = success;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Ok() => new(true, default);
    public static Result Fail(string code, string message) => new(false, new Error(code, message));
    public static Result Fail(Error error) => new(false, error);
}

/// <summary>As <see cref="Result"/>, carrying a value when it succeeded.</summary>
public readonly struct Result<T>
{
    readonly T value;

    Result(bool success, T value, Error error)
    {
        IsSuccess = success;
        this.value = value;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    /// <summary>The value. Throws when the operation failed — check <see cref="IsSuccess"/> first.</summary>
    public T Value => IsSuccess ? value : throw new InvalidOperationException("the operation failed: " + Error);

    public static Result<T> Ok(T value) => new(true, value, default);
    public static Result<T> Fail(string code, string message) => new(false, default!, new Error(code, message));
    public static Result<T> Fail(Error error) => new(false, default!, error);

    /// <summary>Handles both outcomes in one expression.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
        => IsSuccess ? onSuccess(value) : onFailure(Error);
}
