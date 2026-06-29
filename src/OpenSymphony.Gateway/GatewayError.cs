namespace OpenSymphony.Gateway;

// ht: minimal port of gateway error types.

public enum MutationErrorKind
{
    Validation,
    PermissionDenied,
    SchemaDrift,
    Upstream,
    Unavailable,
}

public sealed record MutationError(MutationErrorKind Kind, string Reason)
{
    public string AsReason() => Kind switch
    {
        MutationErrorKind.Validation => $"validation failed: {Reason}",
        MutationErrorKind.PermissionDenied => $"permission denied: {Reason}",
        MutationErrorKind.SchemaDrift => $"schema drift: {Reason}",
        MutationErrorKind.Upstream => $"upstream error: {Reason}",
        MutationErrorKind.Unavailable => $"mutation client unavailable: {Reason}",
        _ => Reason,
    };

    public static MutationError Validation(string reason) => new(MutationErrorKind.Validation, reason);
    public static MutationError PermissionDenied(string reason) => new(MutationErrorKind.PermissionDenied, reason);
    public static MutationError SchemaDrift(string reason) => new(MutationErrorKind.SchemaDrift, reason);
    public static MutationError Upstream(string reason) => new(MutationErrorKind.Upstream, reason);
    public static MutationError Unavailable(string reason) => new(MutationErrorKind.Unavailable, reason);
}