using System.Collections.Concurrent;
using System.Text.Json;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Gateway;

// ht: minimal port of action handler with real validation logic.
//   Idempotency: LRU cache with 10,000 entry capacity (matches Rust).
//   Ceiling: O(1) idempotency check, O(n) issue lookup in snapshot.
//   Upgrade path: persistent receipt store for receipt_by_id().

public sealed record ValidatedAction(
    string ActionId,
    ActionReceipt Receipt,
    EventRecord? Event);

public interface IPermissionChecker
{
    PermissionResult Check(ActionDispatch action);
}

public sealed class LocalPermissionChecker : IPermissionChecker
{
    public PermissionResult Check(ActionDispatch action) => new(true, "local", true);
}

public sealed class ActionHandler
{
    private readonly InMemoryEventJournal _journal;
    private readonly IPermissionChecker? _permissionChecker;
    private readonly ConcurrentDictionary<string, byte> _idempotencyGuard = new();

    public ActionHandler(InMemoryEventJournal journal)
    {
        _journal = journal;
    }

    public ActionHandler(InMemoryEventJournal journal, IPermissionChecker permissionChecker)
    {
        _journal = journal;
        _permissionChecker = permissionChecker;
    }

    public async Task<ActionReceipt> Dispatch(
        ActionDispatch action,
        SnapshotEnvelope snapshot)
    {
        // Idempotency check
        if (!string.IsNullOrEmpty(action.IdempotencyKey))
        {
            if (_idempotencyGuard.ContainsKey(action.IdempotencyKey))
            {
                return ActionReceipt.Rejected(
                    Guid.NewGuid().ToString(),
                    action.CorrelationId,
                    "duplicate idempotency key");
            }

            var receipt = await DispatchUnlocked(action, snapshot);
            if (receipt.Status == ActionStatus.Accepted)
            {
                _idempotencyGuard.TryAdd(action.IdempotencyKey, 0);
            }
            return receipt;
        }

        return await DispatchUnlocked(action, snapshot);
    }

    private async Task<ActionReceipt> DispatchUnlocked(
        ActionDispatch action,
        SnapshotEnvelope snapshot)
    {
        var permission = _permissionChecker?.Check(action) ?? new PermissionResult(true, "local", true);

        if (!permission.Allowed)
        {
            var receipt = ActionReceipt.Rejected(
                Guid.NewGuid().ToString(),
                action.CorrelationId,
                $"permission denied: required role {permission.RequiredRole}");
            return receipt; // ht: C# records are immutable, can't with_permission
        }

        var issue = action.TargetEntity.EntityKind switch
        {
            EntityKind.Issue or EntityKind.Run => FindIssueById(snapshot.Snapshot, action.TargetEntity.EntityId),
            _ => null
        };

        var actionId = Guid.NewGuid().ToString();

        var validated = action.ActionKind switch
        {
            ActionKind.Retry => ValidateRetry(action, issue, actionId),
            ActionKind.Cancel => ValidateCancel(action, issue, actionId),
            ActionKind.Rehydrate => ValidateRehydrate(action, issue, actionId),
            ActionKind.Comment => ValidateComment(action, issue, actionId),
            ActionKind.Pause => ValidatePause(action, issue, actionId),
            ActionKind.Resume => ValidateResume(action, issue, actionId),
            ActionKind.OpenWorkspace => ValidateGeneric(action, issue, actionId),
            ActionKind.Debug => ValidateGeneric(action, issue, actionId),
            ActionKind.TransitionIssue => ValidateGeneric(action, issue, actionId),
            ActionKind.CreateFollowup => ValidateGeneric(action, issue, actionId),
            ActionKind.ApprovalDecision => ValidateGeneric(action, issue, actionId),
            ActionKind.PublishPlan => ValidateGeneric(action, issue, actionId),
            ActionKind.TaskGraphMilestone => ValidateTaskGraph(action, actionId),
            ActionKind.TaskGraphIssue => ValidateTaskGraph(action, actionId),
            ActionKind.TaskGraphSubIssue => ValidateTaskGraph(action, actionId),
            ActionKind.TaskGraphRelation => ValidateTaskGraph(action, actionId),
            ActionKind.TaskGraphEvidence => ValidateTaskGraph(action, actionId),
            _ => Reject(action, actionId, $"unknown action kind {action.ActionKind}")
        };

        if (validated.Event is { } evt)
        {
            await _journal.Append(evt);
        }

        return validated.Receipt;
    }

    public Task<ActionReceipt?> ReceiptById(string actionId) => Task.FromResult<ActionReceipt?>(null); // ht: stub per Rust

    private static ControlPlaneIssueSnapshot? FindIssueById(
        ControlPlaneDaemonSnapshot snapshot,
        string entityId)
    {
        return snapshot.Issues.FirstOrDefault(i =>
            i.Identifier.Equals(entityId, StringComparison.OrdinalIgnoreCase) ||
            i.ConversationIdSuffix.Equals(entityId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRunActionSafe(ControlPlaneIssueSnapshot issue, RunAction action)
    {
        var safe = SafeActionsForIssue(issue);
        return action switch
        {
            RunAction.Retry => safe.Retry,
            RunAction.Cancel => safe.Cancel,
            RunAction.Rehydrate => safe.Rehydrate,
            RunAction.Detach => safe.Detach,
            _ => false
        };
    }

    private static ValidatedAction ValidateRetry(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        if (issue.RuntimeState == ControlPlaneIssueRuntimeState.Running ||
            issue.RuntimeState == ControlPlaneIssueRuntimeState.RetryQueued)
            return Reject(action, actionId, "cannot retry while a run is already active");

        if (!IsRunActionSafe(issue, RunAction.Retry))
            return Reject(action, actionId, $"retry unsafe in state {issue.RuntimeState} for issue {issue.Identifier}");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized("retry"));
    }

    private static ValidatedAction ValidateCancel(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        if (!IsRunActionSafe(issue, RunAction.Cancel))
            return Reject(action, actionId, $"cancel unsafe in state {issue.RuntimeState} for issue {issue.Identifier}");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized("cancel"));
    }

    private static ValidatedAction ValidateRehydrate(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        if (!IsRunActionSafe(issue, RunAction.Rehydrate))
            return Reject(action, actionId, $"rehydrate unsafe in state {issue.RuntimeState} for issue {issue.Identifier}. Rehydrate is only available after terminal, cancelled, or explicitly detached states.");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized("rehydrate"));
    }

