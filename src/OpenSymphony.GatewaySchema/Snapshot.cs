using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of snapshot types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GatewayHealth
{
    Healthy,
    Degraded,
    Failed,
    Starting,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SnapshotEventKind
{
    WorkerStarted,
    WorkspacePrepared,
    StreamAttached,
    SnapshotPublished,
    WorkerCompleted,
    RetryScheduled,
    ClientAttached,
    ClientDetached,
    Warning,
}

public sealed record GatewayMetrics(
    uint RunningIssueCount,
    uint RetryQueueDepth,
    ulong TotalInputTokens,
    ulong TotalOutputTokens,
    ulong TotalCacheReadTokens,
    ulong TotalCostMicros
);

public sealed record ProjectSummary(
    string ProjectId,
    string Name,
    uint MilestoneCount,
    uint IssueCount,
    uint RunningCount,
    uint CompletedCount,
    uint FailedCount
);

public sealed record ProjectMilestoneSummary(
    string MilestoneId,
    string Name,
    uint IssueCount
);

public sealed record ProjectIssueSummary(
    string IssueId,
    string Identifier,
    string Title,
    string State,
    byte? Priority,
    string? MilestoneId,
    string? RuntimeState
);

public sealed record SnapshotEventSummary(
    DateTimeOffset HappenedAt,
    string? IssueIdentifier,
    SnapshotEventKind Kind,
    string Summary
);

public sealed record DashboardSnapshot(
    SchemaVersion SchemaVersion,
    DateTimeOffset GeneratedAt,
    ulong Sequence,
    GatewayHealth Health,
    GatewayMetrics Metrics,
    List<ProjectSummary> Projects,
    List<SnapshotEventSummary> RecentEvents
);

public sealed record ProjectList(
    SchemaVersion SchemaVersion,
    List<ProjectSummary> Projects
);

public sealed record ProjectDetail(
    SchemaVersion SchemaVersion,
    string ProjectId,
    string Name,
    uint MilestoneCount,
    uint IssueCount,
    uint RunningCount,
    uint CompletedCount,
    uint FailedCount,
    string? Summary = null,
    List<ProjectMilestoneSummary> Milestones = null!
)
{
    public ProjectDetail() : this(SchemaVersion.V1(), "", "", 0, 0, 0, 0, 0) { }
}

public sealed record ProjectIssuesPage(
    SchemaVersion SchemaVersion,
    string ProjectId,
    PageCursor? NextCursor,
    List<ProjectIssueSummary> Issues
);