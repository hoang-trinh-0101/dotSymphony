namespace OpenSymphony.Domain;

// ht: IStringIdentifierTag + generic struct replaces the Rust string_identifier! macro.
//   static abstract Kind gives each marker its own name without per-type boilerplate.
public sealed record IdentifierError(string Kind)
{
    public string Message => $"{Kind} cannot be empty";
}

public interface IStringIdentifierTag
{
    static abstract string Kind { get; }
}

public readonly struct StringIdentifier<TTag> : IEquatable<StringIdentifier<TTag>>
    where TTag : IStringIdentifierTag
{
    public string Value { get; }

    private StringIdentifier(string value) => Value = value;

    public static Result<StringIdentifier<TTag>, IdentifierError> New(string value)
    {
        // ht: Rust uses value.trim().is_empty() — trimmed empty check.
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<StringIdentifier<TTag>, IdentifierError>.Err(
                new IdentifierError(typeof(TTag).Name));
        }
        return Result<StringIdentifier<TTag>, IdentifierError>.Ok(
            new StringIdentifier<TTag>(value));
    }

    public override string ToString() => Value;
    public bool Equals(StringIdentifier<TTag> other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StringIdentifier<TTag> other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(StringIdentifier<TTag> left, StringIdentifier<TTag> right) => left.Equals(right);
    public static bool operator !=(StringIdentifier<TTag> left, StringIdentifier<TTag> right) => !left.Equals(right);

    public static implicit operator string(StringIdentifier<TTag> id) => id.Value;
    public static explicit operator StringIdentifier<TTag>(string value) => New(value).Value;
}

public readonly struct ConversationId : IStringIdentifierTag
{
    public static string Kind => nameof(ConversationId);
}

public readonly struct IssueId : IStringIdentifierTag
{
    public static string Kind => nameof(IssueId);
}

public readonly struct IssueIdentifier : IStringIdentifierTag
{
    public static string Kind => nameof(IssueIdentifier);
}

public readonly struct TrackerStateId : IStringIdentifierTag
{
    public static string Kind => nameof(TrackerStateId);
}

public readonly struct WorkerId : IStringIdentifierTag
{
    public static string Kind => nameof(WorkerId);
}

public readonly struct WorkspaceKey : IEquatable<WorkspaceKey>
{
    public string Value { get; }

    private WorkspaceKey(string value) => Value = value;

    public static Result<WorkspaceKey, IdentifierError> New(string value)
    {
        // ht: Rust uses value.is_empty() — RAW empty check, NOT trimmed.
        //   Whitespace is accepted and then sanitized. This is the deliberate
        //   difference vs the 5 string identifiers above.
        if (value.Length == 0)
        {
            return Result<WorkspaceKey, IdentifierError>.Err(
                new IdentifierError(nameof(WorkspaceKey)));
        }
        return Result<WorkspaceKey, IdentifierError>.Ok(
            new WorkspaceKey(Sanitize(value)));
    }

    // ht: public so OpenSymphony.Workspace reuses the exact same char-map
    //   without duplicating the Rust private copy.
    public static string Sanitize(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append((char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') ? c : '_');
        }
        return sb.ToString();
    }

    public override string ToString() => Value;
    public bool Equals(WorkspaceKey other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WorkspaceKey other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(WorkspaceKey left, WorkspaceKey right) => left.Equals(right);
    public static bool operator !=(WorkspaceKey left, WorkspaceKey right) => !left.Equals(right);
    public static implicit operator string(WorkspaceKey key) => key.Value;
}
