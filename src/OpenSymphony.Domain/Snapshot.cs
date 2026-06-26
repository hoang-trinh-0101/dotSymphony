namespace OpenSymphony.Domain;

// ── Snapshot types (ported from snapshot.rs) ───────────────────────────────

// ht: Rust #[serde(rename_all = "snake_case")] + #[default] Unknown.
//   JsonStringEnumConverter with SnakeCaseLower handles serialization.
public enum HealthStatus
{
    Unknown,
    Starting,
    Healthy,
    Degraded,
    Failed,
}

public sealed record ComponentHealthSnapshot(
    HealthStatus Status,
    string? Detail,
    TimestampMs? UpdatedAt);

public readonly record struct RuntimeUsageTotals
{
    public ulong InputTokens { get; init; }
    public ulong OutputTokens { get; init; }
    public ulong CacheReadTokens { get; init; }
    public ulong TotalTokens { get; init; }
    public ulong RuntimeSeconds { get; init; }
    public ulong? EstimatedCostUsdMicros { get; init; }

    public static RuntimeUsageTotals Default => new();
}

public sealed record DaemonSnapshot(
    HealthStatus Health,
    ulong PollIntervalMs,
    uint MaxConcurrentAgents,
    uint RunningIssueCount,
    uint RetryQueueCount,
    TimestampMs? LastPollAt,
    ComponentHealthSnapshot AgentServer,
    RuntimeUsageTotals Usage)
{
    // ht: Rust DaemonSnapshot::new sets running_issue_count=0, retry_queue_count=0.
    public static DaemonSnapshot New(
        HealthStatus health,
        ulong pollIntervalMs,
        uint maxConcurrentAgents,
        TimestampMs? lastPollAt,
        ComponentHealthSnapshot agentServer,
        RuntimeUsageTotals usage) =>
        new(health, pollIntervalMs, maxConcurrentAgents, 0, 0, lastPollAt, agentServer, usage);
}

public sealed record WorkerAttemptSnapshot(
    StringIdentifier<WorkerId> WorkerId,
    RetryAttempt? Attempt,
    uint NormalRetryCount,
    uint TurnCount,
    uint MaxTurns)
{
    public static WorkerAttemptSnapshot From(RunAttempt run) =>
        new(run.WorkerId, run.Attempt, run.NormalRetryCount, run.TurnCount, run.MaxTurns);
}

public sealed record RetrySnapshot(
    RetryAttempt Attempt,
    uint NormalRetryCount,
    TimestampMs ScheduledAt,
    TimestampMs DueAt,
    RetryReason Reason,
    string? Error)
{
    public static RetrySnapshot From(RetryEntry retry) =>
        new(retry.Attempt, retry.NormalRetryCount, retry.ScheduledAt, retry.DueAt, retry.Reason, retry.Error);
}

public sealed record RuntimeStateSnapshot(
    SchedulerStatus State,
    TimestampMs? ClaimedAt,
    TimestampMs? StartedAt,
    TimestampMs? ReleasedAt,
    ReleaseReason? ReleaseReason,
    WorkerAttemptSnapshot? Worker,
    TimestampMs? LastEventAt,
    TimestampMs? StalledAt)
{
    // ht: Rust from_execution is private — only called by IssueSnapshot.From.
    internal static RuntimeStateSnapshot FromExecution(IssueExecution execution)
    {
        var conversation = execution.Conversation;
        var lastEventAt = conversation?.LastEventAt;

        return execution.State switch
        {
            SchedulerStateUnclaimed s => new(
                SchedulerStatus.Unclaimed, null, null, null, null, null, lastEventAt, null),
            SchedulerStateClaimed c => new(
                SchedulerStatus.Claimed, c.Run.ClaimedAt, c.Run.StartedAt, null, null,
                WorkerAttemptSnapshot.From(c.Run), lastEventAt, null),
            SchedulerStateRunning r => new(
                SchedulerStatus.Running, r.Run.ClaimedAt, r.Run.StartedAt, null, null,
                WorkerAttemptSnapshot.From(r.Run), lastEventAt, r.Stall.StalledAt),
            SchedulerStateRetryQueued => new(
                SchedulerStatus.RetryQueued, null, null, null, null, null, lastEventAt, null),
            SchedulerStateReleased rel => new(
                SchedulerStatus.Released, null, null, rel.ReleasedAt, rel.Reason, null, lastEventAt, null),
            _ => throw new InvalidOperationException("unknown scheduler state"),
        };
    }
}

public sealed record IssueSnapshot(
    NormalizedIssue Issue,
    RuntimeStateSnapshot Runtime,
    WorkspaceRecord? Workspace,
    ConversationMetadata? Conversation,
    RetrySnapshot? Retry,
    WorkerOutcomeRecord? LastWorkerOutcome,
    List<WorkerOutcomeRecord> RecentWorkerOutcomes)
{
    public static IssueSnapshot From(IssueExecution execution) => new(
        execution.Issue,
        RuntimeStateSnapshot.FromExecution(execution),
        execution.Workspace,
        execution.Conversation,
        execution.Retry is { } retry ? RetrySnapshot.From(retry) : null,
        execution.LastWorkerOutcome,
        execution.RecentWorkerOutcomes.ToList());
}

public sealed record OrchestratorSnapshot(
    TimestampMs GeneratedAt,
    DaemonSnapshot Daemon,
    List<IssueSnapshot> Issues)
{
    // ht: Rust OrchestratorSnapshot::new counts running/retry-queued issues and
    //   overrides the daemon's running_issue_count/retry_queue_count via struct update.
    public static OrchestratorSnapshot New(
        TimestampMs generatedAt,
        DaemonSnapshot daemon,
        List<IssueSnapshot> issues)
    {
        uint runningIssueCount = 0, retryQueueCount = 0;
        foreach (var issue in issues)
        {
            if (issue.Runtime.State == SchedulerStatus.Running) runningIssueCount++;
            else if (issue.Runtime.State == SchedulerStatus.RetryQueued) retryQueueCount++;
        }

        return new(generatedAt,
            daemon with { RunningIssueCount = runningIssueCount, RetryQueueCount = retryQueueCount },
            issues);
    }
}
