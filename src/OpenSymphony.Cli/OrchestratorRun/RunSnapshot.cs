using System.Collections.Immutable;
using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Cli.OrchestratorRun;

// ht: Port of older/crates/opensymphony-cli/src/orchestrator_run/snapshot.rs
//   Snapshot and control-plane mapping helpers for the runtime CLI.
//   chrono::DateTime<Utc> → DateTimeOffset. HashSet<string> → HashSet<string>.

public static class RunSnapshotMapper
{
    const int RecentEventLimit = 24;

    public static ControlPlaneDaemonSnapshot MapSnapshot(
        OrchestratorSnapshot snapshot,
        string workspaceRoot,
        HashSet<string> terminalStates,
        ControlPlaneAgentServerStatus agentServer,
        ControlPlaneMemoryServerStatus memoryServer,
        ImmutableArray<ControlPlaneRecentEvent> recentEvents)
    {
        var generatedAt = TimestampMsToDateTimeOffset(snapshot.GeneratedAt);
        var lastPollAt = snapshot.Daemon.LastPollAt is { } poll
            ? TimestampMsToDateTimeOffset(poll)
            : generatedAt;

        var daemonStatus = new ControlPlaneDaemonStatus(
            MapDaemonState(snapshot.Daemon.Health),
            lastPollAt,
            workspaceRoot,
            $"poll={snapshot.Daemon.PollIntervalMs}ms, running={snapshot.Daemon.RunningIssueCount}, retry_queue={snapshot.Daemon.RetryQueueCount}"
        );

        var metrics = new ControlPlaneMetricsSnapshot(
            (uint)snapshot.Daemon.RunningIssueCount,
            (uint)snapshot.Daemon.RetryQueueCount,
            snapshot.Daemon.Usage.InputTokens,
            snapshot.Daemon.Usage.OutputTokens,
            snapshot.Daemon.Usage.CacheReadTokens,
            snapshot.Daemon.Usage.TotalTokens,
            snapshot.Daemon.Usage.EstimatedCostUsdMicros ?? 0
        );

        var issues = snapshot.Issues
            .Select(issue => MapIssue(issue, terminalStates, generatedAt))
            .ToList();

        return new ControlPlaneDaemonSnapshot(
            generatedAt,
            daemonStatus,
            agentServer,
            memoryServer,
            metrics,
            issues,
            recentEvents.ToList()
        );
    }

    private static ControlPlaneDaemonState MapDaemonState(HealthStatus health) => health switch
    {
        HealthStatus.Starting => ControlPlaneDaemonState.Starting,
        HealthStatus.Healthy => ControlPlaneDaemonState.Ready,
        HealthStatus.Degraded => ControlPlaneDaemonState.Degraded,
        _ => ControlPlaneDaemonState.Stopped,
    };

    private static ControlPlaneIssueSnapshot MapIssue(
        IssueSnapshot issue,
        HashSet<string> terminalStates,
        DateTimeOffset generatedAt)
    {
        var runtimeState = issue.Runtime.State switch
        {
            SchedulerStatus.Running or SchedulerStatus.Claimed => ControlPlaneIssueRuntimeState.Running,
            SchedulerStatus.RetryQueued => ControlPlaneIssueRuntimeState.RetryQueued,
            SchedulerStatus.Released => MapReleasedState(issue),
            SchedulerStatus.Unclaimed => ControlPlaneIssueRuntimeState.Idle,
            _ => ControlPlaneIssueRuntimeState.Idle,
        };

        var lastOutcome = MapWorkerOutcome(issue, runtimeState);
        var lastEventAt = issue.Conversation?.LastEventAt is { } convEventAt
            ? TimestampMsToDateTimeOffset(convEventAt)
            : issue.LastWorkerOutcome?.FinishedAt is { } outcomeFinishedAt
                ? TimestampMsToDateTimeOffset(outcomeFinishedAt)
                : generatedAt;

        DateTimeOffset? startedAt;
        if (issue.Runtime.StartedAt is { } runtimeStarted)
        {
            startedAt = TimestampMsToDateTimeOffset(runtimeStarted);
        }
        else if (issue.LastWorkerOutcome?.StartedAt is { } outcomeStarted)
        {
            startedAt = TimestampMsToDateTimeOffset(outcomeStarted);
        }
        else
        {
            startedAt = null;
        }

        DateTimeOffset? finishedAt;
        if (issue.Runtime.ReleasedAt is { } runtimeReleased)
        {
            finishedAt = TimestampMsToDateTimeOffset(runtimeReleased);
        }
        else if (issue.LastWorkerOutcome?.FinishedAt is { } outcomeFinished)
        {
            finishedAt = TimestampMsToDateTimeOffset(outcomeFinished);
        }
        else
        {
            finishedAt = null;
        }

        var conversationRuntimeSeconds = issue.Conversation?.RuntimeSeconds ?? 0;
        var calculatedRuntimeSeconds = RuntimeSecondsFromTimestamps(
            startedAt, finishedAt, generatedAt, runtimeState);
        var runtimeSeconds = Math.Max(conversationRuntimeSeconds, calculatedRuntimeSeconds);

        var worker = issue.Runtime.Worker;
        var lastWorkerOutcome = issue.LastWorkerOutcome;

        var turnCount = worker?.TurnCount
            ?? lastWorkerOutcome?.TurnCount
            ?? 0;

        var maxTurns = worker?.MaxTurns ?? 0;

        var blocked = issue.Issue.BlockedBy.Any(blocker =>
            blocker.State is null || !IsTerminalState(terminalStates, blocker.State))
            || (issue.Issue.SubIssues.Count > 0
                && issue.Issue.SubIssues.Any(sub => !IsTerminalState(terminalStates, sub.State)));

        var blockedBy = issue.Issue.BlockedBy
            .Where(blocker => blocker.Identifier.HasValue)
            .Select(blocker => blocker.Identifier.Value.Value)
            .ToList();

        var recentEvents = issue.Conversation?.RecentActivity
            .Select(activity => new ControlPlaneConversationEvent(
                activity.EventId,
                TimestampMsToDateTimeOffset(activity.HappenedAt),
                activity.Kind ?? "",
                activity.Summary ?? "",
                activity.Payload,
                activity.Sequence
            ))
            .Reverse()
            .ToList()
            ?? new List<ControlPlaneConversationEvent>();

        // TODO: ModifiedFiles doesn't exist on ConversationMetadata in Domain
        // Need to either add it to Domain or compute it from RecentActivity
        var modifiedFiles = new List<ControlPlaneFileChange>();

        return new ControlPlaneIssueSnapshot(
            issue.Issue.Identifier.Value,
            issue.Issue.Title,
            issue.Issue.State.Name,
            runtimeState,
            lastOutcome,
            lastEventAt,
            Suffix(issue.Conversation?.ConversationId),
            SuffixPath(issue.Workspace?.Path),
            (uint)(issue.Retry?.NormalRetryCount ?? 0),
            issue.Runtime.ClaimedAt is { } claimed
                ? TimestampMsToDateTimeOffset(claimed)
                : null,
            startedAt,
            finishedAt,
            (uint)turnCount,
            (uint)maxTurns,
            (ulong)runtimeSeconds,
            blocked,
            blockedBy,
            issue.Conversation?.ServerBaseUrl,
            issue.Conversation?.TransportTarget,
            issue.Conversation?.HttpAuthMode,
            issue.Conversation?.WebsocketAuthMode,
            issue.Conversation?.WebsocketQueryParamName,
            recentEvents,
            modifiedFiles,
            issue.Conversation?.InputTokens ?? 0,
            issue.Conversation?.OutputTokens ?? 0,
            issue.Conversation?.CacheReadTokens ?? 0,
            issue.Conversation?.TotalTokens ?? 0,
            // TODO: Detached, CancelAcknowledged, CancelFailed don't exist on ConversationMetadata
            // Need to add them to Domain or derive from StreamState
            false,
            false,
            false
        );
    }

