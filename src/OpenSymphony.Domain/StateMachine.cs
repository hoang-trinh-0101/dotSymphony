using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenSymphony.Domain;

// ── Enums ──────────────────────────────────────────────────────────────────

// ht: Rust #[serde(rename_all = "snake_case")] — JsonStringEnumConverter handles JSON.
public enum SchedulerStatus
{
    Unclaimed,
    Claimed,
    Running,
    RetryQueued,
    Released,
}

public enum TransitionAction
{
    Claim,
    StartRunning,
    RecordTurnStarted,
    ObserveRuntimeEvent,
    QueueRetry,
    Release,
    Reopen,
}

public static class SchedulerStatusExtensions
{
    public static string ToSnakeCaseString(this SchedulerStatus status) => status switch
    {
        SchedulerStatus.Unclaimed => "unclaimed",
        SchedulerStatus.Claimed => "claimed",
        SchedulerStatus.Running => "running",
        SchedulerStatus.RetryQueued => "retry_queued",
        SchedulerStatus.Released => "released",
        _ => status.ToString(),
    };
}

public static class TransitionActionExtensions
{
    public static string ToSnakeCaseString(this TransitionAction action) => action switch
    {
        TransitionAction.Claim => "claim",
        TransitionAction.StartRunning => "start_running",
        TransitionAction.RecordTurnStarted => "record_turn_started",
        TransitionAction.ObserveRuntimeEvent => "observe_runtime_event",
        TransitionAction.QueueRetry => "queue_retry",
        TransitionAction.Release => "release",
        TransitionAction.Reopen => "reopen",
        _ => action.ToString(),
    };
}

// ── State transition errors (NOT serialized — sealed class hierarchy) ──────

public abstract class StateTransitionError
{
    public override string ToString() => GetType().Name;
}

public sealed class InvalidTransition : StateTransitionError
{
    public SchedulerStatus From { get; }
    public TransitionAction Action { get; }
    public InvalidTransition(SchedulerStatus from, TransitionAction action) { From = from; Action = action; }
    public override string ToString() => $"cannot {Action.ToSnakeCaseString()} while issue is {From.ToSnakeCaseString()}";
}

public sealed class AttemptMismatch : StateTransitionError
{
    public RetryAttempt? Expected { get; }
    public RetryAttempt? Actual { get; }
    public AttemptMismatch(RetryAttempt? expected, RetryAttempt? actual) { Expected = expected; Actual = actual; }
    public override string ToString() => $"retry attempt mismatch: expected {Expected}, got {Actual}";
}

public sealed class IssueMismatch : StateTransitionError
{
    public StringIdentifier<IssueId> Expected { get; }
    public StringIdentifier<IssueId> Actual { get; }
    public IssueMismatch(StringIdentifier<IssueId> expected, StringIdentifier<IssueId> actual) { Expected = expected; Actual = actual; }
    public override string ToString() => $"issue mismatch: expected {Expected}, got {Actual}";
}

public sealed class WorkspaceNotAttached : StateTransitionError
{
    public string Attempted { get; }
    public WorkspaceNotAttached(string attempted) { Attempted = attempted; }
    public override string ToString() => $"cannot claim issue without attached workspace; run requested path {Attempted}";
}

public sealed class WorkspaceIssueMismatch : StateTransitionError
{
    public WorkspaceKey ExpectedKey { get; }
    public WorkspaceKey ActualKey { get; }
    public string ActualPath { get; }
    public WorkspaceIssueMismatch(WorkspaceKey expectedKey, WorkspaceKey actualKey, string actualPath)
    { ExpectedKey = expectedKey; ActualKey = actualKey; ActualPath = actualPath; }
    public override string ToString() =>
        $"workspace does not match issue identity: expected key {ExpectedKey} at a path ending in {ExpectedKey}, got key {ActualKey} at {ActualPath}";
}

public sealed class WorkspaceIdentityMismatch : StateTransitionError
{
    public WorkspaceKey ExpectedKey { get; }
    public WorkspaceKey ActualKey { get; }
    public string ExpectedPath { get; }
    public string ActualPath { get; }
    public WorkspaceIdentityMismatch(WorkspaceKey expectedKey, WorkspaceKey actualKey, string expectedPath, string actualPath)
    { ExpectedKey = expectedKey; ActualKey = actualKey; ExpectedPath = expectedPath; ActualPath = actualPath; }
    public override string ToString() =>
        $"workspace identity mismatch: expected key {ExpectedKey} at {ExpectedPath}, got key {ActualKey} at {ActualPath}";
}

