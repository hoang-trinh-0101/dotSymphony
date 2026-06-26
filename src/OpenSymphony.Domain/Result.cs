namespace OpenSymphony.Domain;

// ht: minimal unit type for void-success Results.
public readonly record struct Unit
{
    public static Unit Value => default;
}

// ht: minimal discriminated result. Only abstraction in the domain layer.
public readonly record struct Result<T, E>
{
    private readonly T _value;
    private readonly E _error;
    public bool IsOk { get; }
    public bool IsErr => !IsOk;

    private Result(T value, E error, bool isOk)
    {
        _value = value;
        _error = error;
        IsOk = isOk;
    }

    public T Value => IsOk ? _value : throw new InvalidOperationException("Result is Err");
    public E Error => IsErr ? _error : throw new InvalidOperationException("Result is Ok");

    public static Result<T, E> Ok(T value) => new(value, default!, true);
    public static Result<T, E> Err(E error) => new(default!, error, false);

    public TResult Match<TResult>(Func<T, TResult> ok, Func<E, TResult> err)
        => IsOk ? ok(_value) : err(_error);
}
