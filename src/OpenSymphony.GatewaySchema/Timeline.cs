using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of timeline types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimelineEntryKind
{
    Phase,
    ToolCall,
    Command,
    TokenUpdate,
    Reconnect,
    StallProbe,
    Progress,
    State,
    Log,
    Terminal,
    File,
    Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunPhase
{
    Active,
    Quiet,
    Degraded,
    Stalled,
    RetryQueued,
    Cancelled,
    Detached,
    Completed,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunStreamLiveness
{
    Healthy,
    Stale,
    Dead,
    Detached,
    Degraded,
    Stalled,
}

public sealed record TokenDelta(ulong Input, ulong Output, ulong CacheRead);

public sealed record TimelineEntityRef(
    EntityKind Kind,
    string Id,
    string? Identifier = null
)
{
    public static TimelineEntityRef Run(string id) => new(EntityKind.Run, id);
    public static TimelineEntityRef Issue(string id, string identifier) => new(EntityKind.Issue, id, identifier);
    public static TimelineEntityRef SubIssue(string id) => new(EntityKind.SubIssue, id);
    public static TimelineEntityRef Terminal(string id) => new(EntityKind.TerminalSession, id);
}

public sealed record RunStateEvidence(
    RunPhase Phase,
    RunStreamLiveness Stream,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? StallDeadlineAt,
    string Explanation
);

public sealed record TimelineEntry(
    string EntryId,
    ulong SequenceStart,
    ulong SequenceEnd,
    DateTimeOffset HappenedAt,
    TimelineEntryKind Kind,
    RunPhase? Phase,
    string Title,
    string Summary,
    List<string> EventIds = null!,
    List<TimelineEntityRef> EntityRefs = null!,
    string? CommandId = null,
    string? ToolName = null,
    List<string> FilePaths = null!,
    string? TerminalSessionId = null,
    string? LogLevel = null,
    TokenDelta? TokenDelta = null,
    RunStateEvidence? StateEvidence = null
)
{
    public TimelineEntry() : this("", 0, 0, DateTimeOffset.UtcNow, default, null, "", "") { }
}

public sealed record RunTimeline(
    SchemaVersion SchemaVersion,
    string RunId,
    DateTimeOffset GeneratedAt,
    List<TimelineEntry> Entries
);

public sealed record TerminalSearchMatch(
    ulong FrameSequence,
    DateTimeOffset FrameTimestamp,
    string Snippet
);

public sealed record TerminalSearchResult(
    SchemaVersion SchemaVersion,
    string TerminalSessionId,
    string Query,
    List<TerminalSearchMatch> Matches
);

public sealed record TerminalJumpResult(
    SchemaVersion SchemaVersion,
    string TerminalSessionId,
    string EventId,
    ulong? FrameSequence,
    bool Found
);

public sealed record RunLogEntry(
    ulong Sequence,
    string EventId,
    DateTimeOffset HappenedAt,
    string Level,
    string Message,
    string? TerminalSessionId = null,
    string? CommandId = null
);

public sealed record RunLogPage(
    SchemaVersion SchemaVersion,
    string RunId,
    ulong? NextCursor,
    List<RunLogEntry> Entries
);