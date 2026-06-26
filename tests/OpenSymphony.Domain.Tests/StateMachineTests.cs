using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Domain.Tests;

public class StateMachineTests
{
    // ── Test helpers (ported from lib.rs) ───────────────────────────────────

    static TimestampMs ts(ulong v) => TimestampMs.New(v);

    static T Must<T, E>(Result<T, E> result) where E : StateTransitionError =>
        result.IsOk ? result.Value : throw new Exception(result.Error.ToString());

    static T Must<T, E>(Result<T, E> result, string label) where E : Enum =>
        result.IsOk ? result.Value : throw new Exception($"{label}: {result.Error}");

    static T MustSome<T>(T? value, string message) where T : class =>
        value ?? throw new Exception(message);

    static NormalizedIssue sampleIssue() => new(
        StringIdentifier<IssueId>.New("lin_260").Value,
        StringIdentifier<IssueIdentifier>.New("COE-260").Value,
        "Domain model and orchestrator state machine",
        "Define the shared orchestration model.",
        1,
        new IssueState(null, "In Progress", IssueStateCategory.Active),
        "leonardogonzalez/coe-260-domain-model-and-orchestrator-state-machine",
        "https://linear.app/trilogy-ai-coe/issue/COE-260/domain-model-and-orchestrator-state-machine",
        new() { "foundation", "contracts" },
        null, new(),
        new() { new IssueRef(StringIdentifier<IssueId>.New("lin_261").Value,
            StringIdentifier<IssueIdentifier>.New("COE-261").Value, "Done") },
        ts(10), ts(20));

    static WorkspaceRecord sampleWorkspace() => new(
        "/tmp/workspaces/COE-260",
        WorkspaceKey.New("COE-260").Value,
        false, ts(11), ts(21), ts(22));

    static RunAttempt sampleRun(NormalizedIssue issue, WorkspaceRecord workspace,
        RetryAttempt? attempt, TimestampMs claimedAt) =>
        RunAttempt.New(
            StringIdentifier<WorkerId>.New("worker-1").Value,
            issue.Id, issue.Identifier, workspace.Path, claimedAt, attempt, 8);

    static ConversationMetadata sampleConversation(bool freshConversation) => new(
        StringIdentifier<ConversationId>.New("conv_260").Value)
    {
        ServerBaseUrl = "http://127.0.0.1:3000",
        TransportTarget = "loopback",
        HttpAuthMode = "none",
        WebsocketAuthMode = "none",
        FreshConversation = freshConversation,
        RuntimeContractVersion = "openhands-sdk-agent-server-v1",
        StreamState = RuntimeStreamState.Ready,
        InputTokens = 1024,
        OutputTokens = 512,
        CacheReadTokens = 256,
        TotalTokens = 1536,
    };

    // ── #1: Full lifecycle ──────────────────────────────────────────────────

    [Fact]
    public void StateTransitionsAreExplicitAndTestable()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        Assert.Equal(SchedulerStatus.Claimed, execution.Status);

        var session = sampleConversation(true);
        session.StreamState = RuntimeStreamState.Attaching;

        execution = Must(execution.StartRunning(ts(50), DurationMs.New(300_000), session));
        Assert.Equal(SchedulerStatus.Running, execution.Status);

        Must(execution.RecordTurnStarted(ts(55)));
        Must(execution.ObserveRuntimeEvent(
            ts(56), "evt_1", "conversation_state_update", "ready", null));

        var running = MustSome(execution.CurrentRun, "running attempt must exist");
        Assert.Equal(1u, running.TurnCount);
        var outcome = WorkerOutcomeRecord.FromRun(
            running, WorkerOutcomeKind.Succeeded, ts(60), "worker exited cleanly", null);

        var retry = Must(RetryEntry.Continuation(issue, running.Attempt, 0, ts(60), RetryPolicy.Default),
            "continuation retry");

        execution = Must(execution.QueueRetry(retry, outcome));
        Assert.Equal(SchedulerStatus.RetryQueued, execution.Status);
        Assert.Equal(retry.Attempt, MustSome(execution.Retry, "retry metadata must exist").Attempt);
        var retrySnapshot = execution.Snapshot();
        Assert.Equal("conv_260",
            MustSome(retrySnapshot.Conversation, "retry-queued snapshots must retain conversation metadata")
            .ConversationId.Value);
        Assert.Equal(WorkerOutcomeKind.Succeeded,
            MustSome(execution.LastWorkerOutcome, "last worker outcome must be recorded").Outcome);