public sealed class WorkspacePathMismatch : StateTransitionError
{
    public string Expected { get; }
    public string Actual { get; }
    public WorkspacePathMismatch(string expected, string actual) { Expected = expected; Actual = actual; }
    public override string ToString() => $"workspace path mismatch: expected {Expected}, got {Actual}";
}

public sealed class ConversationNotAttached : StateTransitionError
{
    public override string ToString() => "cannot start running issue without conversation metadata";
}

public sealed class WorkerMismatch : StateTransitionError
{
    public StringIdentifier<WorkerId> Expected { get; }
    public StringIdentifier<WorkerId> Actual { get; }
    public WorkerMismatch(StringIdentifier<WorkerId> expected, StringIdentifier<WorkerId> actual) { Expected = expected; Actual = actual; }
    public override string ToString() => $"worker mismatch: expected {Expected}, got {Actual}";
}

// ── SchedulerState (internally-tagged: {"state":"<snake>","details":{...}}) ─

public abstract class SchedulerState
{
    public abstract SchedulerStatus Status { get; }
}

public sealed class SchedulerStateUnclaimed : SchedulerState
{
    public TimestampMs Since { get; set; }
    public SchedulerStateUnclaimed(TimestampMs since) { Since = since; }
    public override SchedulerStatus Status => SchedulerStatus.Unclaimed;
}

public sealed class SchedulerStateClaimed : SchedulerState
{
    public RunAttempt Run { get; set; }
    public SchedulerStateClaimed(RunAttempt run) { Run = run; }
    public override SchedulerStatus Status => SchedulerStatus.Claimed;
}

public sealed class SchedulerStateRunning : SchedulerState
{
    public RunAttempt Run { get; set; }
    public StallMetadata Stall { get; set; }
    public SchedulerStateRunning(RunAttempt run, StallMetadata stall) { Run = run; Stall = stall; }
    public override SchedulerStatus Status => SchedulerStatus.Running;
}

public sealed class SchedulerStateRetryQueued : SchedulerState
{
    public RetryEntry Retry { get; set; }
    public SchedulerStateRetryQueued(RetryEntry retry) { Retry = retry; }
    public override SchedulerStatus Status => SchedulerStatus.RetryQueued;
}

public sealed class SchedulerStateReleased : SchedulerState
{
    public TimestampMs ReleasedAt { get; set; }
    public ReleaseReason Reason { get; set; }
    public SchedulerStateReleased(TimestampMs releasedAt, ReleaseReason reason) { ReleasedAt = releasedAt; Reason = reason; }
    public override SchedulerStatus Status => SchedulerStatus.Released;
}

// ── IssueExecution ──────────────────────────────────────────────────────────

public sealed class IssueExecution
{
    private const int MaxRecentWorkerOutcomes = 10;

    public NormalizedIssue Issue { get; set; }
    public WorkspaceRecord? Workspace { get; set; }
    public ConversationMetadata? Conversation { get; set; }
    public SchedulerState State { get; set; }
    public WorkerOutcomeRecord? LastWorkerOutcome { get; set; }
    public List<WorkerOutcomeRecord> RecentWorkerOutcomes { get; set; } = new();

    // ht: parameterless ctor for STJ deserialization.
    public IssueExecution() { Issue = null!; State = null!; }

    public IssueExecution(NormalizedIssue issue, TimestampMs observedAt)
    {
        Issue = issue;
        Workspace = null;
        Conversation = null;
        State = new SchedulerStateUnclaimed(observedAt);
        LastWorkerOutcome = null;
        RecentWorkerOutcomes = new();
    }

    public SchedulerStatus Status => State.Status;

    public RunAttempt? CurrentRun => State switch
    {
        SchedulerStateClaimed c => c.Run,
        SchedulerStateRunning r => r.Run,
        _ => null,
    };

    public RetryEntry? Retry => State is SchedulerStateRetryQueued q ? q.Retry : null;

    public IssueSnapshot Snapshot() => IssueSnapshot.From(this);

