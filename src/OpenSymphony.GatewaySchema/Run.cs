using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of run types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunLifecycleState
{
    Eligible,
    Queued,
    Claimed,
    Running,
    Paused,
    Releasing,
    Completed,
    Failed,
    Canceled,
    RetryExhausted,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunStatus
{
    Unclaimed,
    Claimed,
    Running,
    Paused,
    RetryQueued,
    Released,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseReason
{
    Completed,
    TrackerInactive,
    TrackerTerminal,
    Cancelled,
    CancelFailed,
    RetryExhausted,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunAction
{
    Retry,
    Cancel,
    Pause,
    Resume,
    Rehydrate,
    Detach,
    Comment,
    CreateFollowup,
    OpenWorkspace,
    Debug,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileChangeKind
{
    Created,
    Modified,
    Removed,
}

public sealed record SafeActions(
    bool Retry = false,
    bool Cancel = false,
    bool Rehydrate = false,
    bool Detach = false
)
{
    public static SafeActions Default => new();
}

public sealed record HarnessSchedulerDisagreement(
    RunStatus SchedulerStatus,
    string HarnessStatus,
    DateTimeOffset DetectedAt,
    string ResolutionPath
);

public sealed record RunDiagnostics(
    HarnessSchedulerDisagreement? HarnessSchedulerDisagreement,
    bool CancelAcknowledged,
    bool CancelFailed
);

public sealed record RunProgress(
    ulong Sequence,
    string EventId,
    DateTimeOffset HappenedAt,
    string Kind,
    string Summary
);

public sealed record RunLivenessEnvelope(
    RunPhase Phase,
    RunStreamLiveness Stream,
    RunProgress? LatestProgress,
    bool HarnessAcknowledged,
    bool CancelFailed,
    bool Detached
);

public sealed record ChangedFileEntry(
    string Path,
    FileChangeKind Kind,
    ulong? Size,
    string? DiffSummary
);

public sealed record RunEvent(
    ulong Sequence,
    string EventId,
    DateTimeOffset HappenedAt,
    string Kind,
    string Summary,
    JsonElement? Payload,
    JsonElement? RawPayload
);

public sealed record RunEventPage(
    SchemaVersion SchemaVersion,
    string RunId,
    PageCursor? NextCursor,
    List<RunEvent> Events
);

public sealed record RunDetail(
    SchemaVersion SchemaVersion,
    string RunId,
    string IssueId,
    string IssueIdentifier,
    string WorkerId,
    RunStatus Status,
    RunLifecycleState LifecycleState,
    DateTimeOffset ClaimedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    ReleaseReason? ReleaseReason,
    uint TurnCount,
    uint MaxTurns,
    uint? RetryAttempt,
    ulong InputTokens,
    ulong OutputTokens,
    ulong CacheReadTokens,
    ulong RuntimeSeconds,
    string? ConversationId,
    string? WorkspaceId,
    string? WorkspacePath,
    string? HarnessType,
    string? Summary,
    string? Blocker,
    string? Error,
    List<RunAction> AllowedActions,
    RunLivenessEnvelope? Liveness,
    RunDiagnostics? Diagnostics,
    SafeActions SafeActions,
    bool Detached,
    bool CancelAcknowledged,
    bool CancelFailed
)
{
    public RunDetail() : this(SchemaVersion.V1(), "", "", "", "", default, default, DateTimeOffset.UtcNow, null, null, null, 0, 0, null, 0, 0, 0, 0, null, null, null, null, null, null, null, [], null, null, new(), false, false, false) { }
}