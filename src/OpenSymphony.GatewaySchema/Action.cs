using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of action types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionKind
{
    Retry,
    Cancel,
    Pause,
    Resume,
    Rehydrate,
    Comment,
    OpenWorkspace,
    Debug,
    TransitionIssue,
    CreateFollowup,
    ApprovalDecision,
    PublishPlan,
    TaskGraphMilestone,
    TaskGraphIssue,
    TaskGraphSubIssue,
    TaskGraphRelation,
    TaskGraphEvidence,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionStatus
{
    Accepted,
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExpectedFollowup
{
    StateTransition,
    RunLifecycle,
    ActionCompletion,
    JournalUpdate,
    TaskGraphUpdate,
}

public sealed record PermissionResult(
    bool Allowed,
    string RequiredRole,
    bool Evaluated
);

public sealed record ActionTarget(
    EntityKind EntityKind,
    string EntityId
);

public sealed record ActionDispatch(
    SchemaVersion SchemaVersion,
    string CorrelationId,
    ActionKind ActionKind,
    ActionTarget TargetEntity,
    JsonElement? Payload,
    string? IdempotencyKey
);

public sealed record ActionReceipt(
    SchemaVersion SchemaVersion,
    string ActionId,
    string CorrelationId,
    ActionStatus Status,
    string? Reason,
    string IssuedAt,
    PermissionResult? Permission,
    List<ExpectedFollowup> ExpectedFollowup
)
{
    public static ActionReceipt Accepted(
        string actionId,
        string correlationId,
        ActionKind actionKind
    ) => new(
        SchemaVersion.V1(),
        actionId,
        correlationId,
        ActionStatus.Accepted,
        null,
        DateTimeOffset.UtcNow.ToString("o"),
        null,
        actionKind.ExpectedFollowups()
    );

    public static ActionReceipt Rejected(
        string actionId,
        string correlationId,
        string reason
    ) => new(
        SchemaVersion.V1(),
        actionId,
        correlationId,
        ActionStatus.Rejected,
        reason,
        DateTimeOffset.UtcNow.ToString("o"),
        null,
        []
    );
}

public static class ActionKindExtensions
{
    public static string AsString(this ActionKind kind) => kind switch
    {
        ActionKind.Retry => "retry",
        ActionKind.Cancel => "cancel",
        ActionKind.Pause => "pause",
        ActionKind.Resume => "resume",
        ActionKind.Rehydrate => "rehydrate",
        ActionKind.Comment => "comment",
        ActionKind.OpenWorkspace => "open_workspace",
        ActionKind.Debug => "debug",
        ActionKind.TransitionIssue => "transition_issue",
        ActionKind.CreateFollowup => "create_followup",
        ActionKind.ApprovalDecision => "approval_decision",
        ActionKind.PublishPlan => "publish_plan",
        ActionKind.TaskGraphMilestone => "task_graph_milestone",
        ActionKind.TaskGraphIssue => "task_graph_issue",
        ActionKind.TaskGraphSubIssue => "task_graph_sub_issue",
        ActionKind.TaskGraphRelation => "task_graph_relation",
        ActionKind.TaskGraphEvidence => "task_graph_evidence",
        _ => kind.ToString(),
    };

    public static List<ExpectedFollowup> ExpectedFollowups(this ActionKind kind) => kind switch
    {
        ActionKind.Retry => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.RunLifecycle, ExpectedFollowup.StateTransition],
        ActionKind.Cancel => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.RunLifecycle],
        ActionKind.Pause => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.StateTransition],
        ActionKind.Resume => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.StateTransition, ExpectedFollowup.RunLifecycle],
        ActionKind.Rehydrate => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.RunLifecycle, ExpectedFollowup.StateTransition],
        ActionKind.Comment => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.JournalUpdate],
        ActionKind.OpenWorkspace => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.JournalUpdate],
        ActionKind.Debug => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.JournalUpdate],
        ActionKind.TransitionIssue => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.StateTransition],
        ActionKind.CreateFollowup => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.JournalUpdate],
        ActionKind.ApprovalDecision => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.StateTransition],
        ActionKind.PublishPlan => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.JournalUpdate],
        ActionKind.TaskGraphMilestone => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.TaskGraphUpdate],
        ActionKind.TaskGraphIssue => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.TaskGraphUpdate],
        ActionKind.TaskGraphSubIssue => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.TaskGraphUpdate],
        ActionKind.TaskGraphRelation => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.TaskGraphUpdate],
        ActionKind.TaskGraphEvidence => [ExpectedFollowup.ActionCompletion, ExpectedFollowup.TaskGraphUpdate, ExpectedFollowup.JournalUpdate],
        _ => [],
    };
}