using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of approval types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApprovalKind
{
    ToolUse,
    FileWrite,
    CommandExecution,
    PlanPublish,
    Custom,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Cancelled,
    Passed,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApprovalRiskLevel
{
    Low,
    Medium,
    High,
    Unknown,
}

public sealed record ApprovalActor(
    string ActorId,
    string ActorKind,
    string? DisplayName = null
);

public sealed record ApprovalTargetContext(
    string? FilePath = null,
    string? Command = null,
    string? IssueId = null,
    string? IssueIdentifier = null,
    string? RunId = null
);

public sealed record ApprovalRiskSummary(
    ApprovalRiskLevel Level,
    List<string> Reasons
);

public sealed record ApprovalRequest(
    SchemaVersion SchemaVersion,
    string ApprovalId,
    string RunId,
    string IssueId,
    ApprovalKind Kind,
    string Title,
    string Description,
    JsonElement? ProposedAction,
    ApprovalActor? Actor,
    ApprovalTargetContext? TargetContext,
    ApprovalRiskSummary? RiskSummary,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt,
    ApprovalStatus Status,
    string CorrelationId,
    DateTimeOffset? DecidedAt = null
);