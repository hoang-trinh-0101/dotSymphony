using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Codex;

// ht: simplified Result<T> using Exception as error type (thiserror → Exception).
public readonly record struct Result<T>
{
    private readonly T _value;
    private readonly Exception? _error;
    public bool IsOk { get; }
    public bool IsErr => !IsOk;

    private Result(T value, Exception? error, bool isOk)
    {
        _value = value;
        _error = error;
        IsOk = isOk;
    }

    public T Value => IsOk ? _value : throw _error ?? new InvalidOperationException("Result is Err");
    public Exception Error => IsErr ? _error ?? new InvalidOperationException("Result is Err") : throw new InvalidOperationException("Result is Ok");

    public static Result<T> Ok(T value) => new(value, null, true);
    public static Result<T> Err(Exception error) => new(default!, error, false);
    public static Result<T> Err(string error) => new(default!, new Exception(error), false);

    public TResult Match<TResult>(Func<T, TResult> ok, Func<Exception, TResult> err)
        => IsOk ? ok(_value) : err(_error!);
}

// ht: unit result for void operations
public static class Result
{
    public static Result<Unit> Ok => Result<Unit>.Ok(Unit.Value);
    public static Result<Unit> Err(Exception error) => Result<Unit>.Err(error);
    public static Result<Unit> Err(string error) => Result<Unit>.Err(error);
}

public class CodexSchemaValidationError : Exception
{
    public CodexSchemaValidationError(string message) : base(message) { }
    public CodexSchemaValidationError(string message, Exception innerException) : base(message, innerException) { }

    public static CodexSchemaValidationError SchemaRead(string path, Exception source) =>
        new($"failed to read installed Codex app-server schema at {path}: {source.Message}", source);

    public static CodexSchemaValidationError SchemaParse(JsonException exception) =>
        new($"failed to parse installed Codex app-server schema JSON: {exception.Message}", exception);

    public static CodexSchemaValidationError SchemaShape(string message) =>
        new($"installed Codex app-server schema has unexpected shape: {message}");

    public static CodexSchemaValidationError SchemaCompile(string error) =>
        new($"failed to compile installed Codex app-server schema: {error}");

    public static CodexSchemaValidationError Serialize(string error) =>
        new($"failed to serialize Codex JSON-RPC request for schema validation: {error}");

    public static CodexSchemaValidationError Invalid(string method, string errors) =>
        new($"installed Codex app-server schema rejected `{method}` request: {errors}. Update Codex, or update OpenSymphony's Codex adapter if the installed schema is newer and incompatible.");
}