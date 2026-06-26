using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenSymphony.Domain;

// ht: Port of older/crates/opensymphony-domain/src/tracker.rs.
//   Uses DateTimeOffset for chrono::DateTime<Utc> fields.

public sealed class TrackerIssue
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public byte? Priority { get; set; }
    public string State { get; set; } = "";
    public TrackerIssueStateKind StateKind { get; set; } = TrackerIssueStateKind.Unknown("unknown");
    public List<string> Labels { get; set; } = new();
    public string? ParentId { get; set; }
    public TrackerIssueRef? Parent { get; set; }
    public TrackerProjectMilestone? ProjectMilestone { get; set; }
    public List<TrackerIssueBlocker> BlockedBy { get; set; } = new();
    public List<TrackerIssueRef> SubIssues { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TrackerIssueStateSnapshot
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public TrackerIssueState State { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TrackerIssueState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonPropertyName("type")]
    public string TrackerType { get; set; } = "";
    public TrackerIssueStateKind Kind { get; set; } = TrackerIssueStateKind.Unknown("unknown");

    public bool IsTerminal() => Kind.IsTerminal();
}

public sealed class TrackerIssueBlocker
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string Title { get; set; } = "";
    public TrackerIssueState State { get; set; } = new();

    public bool IsTerminal() => State.IsTerminal();
}

public sealed class TrackerIssueRef
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string State { get; set; } = "";

    public bool IsTerminal(HashSet<string> terminalStates)
    {
        var state = State.Trim();
        foreach (var terminal in terminalStates)
        {
            if (terminal.Trim().Equals(state, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public sealed class TrackerProjectMilestone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

// ht: Rust enum with Unknown(String) variant → polymorphic class hierarchy.
[JsonConverter(typeof(TrackerIssueStateKindConverter))]
public abstract class TrackerIssueStateKind
{
    public abstract string Label { get; }

    public static readonly TrackerIssueStateKind Backlog = new BacklogKind();
    public static readonly TrackerIssueStateKind Unstarted = new UnstartedKind();
    public static readonly TrackerIssueStateKind Started = new StartedKind();
    public static readonly TrackerIssueStateKind Completed = new CompletedKind();
    public static readonly TrackerIssueStateKind Canceled = new CanceledKind();
    public static readonly TrackerIssueStateKind Triage = new TriageKind();

    public static TrackerIssueStateKind Unknown(string value) => new UnknownKind(value);

    public static TrackerIssueStateKind FromTrackerType(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        return lower switch
        {
            "backlog" => Backlog,
            "unstarted" => Unstarted,
            "started" => Started,
            "completed" => Completed,
            "canceled" => Canceled,
            "triage" or "triaged" => Triage,
            _ => new UnknownKind(lower),
        };
    }

    public bool IsTerminal() => this is CompletedKind or CanceledKind;

    public sealed class BacklogKind : TrackerIssueStateKind
    {
        public override string Label => "backlog";
        public override bool Equals(object? obj) => obj is BacklogKind;
        public override int GetHashCode() => Label.GetHashCode();
    }

    public sealed class UnstartedKind : TrackerIssueStateKind
    {
        public override string Label => "unstarted";
        public override bool Equals(object? obj) => obj is UnstartedKind;
        public override int GetHashCode() => Label.GetHashCode();
    }

    public sealed class StartedKind : TrackerIssueStateKind
    {
        public override string Label => "started";
        public override bool Equals(object? obj) => obj is StartedKind;
        public override int GetHashCode() => Label.GetHashCode();
    }

    public sealed class CompletedKind : TrackerIssueStateKind
    {
        public override string Label => "completed";
        public override bool Equals(object? obj) => obj is CompletedKind;
        public override int GetHashCode() => Label.GetHashCode();
    }

    public sealed class CanceledKind : TrackerIssueStateKind
    {
        public override string Label => "canceled";
        public override bool Equals(object? obj) => obj is CanceledKind;
        public override int GetHashCode() => Label.GetHashCode();
    }

    public sealed class TriageKind : TrackerIssueStateKind
    {
        public override string Label => "triage";
        public override bool Equals(object? obj) => obj is TriageKind;
        public override int GetHashCode() => Label.GetHashCode();
    }

    public sealed class UnknownKind : TrackerIssueStateKind
    {
        private readonly string _value;
        public UnknownKind(string value) => _value = value;
        public string Value => _value;
        public override string Label => "unknown";
        public override bool Equals(object? obj) => obj is UnknownKind other && _value == other._value;
        public override int GetHashCode() => _value.GetHashCode();
    }
}

public sealed class TrackerIssueStateKindConverter : JsonConverter<TrackerIssueStateKind>
{
    public override TrackerIssueStateKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()!;
        return value switch
        {
            "backlog" => TrackerIssueStateKind.Backlog,
            "unstarted" => TrackerIssueStateKind.Unstarted,
            "started" => TrackerIssueStateKind.Started,
            "completed" => TrackerIssueStateKind.Completed,
            "canceled" => TrackerIssueStateKind.Canceled,
            "triage" => TrackerIssueStateKind.Triage,
            _ => TrackerIssueStateKind.Unknown(value),
        };
    }

    public override void Write(Utf8JsonWriter writer, TrackerIssueStateKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Label);
    }
}

// ht: Rust #[serde(rename_all = "snake_case")] enum → snake_case strings.
[JsonConverter(typeof(TrackerErrorCategoryConverter))]
public enum TrackerErrorCategory
{
    Auth,
    RateLimited,
    Transport,
    Timeout,
    InvalidResponse,
    NotFound,
    InvalidStateTransition,
    PermissionDenied,
}

public sealed class TrackerErrorCategoryConverter : JsonConverter<TrackerErrorCategory>
{
    private static readonly Dictionary<TrackerErrorCategory, string> Names = new()
    {
        [TrackerErrorCategory.Auth] = "auth",
        [TrackerErrorCategory.RateLimited] = "rate_limited",
        [TrackerErrorCategory.Transport] = "transport",
        [TrackerErrorCategory.Timeout] = "timeout",
        [TrackerErrorCategory.InvalidResponse] = "invalid_response",
        [TrackerErrorCategory.NotFound] = "not_found",
        [TrackerErrorCategory.InvalidStateTransition] = "invalid_state_transition",
        [TrackerErrorCategory.PermissionDenied] = "permission_denied",
    };

    private static readonly Dictionary<string, TrackerErrorCategory> Reverse = Names
        .ToDictionary(kv => kv.Value, kv => kv.Key);

    public override TrackerErrorCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()!;
        return Reverse.TryGetValue(s, out var cat) ? cat : TrackerErrorCategory.InvalidResponse;
    }

    public override void Write(Utf8JsonWriter writer, TrackerErrorCategory value, JsonSerializerOptions options)
        => writer.WriteStringValue(Names[value]);
}