    public Result<IssueExecution, StateTransitionError> RefreshIssue(NormalizedIssue issue)
    {
        if (issue.Id != Issue.Id)
            return Result<IssueExecution, StateTransitionError>.Err(
                new IssueMismatch(Issue.Id, issue.Id));
        Issue = issue;
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public void UpdateConversation(ConversationMetadata conversation) => Conversation = conversation;

    public void UpdateConversationTokenUsage(ulong input, ulong output, ulong cacheRead, ulong total)
    {
        Conversation?.SetTokenUsage(input, output, cacheRead, total);
    }

    public Result<IssueExecution, StateTransitionError> AttachWorkspace(WorkspaceRecord workspace)
    {
        if (Workspace is { } current)
        {
            var expectedPath = ComparableWorkspacePath(current.Path);
            var actualPath = ComparableWorkspacePath(workspace.Path);

            if (current.WorkspaceKey != workspace.WorkspaceKey || expectedPath != actualPath)
                return Result<IssueExecution, StateTransitionError>.Err(
                    new WorkspaceIdentityMismatch(current.WorkspaceKey, workspace.WorkspaceKey, expectedPath, actualPath));
        }
        else
        {
            var expectedKey = IssueWorkspaceKey(Issue);
            var actualPath = ComparableWorkspacePath(workspace.Path);

            if (workspace.WorkspaceKey != expectedKey || !WorkspacePathMatchesKey(actualPath, expectedKey))
                return Result<IssueExecution, StateTransitionError>.Err(
                    new WorkspaceIssueMismatch(expectedKey, workspace.WorkspaceKey, actualPath));
        }

        Workspace = workspace;
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> Claim(RunAttempt run)
    {
        var binding = ValidateRunBinding(run);
        if (binding.IsErr) return binding;

        uint normalRetryCount;
        switch (State)
        {
            case SchedulerStateUnclaimed:
                if (run.Attempt is not null)
                    return Result<IssueExecution, StateTransitionError>.Err(
                        new AttemptMismatch(null, run.Attempt));
                normalRetryCount = 0;
                break;
            case SchedulerStateRetryQueued q:
                if (run.Attempt != q.Retry.Attempt)
                    return Result<IssueExecution, StateTransitionError>.Err(
                        new AttemptMismatch(q.Retry.Attempt, run.Attempt));
                normalRetryCount = q.Retry.NormalRetryCount;
                break;
            default:
                return InvalidTransitionErr(Status, TransitionAction.Claim);
        }

        State = new SchedulerStateClaimed(run.WithNormalRetryCount(normalRetryCount));
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> StartRunning(
        TimestampMs startedAt, DurationMs stallTimeoutMs, ConversationMetadata? session)
    {
        if (State is not SchedulerStateClaimed claimed)
            return InvalidTransitionErr(Status, TransitionAction.StartRunning);

        if (session is not null)
            Conversation = session;
        if (Conversation is null)
            return Result<IssueExecution, StateTransitionError>.Err(new ConversationNotAttached());

        State = new SchedulerStateRunning(claimed.Run.MarkStarted(startedAt), StallMetadata.New(startedAt, stallTimeoutMs));
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> RecordTurnStarted(TimestampMs observedAt)
    {
        if (State is not SchedulerStateRunning running)
            return InvalidTransitionErr(Status, TransitionAction.RecordTurnStarted);

        running.Run.RecordTurnStarted();
        running.Stall = running.Stall.ObserveActivity(observedAt, out _);
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> ObserveRuntimeEvent(
        TimestampMs eventAt, string? eventId, string? eventKind, string? summary, JsonElement? payload)
    {
        if (State is not SchedulerStateRunning running)
            return InvalidTransitionErr(Status, TransitionAction.ObserveRuntimeEvent);

        Conversation?.ObserveEvent(eventAt, eventId, eventKind, summary, payload);
        running.Stall = running.Stall.ObserveActivity(eventAt, out _);
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> QueueRetry(RetryEntry retry, WorkerOutcomeRecord outcome)
    {
        var retryBinding = ValidateRetryBinding(retry);
        if (retryBinding.IsErr) return retryBinding;

        RetryAttempt? expectedAttempt;
        switch (State)
        {
            case SchedulerStateClaimed c:
                {
                    var run = c.Run;
                    if (outcome.WorkerId != run.WorkerId)
                        return Result<IssueExecution, StateTransitionError>.Err(
                            new WorkerMismatch(run.WorkerId, outcome.WorkerId));
                    if (outcome.Attempt != run.Attempt)
                        return Result<IssueExecution, StateTransitionError>.Err(
                            new AttemptMismatch(run.Attempt, outcome.Attempt));
                    expectedAttempt = RetryAttempt.After(run.Attempt) is { IsOk: true } after ? after.Value : null;
                    break;
                }
            case SchedulerStateRunning r:
                {
                    var run = r.Run;
                    if (outcome.WorkerId != run.WorkerId)
                        return Result<IssueExecution, StateTransitionError>.Err(
                            new WorkerMismatch(run.WorkerId, outcome.WorkerId));
                    if (outcome.Attempt != run.Attempt)
                        return Result<IssueExecution, StateTransitionError>.Err(
                            new AttemptMismatch(run.Attempt, outcome.Attempt));
                    expectedAttempt = RetryAttempt.After(run.Attempt) is { IsOk: true } after ? after.Value : null;
                    break;
                }
            default:
                return InvalidTransitionErr(Status, TransitionAction.QueueRetry);
        }

        if (expectedAttempt != retry.Attempt)
            return Result<IssueExecution, StateTransitionError>.Err(
                new AttemptMismatch(expectedAttempt, retry.Attempt));

        RecordOutcome(outcome);
        State = new SchedulerStateRetryQueued(retry);
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> Release(
        TimestampMs releasedAt, ReleaseReason reason, WorkerOutcomeRecord? outcome)
    {
        if (State is SchedulerStateReleased)
            return InvalidTransitionErr(Status, TransitionAction.Release);

        if (outcome is not null)
            RecordOutcome(outcome);

        State = new SchedulerStateReleased(releasedAt, reason);
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    public Result<IssueExecution, StateTransitionError> Reopen(TimestampMs observedAt)
    {
        if (State is not SchedulerStateReleased released)
            return InvalidTransitionErr(Status, TransitionAction.Reopen);

        if (!released.Reason.PreservesReactivationState())
        {
            Workspace = null;
            Conversation = null;
        }
        State = new SchedulerStateUnclaimed(observedAt);
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    void RecordOutcome(WorkerOutcomeRecord outcome)
    {
        LastWorkerOutcome = outcome;
        if (RecentWorkerOutcomes.Count == MaxRecentWorkerOutcomes)
            RecentWorkerOutcomes.RemoveAt(0);
        RecentWorkerOutcomes.Add(outcome);
    }

    Result<IssueExecution, StateTransitionError> ValidateRunBinding(RunAttempt run)
    {
        if (run.IssueId != Issue.Id)
            return Result<IssueExecution, StateTransitionError>.Err(
                new IssueMismatch(Issue.Id, run.IssueId));

        if (Workspace is null)
            return Result<IssueExecution, StateTransitionError>.Err(
                new WorkspaceNotAttached(run.WorkspacePath));

        var expected = ComparableWorkspacePath(Workspace.Path);
        var actual = ComparableWorkspacePath(run.WorkspacePath);
        if (expected != actual)
            return Result<IssueExecution, StateTransitionError>.Err(
                new WorkspacePathMismatch(expected, actual));

        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    Result<IssueExecution, StateTransitionError> ValidateRetryBinding(RetryEntry retry)
    {
        if (retry.IssueId != Issue.Id)
            return Result<IssueExecution, StateTransitionError>.Err(
                new IssueMismatch(Issue.Id, retry.IssueId));
        return Result<IssueExecution, StateTransitionError>.Ok(this);
    }

    // ── Local return helpers for pattern-match arms ─────────────────────────

    static Result<IssueExecution, StateTransitionError> InvalidTransitionErr(
        SchedulerStatus from, TransitionAction action) =>
        Result<IssueExecution, StateTransitionError>.Err(new InvalidTransition(from, action));

    // ── Path helpers ────────────────────────────────────────────────────────

    static WorkspaceKey IssueWorkspaceKey(NormalizedIssue issue) =>
        WorkspaceKey.New(issue.Identifier.Value).Value;

    static bool WorkspacePathMatchesKey(string path, WorkspaceKey key) =>
        Path.GetFileName(path) == key.Value;

    static string ComparableWorkspacePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            try { return Path.GetFullPath(path); }
            catch { return NormalizeWorkspacePath(path); }
        }
        return NormalizeWorkspacePath(path);
    }

    static string NormalizeWorkspacePath(string path)
    {
        // ht: manual component walk resolving ".." lexically (Rust normalize_workspace_path).
        //   Fallback when GetFullPath throws. Handles both / and \ separators.
        var parts = path.Split('/', '\\');
        var stack = new List<string>();
        bool hasRoot = path.Length > 0 && (path[0] == '/' || path[0] == '\\');
        string? prefix = null;
        if (path.Length >= 2 && path[1] == ':')
        {
            prefix = path.Substring(0, 2);
            hasRoot = true;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0 && stack[^1] != "..")
                    stack.RemoveAt(stack.Count - 1);
                else if (!hasRoot)
                    stack.Add("..");
            }
            else
                stack.Add(part);
        }

        var sb = new StringBuilder();
        if (prefix is not null) { sb.Append(prefix); sb.Append(Path.DirectorySeparatorChar); }
        else if (hasRoot) { sb.Append(Path.DirectorySeparatorChar); }
        for (var i = 0; i < stack.Count; i++)
        {
            if (i > 0) sb.Append(Path.DirectorySeparatorChar);
            sb.Append(stack[i]);
        }

        return sb.Length == 0 ? "." : sb.ToString();
    }
}
