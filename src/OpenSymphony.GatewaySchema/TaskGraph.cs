using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of task graph types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskGraphNodeKind
{
    Milestone,
    Issue,
    SubIssue,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskGraphStateCategory
{
    Backlog,
    Todo,
    InProgress,
    Done,
    Canceled,
}

public sealed record DiffSummary(
    uint FilesAdded,
    uint FilesModified,
    uint FilesRemoved,
    uint LinesAdded,
    uint LinesRemoved
);

public sealed record TaskGraphRuntimeOverlay(
    bool Eligible,
    bool Queued,
    string? ActiveRunId = null,
    string? LastOutcome = null,
    uint RetryCount = 0,
    string? WorkspaceId = null,
    string? HarnessType = null,
    string? ConversationId = null,
    DateTimeOffset? LastEventAt = null,
    DiffSummary? DiffSummary = null,
    string? ValidationStatus = null,
    string? BlockerSummary = null
);

public sealed record TaskGraphNode(
    SchemaVersion SchemaVersion,
    string NodeId,
    TaskGraphNodeKind Kind,
    string Identifier,
    string Title,
    string State,
    TaskGraphStateCategory StateCategory,
    byte? Priority,
    string? ParentId,
    List<string> Children,
    List<string> BlockedBy,
    string? Url,
    string? BranchName,
    List<string> Labels,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint? EstimateMinutes,
    TaskGraphRuntimeOverlay? RuntimeOverlay = null
);

public sealed record TaskGraphSnapshot(
    SchemaVersion SchemaVersion,
    string ProjectId,
    DateTimeOffset GeneratedAt,
    List<TaskGraphNode> Nodes,
    List<string> RootIds
);