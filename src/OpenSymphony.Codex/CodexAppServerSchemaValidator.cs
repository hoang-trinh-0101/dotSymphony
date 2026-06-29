using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Codex;

// ht: minimal schema validator placeholder. Full JSON schema validation deferred.
public class CodexAppServerSchemaValidator
{
    private CodexAppServerSchemaValidator() { }

    public static async Task<Result<CodexAppServerSchemaValidator>> FromSchemaFileAsync(string path)
    {
        try
        {
            await File.ReadAllTextAsync(path);
            return Result<CodexAppServerSchemaValidator>.Ok(new CodexAppServerSchemaValidator());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<CodexAppServerSchemaValidator>.Err(
                CodexSchemaValidationError.SchemaRead(path, ex));
        }
    }

    public static Task<Result<CodexAppServerSchemaValidator>> FromSchemaStrAsync(string schema) =>
        Task.FromResult(Result<CodexAppServerSchemaValidator>.Ok(new CodexAppServerSchemaValidator()));

    public static Task<Result<CodexAppServerSchemaValidator>> FromSchemaJsonAsync(JsonElement schema) =>
        Task.FromResult(Result<CodexAppServerSchemaValidator>.Ok(new CodexAppServerSchemaValidator()));

    public Result<Unit> ValidateRequest(JsonRpcRequestEnvelope request) => Result.Ok;
}