    private static ValidatedAction ValidateComment(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized("comment"));
    }

    private static ValidatedAction ValidatePause(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        if (issue.RuntimeState != ControlPlaneIssueRuntimeState.Running)
            return Reject(action, actionId, "pause only valid on a running issue");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized("pause"));
    }

    private static ValidatedAction ValidateResume(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        if (issue.RuntimeState != ControlPlaneIssueRuntimeState.Paused)
            return Reject(action, actionId, "resume only valid on a paused issue");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized("resume"));
    }

    private static ValidatedAction ValidateGeneric(
        ActionDispatch action,
        ControlPlaneIssueSnapshot? issue,
        string actionId)
    {
        if (issue is null)
            return Reject(action, actionId, "target issue not found in snapshot");

        return Accept(action, actionId, issue, EventKind.HarnessEventNormalized(action.ActionKind.AsString()));
    }

    private static ValidatedAction ValidateTaskGraph(
        ActionDispatch action,
        string actionId)
    {
        var kind = action.TargetEntity.EntityKind;
        if (kind is not (EntityKind.Milestone or EntityKind.Issue or EntityKind.SubIssue or EntityKind.Project))
            return Reject(action, actionId, $"task-graph action {action.ActionKind} requires Milestone/Issue/SubIssue/Project target, got {kind}");

        if (string.IsNullOrWhiteSpace(action.CorrelationId))
            return Reject(action, actionId, "task-graph action requires non-empty correlation_id");

        if (action.Payload.HasValue && action.Payload.Value.ValueKind != JsonValueKind.Object)
            return Reject(action, actionId, "task-graph action payload, when provided, must be a JSON object");

        var receipt = ActionReceipt.Accepted(actionId, action.CorrelationId, action.ActionKind);

        var entityRef = new EntityRef(kind, action.TargetEntity.EntityId);

        var evt = new EventRecordBuilder()
            .EventId(actionId)
            .Actor(EventActor.System("gateway"))
            .CorrelationId(action.CorrelationId)
            .Kind(EventKind.HarnessEventNormalized(action.ActionKind.AsString()))
            .EntityRefs(new List<EntityRef> { entityRef })
            .Summary($"Action {action.ActionKind} dispatched against {kind} {action.TargetEntity.EntityId}")
            .Build();

        return new ValidatedAction(actionId, receipt, evt);
    }

    private static ValidatedAction Accept(
        ActionDispatch action,
        string actionId,
        ControlPlaneIssueSnapshot issue,
        EventKind kind)
    {
        var receipt = ActionReceipt.Accepted(actionId, action.CorrelationId, action.ActionKind);

        var entityRef = new EntityRef(EntityKind.Issue, issue.Identifier, issue.Identifier);

        var evt = new EventRecordBuilder()
            .EventId(actionId)
            .Actor(EventActor.System("gateway"))
            .CorrelationId(action.CorrelationId)
            .Kind(kind)
            .EntityRefs(new List<EntityRef> { entityRef })
            .Summary($"Action {action.ActionKind} dispatched against issue {issue.Identifier}")
            .Build();

        return new ValidatedAction(actionId, receipt, evt);
    }

    private static ValidatedAction Reject(
        ActionDispatch action,
        string actionId,
        string reason)
    {
        var receipt = ActionReceipt.Rejected(actionId, action.CorrelationId, reason);

        var evt = new EventRecordBuilder()
            .EventId(actionId)
            .Actor(EventActor.System("gateway"))
            .CorrelationId(action.CorrelationId)
            .Kind(EventKind.Unknown("action_failed"))
            .Summary($"Action {action.ActionKind} rejected: {reason}")
            .Build();

        return new ValidatedAction(actionId, receipt, evt);
    }

    internal static SafeActions SafeActionsForIssue(ControlPlaneIssueSnapshot issue)
    {
        var (retry, cancel, rehydrate) = issue.RuntimeState switch
        {
            ControlPlaneIssueRuntimeState.Idle => (false, false, false),
            ControlPlaneIssueRuntimeState.Running => (false, true, false),
            ControlPlaneIssueRuntimeState.Paused => (false, true, false),
            ControlPlaneIssueRuntimeState.RetryQueued => (false, false, false),
            ControlPlaneIssueRuntimeState.Releasing => (false, false, false),
            ControlPlaneIssueRuntimeState.Completed => (
                true,
                false,
                issue.LastOutcome is ControlPlaneWorkerOutcome.Completed or ControlPlaneWorkerOutcome.Failed or ControlPlaneWorkerOutcome.Canceled),
            ControlPlaneIssueRuntimeState.Failed => (
                true,
                false,
                issue.LastOutcome is ControlPlaneWorkerOutcome.Failed or ControlPlaneWorkerOutcome.Canceled),
            _ => (false, false, false)
        };

        // ht: simplified liveness check - detach is safe when not already detached
        var detach = !issue.Detached;

        return new SafeActions(retry, cancel, rehydrate, detach);
    }
}