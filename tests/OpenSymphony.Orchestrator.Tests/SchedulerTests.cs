using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;
using OpenSymphony.Orchestrator;
using OpenSymphony.Workflow;

namespace OpenSymphony.Orchestrator.Tests;

// ht: scheduler.rs has no #[cfg(test)] module. Per ht rule, non-trivial logic gets one
//   runnable check. These tests exercise dispatch, retry-queue, terminal reconciliation,
//   and stall handling via fake backends — the four required orchestrator test areas.
public class SchedulerTests
{
    static TrackerIssue ActiveIssue(string identifier, byte priority = 1) => new()
    {
        Id = $"issue-{identifier.ToLowerInvariant()}",
        Identifier = identifier,
        Url = $"https://linear.app/example/{identifier}",
        Title = $"Issue {identifier}",
        State = "In Progress",
        StateKind = TrackerIssueStateKind.Started,
        Priority = priority,
        Labels = new(),
        BlockedBy = new(),
        SubIssues = new(),
        CreatedAt = DateTimeOffset.Parse("2026-03-20T00:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTimeOffset.Parse("2026-03-20T00:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    static TrackerIssue TerminalIssue(string identifier) => new()
    {
        Id = $"issue-{identifier.ToLowerInvariant()}",
        Identifier = identifier,
        Url = $"https://linear.app/example/{identifier}",
        Title = $"Issue {identifier}",
        State = "Done",
        StateKind = TrackerIssueStateKind.Completed,
        Priority = 1,
        Labels = new(),
        BlockedBy = new(),
        SubIssues = new(),
        CreatedAt = DateTimeOffset.Parse("2026-03-20T00:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTimeOffset.Parse("2026-03-20T00:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    static SchedulerConfig Config(uint maxConcurrent = 2, ulong? stallTimeout = null) => new()
    {
        PollIntervalMs = 1000,
        MaxConcurrentAgents = maxConcurrent,
        MaxTurns = 8,
        MaxConcurrentAgentsByState = new(),
        RetryPolicy = RetryPolicy.Default,
        StallTimeoutMs = stallTimeout,
        ActiveStates = new() { "In Progress" },
        TerminalStates = new() { "Done", "Canceled" },
        Routing = new RoutingConfig(
            "openhands_agent_server", null, null,
            "OPENSYMPHONY_HARNESS", "OPENSYMPHONY_MODEL", "OPENSYMPHONY_MODEL_PROFILE",
            false, false, false, false),
    };

    sealed class FakeTracker : ITrackerBackend
    {
        public List<TrackerIssue> Active { get; set; } = new();
        public List<TrackerIssue> Terminal { get; set; } = new();
        public List<TrackerIssueStateSnapshot> StateSnapshots { get; set; } = new();
        public Task<List<TrackerIssue>> CandidateIssuesAsync() => Task.FromResult(Active.ToList());
        public Task<List<TrackerIssue>> TerminalIssuesAsync() => Task.FromResult(Terminal.ToList());
        public Task<List<TrackerIssueStateSnapshot>> IssueStatesByIdsAsync(IReadOnlyList<string> issueIds)
            => Task.FromResult(StateSnapshots.Where(s => issueIds.Contains(s.Id)).ToList());
    }

    sealed class FakeWorkspace : IWorkspaceBackend
    {
        public List<RecoveryRecord> Recovery { get; set; } = new();
        public Task<WorkspaceRecord> EnsureWorkspaceAsync(NormalizedIssue issue, TimestampMs observedAt)
            => Task.FromResult(new WorkspaceRecord(
                $"/tmp/workspaces/{issue.Identifier.Value}",
                WorkspaceKey.New(issue.Identifier.Value).Value, true, observedAt, observedAt, null));
        public Task<List<RecoveryRecord>> RecoverWorkspacesAsync() => Task.FromResult(Recovery.ToList());
        public Task CleanupWorkspaceAsync(WorkspaceRecord workspace, bool terminal) => Task.CompletedTask;
    }

    sealed class FakeWorker : IWorkerBackend
    {
        public List<WorkerStartRequest> StartRequests { get; } = new();
        public List<(StringIdentifier<WorkerId>, WorkerAbortReason)> Aborts { get; } = new();
        public Queue<List<WorkerUpdate>> UpdateQueue { get; } = new();
        public bool LaunchSucceeds { get; set; } = true;

        public Task<WorkerLaunch> StartWorkerAsync(WorkerStartRequest request)
        {
            StartRequests.Add(request);
            if (!LaunchSucceeds) throw new Exception("boom");
            var conv = new ConversationMetadata(
                StringIdentifier<ConversationId>.New($"conv-{request.Issue.Identifier.Value}").Value)
            { FreshConversation = true, StreamState = RuntimeStreamState.Ready };
            return Task.FromResult(new WorkerLaunch(conv));
        }

        public Task<List<WorkerUpdate>> PollUpdatesAsync()
            => Task.FromResult(UpdateQueue.TryDequeue(out var updates) ? updates : new List<WorkerUpdate>());

        public Task AbortWorkerAsync(StringIdentifier<WorkerId> workerId, WorkerAbortReason reason)
        {
            Aborts.Add((workerId, reason));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchesReadyIssueAndTracksRunningWorker()
    {
        var tracker = new FakeTracker { Active = { ActiveIssue("COE-100") } };
        var workspace = new FakeWorkspace();
        var worker = new FakeWorker();
        var scheduler = new Scheduler<FakeTracker, FakeWorkspace, FakeWorker>(tracker, workspace, worker, Config());

        await scheduler.TickAsync(TimestampMs.New(1000));

        Assert.Single(worker.StartRequests);
        var issueId = worker.StartRequests[0].Issue.Id;
        var exec = scheduler.Execution(issueId);
        Assert.NotNull(exec);
        Assert.Equal(SchedulerStatus.Running, exec!.Status);
    }

    [Fact]
    public async Task QueuesRetryWhenWorkerReportsFailure()
    {
        var tracker = new FakeTracker { Active = { ActiveIssue("COE-200") } };
        var workspace = new FakeWorkspace();
        var worker = new FakeWorker();
        var scheduler = new Scheduler<FakeTracker, FakeWorkspace, FakeWorker>(tracker, workspace, worker, Config());

        await scheduler.TickAsync(TimestampMs.New(1000));
        var issueId = worker.StartRequests[0].Issue.Id;
        var workerId = worker.StartRequests[0].Run.WorkerId;

        worker.UpdateQueue.Enqueue(new List<WorkerUpdate>
        {
            new WorkerUpdate.Finished(workerId,
                WorkerOutcomeRecord.FromRun(
                    worker.StartRequests[0].Run, WorkerOutcomeKind.Failed,
                    TimestampMs.New(2000), "failed", "error")),
        });

        await scheduler.TickAsync(TimestampMs.New(2000));

        var exec = scheduler.Execution(issueId);
        Assert.NotNull(exec);
        Assert.Equal(SchedulerStatus.RetryQueued, exec!.Status);
        Assert.NotNull(exec.Retry);
        Assert.Equal(RetryReason.Failure, exec.Retry!.Reason);
    }

    [Fact]
    public async Task ReleasesIssueWhenTrackerReportsTerminal()
    {
        var tracker = new FakeTracker { Active = { ActiveIssue("COE-300") } };
        var workspace = new FakeWorkspace();
        var worker = new FakeWorker();
        var scheduler = new Scheduler<FakeTracker, FakeWorkspace, FakeWorker>(tracker, workspace, worker, Config());

        await scheduler.TickAsync(TimestampMs.New(1000));
        var issueId = worker.StartRequests[0].Issue.Id;
        var workerId = worker.StartRequests[0].Run.WorkerId;

        // Next tick: issue is now terminal, worker still running → abort + release.
        tracker.Active.Clear();
        tracker.Terminal.Add(TerminalIssue("COE-300"));
        worker.UpdateQueue.Enqueue(new List<WorkerUpdate>());

        await scheduler.TickAsync(TimestampMs.New(2000));

        var exec = scheduler.Execution(issueId);
        Assert.NotNull(exec);
        Assert.Equal(SchedulerStatus.Released, exec!.Status);
        Assert.Contains(worker.Aborts, a => a.Item1 == workerId && a.Item2 == WorkerAbortReason.TrackerTerminal);
    }

    [Fact]
    public async Task StalledWorkerIsAbortedAndQueuedForRetry()
    {
        var tracker = new FakeTracker { Active = { ActiveIssue("COE-400") } };
        var workspace = new FakeWorkspace();
        var worker = new FakeWorker();
        // ht: stall timeout = 100ms; observed_at advances past it.
        var scheduler = new Scheduler<FakeTracker, FakeWorkspace, FakeWorker>(tracker, workspace, worker, Config(stallTimeout: 100));

        await scheduler.TickAsync(TimestampMs.New(1000));
        var issueId = worker.StartRequests[0].Issue.Id;
        var workerId = worker.StartRequests[0].Run.WorkerId;

        // No updates → stall deadline (1000 + 100 = 1100) is passed at observed_at=2000.
        worker.UpdateQueue.Enqueue(new List<WorkerUpdate>());
        await scheduler.TickAsync(TimestampMs.New(2000));

        Assert.Contains(worker.Aborts, a => a.Item1 == workerId && a.Item2 == WorkerAbortReason.Stalled);
        var exec = scheduler.Execution(issueId);
        Assert.NotNull(exec);
        Assert.Equal(SchedulerStatus.RetryQueued, exec!.Status);
        Assert.Equal(RetryReason.Stalled, exec.Retry!.Reason);
    }

    [Fact]
    public async Task RespectsMaxConcurrentAgents()
    {
        var tracker = new FakeTracker
        {
            Active = { ActiveIssue("COE-1"), ActiveIssue("COE-2"), ActiveIssue("COE-3") },
        };
        var workspace = new FakeWorkspace();
        var worker = new FakeWorker();
        var scheduler = new Scheduler<FakeTracker, FakeWorkspace, FakeWorker>(tracker, workspace, worker, Config(maxConcurrent: 2));

        await scheduler.TickAsync(TimestampMs.New(1000));

        Assert.Equal(2, worker.StartRequests.Count);
    }

    [Fact]
    public void DecideIssueRouteReturnsHarnessDecisionForValidHarness()
    {
        var config = Config();
        var issue = new NormalizedIssue(
            StringIdentifier<IssueId>.New("lin_1").Value,
            StringIdentifier<IssueIdentifier>.New("COE-1").Value,
            "title", null, null,
            new IssueState(null, "In Progress", IssueStateCategory.Active),
            null, null, new(), null, new(), new(), null, null);

        var route = SchedulerRouting.DecideIssueRoute(issue, config);

        Assert.Equal("openhands_agent_server", route.HarnessKind);
        Assert.Equal("issue_execution", route.TaskType);
        Assert.False(route.DryRun);
    }

    [Fact]
    public void DecideIssueRouteRejectsUnknownHarness()
    {
        var config = Config();
        config.Routing = config.Routing with { Harness = "unknown_harness" };
        var issue = new NormalizedIssue(
            StringIdentifier<IssueId>.New("lin_1").Value,
            StringIdentifier<IssueIdentifier>.New("COE-1").Value,
            "title", null, null,
            new IssueState(null, "In Progress", IssueStateCategory.Active),
            null, null, new(), null, new(), new(), null, null);

        Assert.Throws<SchedulerError>(() => SchedulerRouting.DecideIssueRoute(issue, config));
    }
}