        var retryRun = sampleRun(issue, workspace, retry.Attempt, ts(61));
        execution = Must(execution.Claim(retryRun));
        Assert.Equal(SchedulerStatus.Claimed, execution.Status);
        Assert.Equal(1u,
            MustSome(execution.CurrentRun, "claimed retry run must exist").NormalRetryCount);

        execution = Must(execution.Release(ts(70), ReleaseReason.TrackerInactive, null));
        Assert.Equal(SchedulerStatus.Released, execution.Status);

        var snapshot = execution.Snapshot();
        Assert.Equal(SchedulerStatus.Released, snapshot.Runtime.State);
        Assert.Equal(ReleaseReason.TrackerInactive, snapshot.Runtime.ReleaseReason);
        Assert.Equal(1, snapshot.RecentWorkerOutcomes.Count);
    }

    // ── #2: Invalid transitions + attempt mismatches ────────────────────────

    [Fact]
    public void InvalidTransitionsAndAttemptMismatchesAreRejected()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();

        var execution = new IssueExecution(issue, ts(30));
        var error = Assert.IsType<InvalidTransition>(
            execution.StartRunning(ts(50), DurationMs.New(10_000), null).Error);

        var execution2 = new IssueExecution(issue, ts(30));
        Must(execution2.AttachWorkspace(workspace));
        var run = sampleRun(issue, workspace, null, ts(40));
        execution2 = Must(execution2.Claim(run));
        var outcome = new WorkerOutcomeRecord(
            StringIdentifier<WorkerId>.New("worker-1").Value, null,
            WorkerOutcomeKind.Failed, ts(40), ts(41), 0, null, "boom");
        var retry = Must(RetryEntry.Failure(issue, null, 0, ts(41),
            RetryReason.Failure, "boom", RetryPolicy.Default), "failure retry");
        execution2 = Must(execution2.QueueRetry(retry, outcome));

        var wrongAttemptRun = sampleRun(issue, workspace, null, ts(42));
        Assert.IsType<AttemptMismatch>(execution2.Claim(wrongAttemptRun).Error);
    }

    // ── #3: Claim rejects without workspace ─────────────────────────────────

    [Fact]
    public void ClaimRejectsRunsWithoutAnAttachedWorkspace()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        var run = sampleRun(issue, workspace, null, ts(40));
        Assert.IsType<WorkspaceNotAttached>(execution.Claim(run).Error);
    }

    // ── #4: StartRunning requires conversation for first run ────────────────

    [Fact]
    public void StartRunningRequiresConversationMetadataForFirstRun()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        Assert.IsType<ConversationNotAttached>(
            execution.StartRunning(ts(50), DurationMs.New(300), null).Error);
    }

    // ── #5: StartRunning can reuse retained conversation ────────────────────

    [Fact]
    public void StartRunningCanReuseRetainedConversationMetadata()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        execution = Must(execution.StartRunning(ts(50), DurationMs.New(300), sampleConversation(false)));
        execution = Must(execution.Release(ts(60), ReleaseReason.TrackerInactive, null));
        execution = Must(execution.Reopen(ts(70)));

        var run2 = sampleRun(issue, workspace, null, ts(80));
        execution = Must(execution.Claim(run2));
        execution = Must(execution.StartRunning(ts(90), DurationMs.New(300), null));

        Assert.Equal("conv_260",
            MustSome(execution.Conversation, "retained conversation metadata should be reused")
            .ConversationId.Value);
        Assert.Equal(SchedulerStatus.Running, execution.Status);
    }

    // ── #6: Claim accepts equivalent normalized paths ───────────────────────

    [Fact]
    public void ClaimAcceptsEquivalentNormalizedWorkspacePaths()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        run = RunAttempt.New(run.WorkerId, run.IssueId, run.IssueIdentifier,
            "/tmp/workspaces/../workspaces/COE-260", run.ClaimedAt, run.Attempt, run.MaxTurns);

        execution = Must(execution.Claim(run));
        Assert.Equal(SchedulerStatus.Claimed, execution.Status);
    }

    // ── #7: Unix symlink test — skip on Windows ─────────────────────────────

    [Fact(Skip = "Unix-only: symlink root equivalence (#[cfg(unix)] in Rust)")]
    public void ClaimAcceptsWorkspacePathsWithEquivalentSymlinkRoots()
    {
    }

    // ── #10: Reopen preserves workspace+conversation after inactive ─────────

    [Fact]
    public void ReopenPreservesWorkspaceAndConversationAfterInactiveRelease()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        execution = Must(execution.StartRunning(ts(50), DurationMs.New(300_000), sampleConversation(false)));
        execution = Must(execution.Release(ts(60), ReleaseReason.TrackerInactive, null));
        execution = Must(execution.Reopen(ts(70)));

        Assert.Equal(SchedulerStatus.Unclaimed, execution.Status);
        Assert.Equal(workspace, execution.Workspace);
        Assert.NotNull(execution.Conversation);
    }

    // ── #11: Reopen clears workspace+conversation after terminal ────────────

    [Fact]
    public void ReopenClearsWorkspaceAndConversationAfterTerminalRelease()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        execution = Must(execution.StartRunning(ts(50), DurationMs.New(300_000), sampleConversation(false)));
        execution = Must(execution.Release(ts(60), ReleaseReason.TrackerTerminal, null));
        execution = Must(execution.Reopen(ts(70)));

        Assert.Equal(SchedulerStatus.Unclaimed, execution.Status);
        Assert.Null(execution.Workspace);
        Assert.Null(execution.Conversation);
    }

    // ── #12: Recent worker outcomes bounded to 10 ───────────────────────────

    [Fact]
    public void RecentWorkerOutcomesAreBoundedToLatestWindow()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        RetryAttempt? nextAttempt = null;

        for (ulong index = 0; index < 12; index++)
        {
            var claimedAt = ts(40 + index * 10);
            var run = sampleRun(issue, workspace, nextAttempt, claimedAt);
            execution = Must(execution.Claim(run));

            var currentRun = MustSome(execution.CurrentRun, "claimed run must exist");
            var summary = $"outcome {index}";
            var finishedAt = ts(45 + index * 10);
            var outcome = WorkerOutcomeRecord.FromRun(
                currentRun, WorkerOutcomeKind.Failed, finishedAt, summary, "boom");
            var retry = Must(RetryEntry.Failure(issue, currentRun.Attempt, 0, finishedAt,
                RetryReason.Failure, "boom", RetryPolicy.Default), "failure retry");

            nextAttempt = retry.Attempt;
            execution = Must(execution.QueueRetry(retry, outcome));
        }

        var snapshot = execution.Snapshot();
        Assert.Equal("outcome 11", snapshot.LastWorkerOutcome?.Summary);
        Assert.Equal(10, snapshot.RecentWorkerOutcomes.Count);
        Assert.Equal("outcome 2", snapshot.RecentWorkerOutcomes[0].Summary);
        Assert.Equal("outcome 11", snapshot.RecentWorkerOutcomes[9].Summary);
    }

    // ── #13: Snapshot models serialize stably ───────────────────────────────

    [Fact]
    public void SnapshotModelsSerializeStably()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));
        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        var issueSnapshot = IssueSnapshot.From(execution);

        var snapshot = OrchestratorSnapshot.New(
            ts(100),
            DaemonSnapshot.New(
                HealthStatus.Healthy, 30_000, 4, ts(90),
                new ComponentHealthSnapshot(HealthStatus.Healthy, "ready", ts(95)),
                RuntimeUsageTotals.Default),
            new() { issueSnapshot });

        var json = JsonSerializer.Serialize(snapshot, DomainJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(100u, root.GetProperty("generated_at").GetUInt64());
        Assert.Equal("healthy", root.GetProperty("daemon").GetProperty("health").GetString());
        Assert.Equal(0u, root.GetProperty("daemon").GetProperty("running_issue_count").GetUInt64());
        Assert.Equal("COE-260", root.GetProperty("issues")[0].GetProperty("issue").GetProperty("identifier").GetString());
        Assert.Equal("claimed", root.GetProperty("issues")[0].GetProperty("runtime").GetProperty("state").GetString());
        Assert.Equal("/tmp/workspaces/COE-260", root.GetProperty("issues")[0].GetProperty("workspace").GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("issues")[0].GetProperty("retry").ValueKind);
    }

    // ── #14: Replayed runtime events don't hide existing stalls ─────────────

    [Fact]
    public void ReplayedRuntimeEventsDoNotHideExistingStalls()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        execution = Must(execution.StartRunning(ts(50), DurationMs.New(300), sampleConversation(false)));

        Must(execution.ObserveRuntimeEvent(
            ts(60), "evt_latest", "conversation_state_update", "ready", null));
        Must(execution.ObserveRuntimeEvent(
            ts(55), "evt_old", "tool_call", "replayed", null));

        var conversation = MustSome(execution.Conversation, "running execution must keep conversation metadata");
        Assert.Equal(ts(60), conversation.LastEventAt);
        Assert.Equal("evt_latest", conversation.LastEventId);

        var snapshot = execution.Snapshot();
        Assert.Equal(ts(60), snapshot.Runtime.LastEventAt);
        Assert.Equal(ts(360), snapshot.Runtime.StalledAt);
    }

    // ── #15: AttachWorkspace rejects rebinding to different identity ────────

    [Fact]
    public void AttachWorkspaceRejectsRebindingToDifferentIdentity()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var reboundWorkspace = workspace with
        {
            Path = "/tmp/workspaces/COE-260-alt",
            WorkspaceKey = WorkspaceKey.New("COE-260-alt").Value,
        };

        Assert.IsType<WorkspaceIdentityMismatch>(execution.AttachWorkspace(reboundWorkspace).Error);
        Assert.Equal(workspace, execution.Workspace);
    }

    // ── #16: AttachWorkspace rejects first binding for wrong issue path ─────

    [Fact]
    public void AttachWorkspaceRejectsFirstBindingForTheWrongIssuePath()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace() with
        {
            Path = "/tmp/workspaces/COE-261",
        };
        var execution = new IssueExecution(issue, ts(30));

        Assert.IsType<WorkspaceIssueMismatch>(execution.AttachWorkspace(workspace).Error);
        Assert.Null(execution.Workspace);
    }

    // ── #17: AttachWorkspace allows refresh for same identity ───────────────

    [Fact]
    public void AttachWorkspaceAllowsRefreshForSameIdentity()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var refreshedWorkspace = workspace with
        {
            UpdatedAt = ts(99),
            LastSeenTrackerRefreshAt = ts(100),
        };

        Must(execution.AttachWorkspace(refreshedWorkspace));
        Assert.Equal(refreshedWorkspace, execution.Workspace);
    }

    // ── #18: Running snapshot last_event_at stays None without events ───────

    [Fact]
    public void RunningSnapshotLastEventAtStaysNoneWithoutRuntimeEvents()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        execution = Must(execution.StartRunning(ts(50), DurationMs.New(300), sampleConversation(false)));

        Must(execution.RecordTurnStarted(ts(55)));

        var snapshot = execution.Snapshot();
        Assert.Null(snapshot.Runtime.LastEventAt);
        Assert.Equal(ts(355), snapshot.Runtime.StalledAt);
        Assert.Equal(0u, snapshot.Runtime.Worker?.NormalRetryCount);
    }

    // ── #19: QueueRetry rejects outcomes from different worker ──────────────

    [Fact]
    public void QueueRetryRejectsOutcomesFromADifferentWorker()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var run = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(run));
        var outcome = new WorkerOutcomeRecord(
            StringIdentifier<WorkerId>.New("worker-2").Value, null,
            WorkerOutcomeKind.Failed, ts(40), ts(41), 0, "stale worker", "boom");
        var retry = Must(RetryEntry.Failure(issue, null, 0, ts(41),
            RetryReason.Failure, "boom", RetryPolicy.Default), "failure retry");

        Assert.IsType<WorkerMismatch>(execution.QueueRetry(retry, outcome).Error);
    }

    // ── #20: QueueRetry rejects outcomes from different attempt ─────────────

    [Fact]
    public void QueueRetryRejectsOutcomesFromADifferentAttempt()
    {
        var issue = sampleIssue();
        var workspace = sampleWorkspace();
        var execution = new IssueExecution(issue, ts(30));
        Must(execution.AttachWorkspace(workspace));

        var firstRun = sampleRun(issue, workspace, null, ts(40));
        execution = Must(execution.Claim(firstRun));
        var firstOutcome = WorkerOutcomeRecord.FromRun(
            MustSome(execution.CurrentRun, "claimed run must exist"),
            WorkerOutcomeKind.Succeeded, ts(50), "completed", null);
        var firstRetry = Must(RetryEntry.Continuation(issue, null, 0, ts(50), RetryPolicy.Default),
            "continuation retry");
        execution = Must(execution.QueueRetry(firstRetry, firstOutcome));

        var retryRun = sampleRun(issue, workspace, firstRetry.Attempt, ts(60));
        execution = Must(execution.Claim(retryRun));
        var staleOutcome = new WorkerOutcomeRecord(
            StringIdentifier<WorkerId>.New("worker-1").Value, null,
            WorkerOutcomeKind.Failed, ts(40), ts(61), 1, "old attempt", "boom");
        var retry = Must(RetryEntry.Failure(issue, firstRetry.Attempt, 1, ts(61),
            RetryReason.Failure, "boom", RetryPolicy.Default), "failure retry");

        Assert.IsType<AttemptMismatch>(execution.QueueRetry(retry, staleOutcome).Error);
    }
}