    private static ControlPlaneIssueRuntimeState MapReleasedState(IssueSnapshot issue)
    {
        var outcomeKind = issue.LastWorkerOutcome?.Outcome;
        if (outcomeKind is WorkerOutcomeKind.Failed
            or WorkerOutcomeKind.TimedOut
            or WorkerOutcomeKind.Stalled)
        {
            return ControlPlaneIssueRuntimeState.Failed;
        }
        return ControlPlaneIssueRuntimeState.Completed;
    }

    private static ControlPlaneWorkerOutcome MapWorkerOutcome(
        IssueSnapshot issue,
        ControlPlaneIssueRuntimeState runtimeState)
    {
        if (runtimeState == ControlPlaneIssueRuntimeState.Running)
        {
            return ControlPlaneWorkerOutcome.Running;
        }

        var outcomeKind = issue.LastWorkerOutcome?.Outcome;
        return outcomeKind switch
        {
            WorkerOutcomeKind.Succeeded => ControlPlaneWorkerOutcome.Completed,
            WorkerOutcomeKind.Failed => ControlPlaneWorkerOutcome.Failed,
            WorkerOutcomeKind.TimedOut => ControlPlaneWorkerOutcome.Failed,
            WorkerOutcomeKind.Stalled => ControlPlaneWorkerOutcome.Failed,
            WorkerOutcomeKind.Cancelled => ControlPlaneWorkerOutcome.Canceled,
            _ => ControlPlaneWorkerOutcome.Unknown,
        };
    }

    private static ControlPlaneFileChangeKind MapFileChangeKind(string kind) => kind.ToLowerInvariant() switch
    {
        "created" => ControlPlaneFileChangeKind.Created,
        "modified" => ControlPlaneFileChangeKind.Modified,
        "removed" => ControlPlaneFileChangeKind.Removed,
        _ => ControlPlaneFileChangeKind.Modified,
    };

    private static bool IsTerminalState(HashSet<string> terminalStates, string state) =>
        terminalStates.Contains(state);

    private static double RuntimeSecondsFromTimestamps(
        DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt,
        DateTimeOffset generatedAt,
        ControlPlaneIssueRuntimeState runtimeState)
    {
        if (startedAt is null) return 0;

        var end = finishedAt ?? generatedAt;
        var duration = end - startedAt.Value;
        return Math.Max(0, duration.TotalSeconds);
    }

    private static string Suffix(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 8)
            return value ?? "-";

        return value.Substring(value.Length - 8);
    }

    private static string SuffixPath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "-";

        var parts = value.Split(['/', '\\']);
        if (parts.Length == 0)
            return "-";

        var last = parts[^1];
        if (last.Length <= 20)
            return last;

        return "..." + last.Substring(last.Length - 20);
    }

    private static DateTimeOffset TimestampMsToDateTimeOffset(TimestampMs timestamp) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp.Value).DateTime;

    public static HashSet<string> TerminalStateSet(List<string> terminalStates) =>
        terminalStates.ToHashSet(StringComparer.Ordinal);

    public static ImmutableArray<ControlPlaneRecentEvent> PushRecentEvent(
        ImmutableArray<ControlPlaneRecentEvent> events,
        ControlPlaneRecentEvent newEvent)
    {
        if (events.Length >= RecentEventLimit)
        {
            events = events.RemoveAt(0);
        }
        return events.Add(newEvent);
    }
}