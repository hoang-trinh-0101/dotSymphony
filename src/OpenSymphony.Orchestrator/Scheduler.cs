using System.Text.Json;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;
using OpenSymphony.Workflow;
using static OpenSymphony.Orchestrator.SchedulerHelpers;

namespace OpenSymphony.Orchestrator;

// ht: Port of older/crates/opensymphony-orchestrator/src/scheduler.rs Scheduler + helpers.
//   Rust generic Scheduler<T,W,M> with trait bounds → C# Scheduler<TrackerT,WorkspaceT,WorkerT>
//   with interface constraints. tokio::Mutex removed (single-process, lock statement if needed).
//   tracing macros removed. run_until_shutdown uses CancellationToken.

public sealed class Scheduler<TrackerT, WorkspaceT, WorkerT>
    where TrackerT : ITrackerBackend
    where WorkspaceT : IWorkspaceBackend
    where WorkerT : IWorkerBackend
{
    const ulong DisabledStallTimeoutMs = ulong.MaxValue / 4;
    const string RoutingTaskIssueExecution = "issue_execution";

    readonly TrackerT _tracker;
    readonly WorkspaceT _workspace;
    readonly WorkerT _worker;
    readonly SchedulerConfig _config;
    readonly Dictionary<StringIdentifier<IssueId>, IssueExecution> _executions = new();
    readonly Dictionary<string, int> _runningCountsByState = new();
    readonly Dictionary<StringIdentifier<WorkerId>, StringIdentifier<IssueId>> _workerIndex = new();
    List<RecoveryRecord>? _pendingRecovery;
    bool _recovered;
    ulong _nextWorkerOrdinal;
    TimestampMs? _lastPollAt;
    HealthStatus _health = HealthStatus.Starting;

    public Scheduler(TrackerT tracker, WorkspaceT workspace, WorkerT worker, SchedulerConfig config)
    {
        _tracker = tracker;
        _workspace = workspace;
        _worker = worker;
        _config = config;
    }

    public SchedulerConfig Config => _config;
    public TrackerT Tracker => _tracker;
    public WorkspaceT Workspace => _workspace;
    public WorkerT Worker => _worker;
    public IReadOnlyDictionary<StringIdentifier<IssueId>, IssueExecution> Executions => _executions;

    public IssueExecution? Execution(StringIdentifier<IssueId> issueId) =>
        _executions.TryGetValue(issueId, out var e) ? e : null;

    public OrchestratorSnapshot Snapshot(TimestampMs generatedAt)
    {
        var issues = _executions.Values.Select(IssueSnapshot.From).ToList();
        issues.Sort((l, r) => string.Compare(l.Issue.Identifier.Value, r.Issue.Identifier.Value, StringComparison.Ordinal));

        ulong totalInput = 0, totalOutput = 0, totalCacheRead = 0, totalTokens = 0;
        foreach (var issue in issues)
        {
            if (issue.Conversation is { } conv)
            {
                totalInput += conv.InputTokens;
                totalOutput += conv.OutputTokens;
                totalCacheRead += conv.CacheReadTokens;
                totalTokens += conv.EffectiveTotalTokens();
            }
        }

        var daemon = DaemonSnapshot.New(
            _health, _config.PollIntervalMs, _config.MaxConcurrentAgents, _lastPollAt,
            new ComponentHealthSnapshot(HealthStatus.Unknown, null, null),
            new RuntimeUsageTotals
            {
                InputTokens = totalInput,
                OutputTokens = totalOutput,
                CacheReadTokens = totalCacheRead,
                TotalTokens = totalTokens,
                RuntimeSeconds = 0,
                EstimatedCostUsdMicros = null,
            });

        return OrchestratorSnapshot.New(generatedAt, daemon, issues);
    }

    public async Task<OrchestratorSnapshot> BootstrapAsync(TimestampMs observedAt)
    {
        if (_pendingRecovery is null)
            _pendingRecovery = await SafeRecoverWorkspaces();

        var trackerSnapshot = await LoadTrackerSnapshot();
        await BootstrapRecovery(trackerSnapshot, observedAt);
        await ReconcileTrackerState(trackerSnapshot, observedAt);

        _lastPollAt = observedAt;
        return Snapshot(observedAt);
    }

    public async Task<OrchestratorSnapshot> TickAsync(TimestampMs observedAt)
    {
        if (_pendingRecovery is null)
            _pendingRecovery = await SafeRecoverWorkspaces();

        var updates = await SafePollUpdates();
        await ApplyWorkerUpdates(updates);

        var trackerSnapshot = await LoadTrackerSnapshot();
        await BootstrapRecovery(trackerSnapshot, observedAt);
        await ReconcileTrackerState(trackerSnapshot, observedAt);
        await HandleStalls(observedAt);
        await DispatchReadyIssues(trackerSnapshot.Active, observedAt);

        _lastPollAt = observedAt;
        _health = HealthStatus.Healthy;
        return Snapshot(observedAt);
    }

    public async Task RunUntilShutdownAsync(CancellationToken shutdown)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.PollIntervalMs));
        while (!shutdown.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(shutdown);
            var now = TimestampMs.New(CurrentEpochMillis());
            try { await TickAsync(now); }
            catch (SchedulerError) { _health = HealthStatus.Degraded; }
        }
    }

    async Task<List<RecoveryRecord>> SafeRecoverWorkspaces()
    {
        try { return await _workspace.RecoverWorkspacesAsync(); }
        catch (Exception ex) { throw SchedulerError.Workspace(ex.ToString()); }
    }

    async Task<List<WorkerUpdate>> SafePollUpdates()
    {
        try { return await _worker.PollUpdatesAsync(); }
        catch (Exception ex) { throw SchedulerError.Workspace(ex.ToString()); }
    }

    async Task<TrackerSnapshot> LoadTrackerSnapshot()
    {
        List<TrackerIssue> active, terminal;
        try { active = await _tracker.CandidateIssuesAsync(); }
        catch (Exception ex) { throw SchedulerError.Tracker(ex.ToString()); }
        try { terminal = await _tracker.TerminalIssuesAsync(); }
        catch (Exception ex) { throw SchedulerError.Tracker(ex.ToString()); }

        var activeIds = active.Select(i => i.Id).ToHashSet();
        var terminalIds = terminal.Select(i => i.Id).ToHashSet();

        var lookupIds = new SortedSet<string>(
            _executions.Keys.Select(k => k.Value));
        if (_pendingRecovery is { } records)
            lookupIds.UnionWith(records.Select(r => r.Issue.Id.Value));
        lookupIds.RemoveWhere(id => activeIds.Contains(id) || terminalIds.Contains(id));

        var activeIndex = new Dictionary<string, int>();
        for (var i = 0; i < active.Count; i++)
            activeIndex[active[i].Id] = i;

        var terminalStateById = new Dictionary<string, string>();
        foreach (var issue in terminal)
            terminalStateById[issue.Id] = issue.State;

        var stateById = new Dictionary<string, TrackerIssueStateSnapshot>();
        if (lookupIds.Count > 0)
        {
            List<TrackerIssueStateSnapshot> snapshots;
            try { snapshots = await _tracker.IssueStatesByIdsAsync(lookupIds.ToList()); }
            catch (Exception ex) { throw SchedulerError.Tracker(ex.ToString()); }
            foreach (var snap in snapshots)
                stateById[snap.Id] = snap;
        }

        return new TrackerSnapshot(active, activeIndex, terminalStateById, stateById);
    }

    async Task BootstrapRecovery(TrackerSnapshot trackerSnapshot, TimestampMs observedAt)
    {
        if (_recovered) return;

        if (_pendingRecovery is null)
        {
            _recovered = true;
            return;
        }

        var records = _pendingRecovery;
        _pendingRecovery = null;

        foreach (var record in records)
        {
            var issueId = record.Issue.Id;
            if (trackerSnapshot.ActiveIssue(issueId) is { } activeIssue)
            {
                var normalized = NormalizeTrackerIssue(activeIssue, _config);
                UpsertActiveExecution(normalized, observedAt, record.Workspace);
                continue;
            }

            if (trackerSnapshot.ContainsTerminal(issueId.Value))
            {
                try { await _workspace.CleanupWorkspaceAsync(record.Workspace, true); }
                catch (Exception ex) { throw SchedulerError.Workspace(ex.ToString()); }
                continue;
            }

            var issue = record.Issue;
            if (trackerSnapshot.StateById.TryGetValue(issueId.Value, out var snap))
                issue = issue with { State = IssueStateFromName(snap.State.Name, _config) };

            var execution = new IssueExecution(issue, observedAt);
            var attachResult = execution.AttachWorkspace(record.Workspace);
            if (attachResult.IsErr) throw SchedulerError.FromStateTransition(attachResult.Error);
            var releaseResult = execution.Release(observedAt, Domain.ReleaseReason.TrackerInactive, null);
            if (releaseResult.IsErr) throw SchedulerError.FromStateTransition(releaseResult.Error);
            _executions[issue.Id] = releaseResult.Value;
        }

        _recovered = true;
    }

    async Task ReconcileTrackerState(TrackerSnapshot trackerSnapshot, TimestampMs observedAt)
    {
        foreach (var trackerIssue in trackerSnapshot.Active)
        {
            var normalized = NormalizeTrackerIssue(trackerIssue, _config);
            UpsertActiveExecution(normalized, observedAt, null);
        }

        var existingIds = _executions.Keys.ToList();
        foreach (var issueId in existingIds)
        {
            if (trackerSnapshot.ContainsActive(issueId.Value))
                continue;

            if (trackerSnapshot.TerminalStateName(issueId.Value) is { } terminalStateName)
            {
                if (!_executions.TryGetValue(issueId, out var existing)) continue;
                var normalized = existing.Issue with { State = IssueStateFromName(terminalStateName, _config) };
                await ReleaseIssue(issueId, normalized, observedAt, Domain.ReleaseReason.TrackerTerminal, true, WorkerAbortReason.TrackerTerminal);
                continue;
            }

            if (trackerSnapshot.StateById.TryGetValue(issueId.Value, out var snap))
            {
                var category = StateCategoryFromName(snap.State.Name, _config);
                if (category == IssueStateCategory.Active)
                    continue;

                var normalized = _executions.TryGetValue(issueId, out var existing)
                    ? existing.Issue with { State = IssueStateFromName(snap.State.Name, _config) }
                    : MinimalIssueFromStateSnapshot(snap, _config);

                var (reason, cleanup, abortReason) = category switch
                {
                    IssueStateCategory.Terminal => (Domain.ReleaseReason.TrackerTerminal, true, (WorkerAbortReason?)WorkerAbortReason.TrackerTerminal),
                    IssueStateCategory.NonActive => (Domain.ReleaseReason.TrackerInactive, false, (WorkerAbortReason?)WorkerAbortReason.TrackerInactive),
                    _ => (default(Domain.ReleaseReason), false, (WorkerAbortReason?)null),
                };
                await ReleaseIssue(issueId, normalized, observedAt, reason, cleanup, abortReason);
            }
        }
    }

    async Task DispatchReadyIssues(IReadOnlyList<TrackerIssue> activeIssues, TimestampMs observedAt)
    {
        var ready = Selection.FilterIssuesForDispatch(activeIssues, _config.TerminalStateSet());
        var availableCapacity = (int)_config.MaxConcurrentAgents - _workerIndex.Count;
        if (availableCapacity <= 0) return;

        var pendingLaunches = new List<(StringIdentifier<IssueId> IssueId, IssueExecution Execution, RunAttempt ClaimedRun, WorkerStartRequest Request)>();
        var plannedRunningByState = new Dictionary<string, int>();

        foreach (var trackerIssue in ready)
        {
            if (pendingLaunches.Count >= availableCapacity) break;

            var normalized = NormalizeTrackerIssue(trackerIssue, _config);
            var issueId = normalized.Id;
            var shouldDispatch = _executions.TryGetValue(issueId, out var exec) switch
            {
                true => exec.Status switch
                {
                    SchedulerStatus.Unclaimed => true,
                    SchedulerStatus.RetryQueued => exec.Retry is { } retry && retry.DueAt <= observedAt,
                    _ => false,
                },
                false => true,
            };
            if (!shouldDispatch) continue;

            var stateKey = NormalizedStateName(normalized.State.Name);

            if (StateLimitFor(_config.MaxConcurrentAgentsByState, stateKey) is { } limit)
            {
                var runningInState = RunningCountForNormalizedState(stateKey)
                    + (plannedRunningByState.TryGetValue(stateKey, out var planned) ? planned : 0);
                if (runningInState >= (int)limit) continue;
            }

            WorkspaceRecord workspace;
            try { workspace = await _workspace.EnsureWorkspaceAsync(normalized, observedAt); }
            catch (Exception ex) { throw SchedulerError.Workspace(ex.ToString()); }

            var workerId = NextWorkerId();
            var previousRetry = _executions.TryGetValue(issueId, out var prev) ? prev.Retry?.Attempt : null;
            var run = RunAttempt.New(workerId, normalized.Id, normalized.Identifier, workspace.Path, observedAt, previousRetry, _config.MaxTurns);
            var route = SchedulerRouting.DecideIssueRoute(normalized, _config);

            var execution = RemoveExecution(issueId) ?? new IssueExecution(normalized, observedAt);
            var refreshResult = execution.RefreshIssue(normalized);
            if (refreshResult.IsErr) throw SchedulerError.FromStateTransition(refreshResult.Error);
            var attachResult = execution.AttachWorkspace(workspace);
            if (attachResult.IsErr) throw SchedulerError.FromStateTransition(attachResult.Error);
            var claimResult = execution.Claim(run);
            if (claimResult.IsErr) throw SchedulerError.FromStateTransition(claimResult.Error);
            execution = claimResult.Value;
            var claimedRun = execution.CurrentRun!;

            var startRequest = new WorkerStartRequest(normalized, workspace, claimedRun, route);

            plannedRunningByState[stateKey] = (plannedRunningByState.TryGetValue(stateKey, out var p) ? p : 0) + 1;
            pendingLaunches.Add((issueId, execution, claimedRun, startRequest));
        }

        var startResults = await _worker.StartWorkersAsync(pendingLaunches.Select(x => x.Request).ToList());

        for (var i = 0; i < pendingLaunches.Count; i++)
        {
            var (issueId, execution, claimedRun, _) = pendingLaunches[i];
            var result = startResults[i];
            IssueExecution finalExecution;
            if (result.IsOk)
            {
                var launch = result.Value;
                var startResult = execution.StartRunning(observedAt, EffectiveStallTimeout(_config.StallTimeoutMs), launch.Conversation);
                if (startResult.IsErr) throw SchedulerError.FromStateTransition(startResult.Error);
                finalExecution = startResult.Value;
                var turnResult = finalExecution.RecordTurnStarted(observedAt);
                if (turnResult.IsErr) throw SchedulerError.FromStateTransition(turnResult.Error);
                _workerIndex[claimedRun.WorkerId] = issueId;
            }
            else
            {
                var detail = result.Error;
                var outcome = WorkerOutcomeRecord.FromRun(claimedRun, WorkerOutcomeKind.Failed, observedAt, "failed to start worker", detail);
                finalExecution = ResolveFinishedExecution(execution, outcome, observedAt);
            }
            InsertExecution(issueId, finalExecution);
        }
    }

    async Task ApplyWorkerUpdates(List<WorkerUpdate> updates)
    {
        foreach (var update in updates)
        {
            switch (update)
            {
                case WorkerUpdate.RuntimeEvent re:
                    if (!_workerIndex.TryGetValue(re.WorkerId, out var reIssueId)) continue;
                    if (_executions.TryGetValue(reIssueId, out var reExec))
                    {
                        var result = reExec.ObserveRuntimeEvent(re.ObservedAt, re.EventId, re.EventKind, re.Summary, re.Payload);
                        if (result.IsErr) throw SchedulerError.FromStateTransition(result.Error);
                    }
                    break;
                case WorkerUpdate.Finished fin:
                    if (!_workerIndex.Remove(fin.WorkerId, out var finIssueId)) continue;
                    var finExec = RemoveExecution(finIssueId);
                    if (finExec is null) continue;
                    var finishedAt = fin.Outcome.FinishedAt;
                    var resolved = ResolveFinishedExecution(finExec, fin.Outcome, finishedAt);
                    InsertExecution(finIssueId, resolved);
                    break;
                case WorkerUpdate.ConversationMetadataUpdate cmu:
                    if (!_workerIndex.TryGetValue(cmu.WorkerId, out var cmuIssueId)) continue;
                    if (_executions.TryGetValue(cmuIssueId, out var cmuExec))
                        cmuExec.UpdateConversation(cmu.Conversation);
                    break;
                case WorkerUpdate.TokenUsageUpdate tuu:
                    if (!_workerIndex.TryGetValue(tuu.WorkerId, out var tuuIssueId)) continue;
                    if (_executions.TryGetValue(tuuIssueId, out var tuuExec))
                        tuuExec.UpdateConversationTokenUsage(tuu.InputTokens, tuu.OutputTokens, tuu.CacheReadTokens, tuu.TotalTokens);
                    break;
            }
        }
    }

    async Task HandleStalls(TimestampMs observedAt)
    {
        if (_config.StallTimeoutMs is null) return;

        var stalled = new List<StringIdentifier<IssueId>>();
        foreach (var (issueId, execution) in _executions)
        {
            if (execution.State is SchedulerStateRunning { Stall: var stall } && stall.StalledAt <= observedAt)
                stalled.Add(issueId);
        }

        foreach (var issueId in stalled)
        {
            var execution = RemoveExecution(issueId);
            if (execution is null) continue;
            var run = execution.CurrentRun;
            if (run is null)
            {
                InsertExecution(issueId, execution);
                continue;
            }

            await AbortWorker(run.WorkerId, WorkerAbortReason.Stalled);
            var outcome = WorkerOutcomeRecord.FromRun(run, WorkerOutcomeKind.Stalled, observedAt,
                "worker exceeded the configured stall timeout", "scheduler stall timeout reached");
            var resolved = ResolveFinishedExecution(execution, outcome, observedAt);
            InsertExecution(issueId, resolved);
        }
    }

    void UpsertActiveExecution(NormalizedIssue issue, TimestampMs observedAt, WorkspaceRecord? recoveredWorkspace)
    {
        var issueId = issue.Id;
        var execution = RemoveExecution(issueId) ?? new IssueExecution(issue, observedAt);

        var wasTerminalOutcome = execution.LastWorkerOutcome is { } o &&
            (o.Outcome == WorkerOutcomeKind.Detached || o.Outcome == WorkerOutcomeKind.CancelFailed);
        if (execution.Status == SchedulerStatus.Released && !wasTerminalOutcome)
        {
            var reopenResult = execution.Reopen(observedAt);
            if (reopenResult.IsErr) throw SchedulerError.FromStateTransition(reopenResult.Error);
            execution = reopenResult.Value;
        }

        var refreshResult = execution.RefreshIssue(issue);
        if (refreshResult.IsErr) throw SchedulerError.FromStateTransition(refreshResult.Error);
        if (recoveredWorkspace is { } ws)
        {
            var attachResult = execution.AttachWorkspace(ws);
            if (attachResult.IsErr) throw SchedulerError.FromStateTransition(attachResult.Error);
        }
        InsertExecution(issueId, execution);
    }

    async Task ReleaseIssue(
        StringIdentifier<IssueId> issueId, NormalizedIssue issue, TimestampMs observedAt,
        Domain.ReleaseReason reason, bool cleanupTerminal, WorkerAbortReason? abortReason)
    {
        var execution = RemoveExecution(issueId);
        if (execution is null) return;

        var refreshResult = execution.RefreshIssue(issue);
        if (refreshResult.IsErr) throw SchedulerError.FromStateTransition(refreshResult.Error);
        execution = refreshResult.Value;

        if (execution.CurrentRun is { } run && abortReason is { } ar)
            await AbortWorker(run.WorkerId, ar);

        if (execution.Status != SchedulerStatus.Released)
        {
            var releaseResult = execution.Release(observedAt, reason, null);
            if (releaseResult.IsErr) throw SchedulerError.FromStateTransition(releaseResult.Error);
            execution = releaseResult.Value;
        }

        if (cleanupTerminal && execution.Workspace is { } workspace)
        {
            try { await _workspace.CleanupWorkspaceAsync(workspace, true); }
            catch (Exception ex) { throw SchedulerError.Workspace(ex.ToString()); }
        }
        InsertExecution(issueId, execution);
    }

    async Task AbortWorker(StringIdentifier<WorkerId> workerId, WorkerAbortReason reason)
    {
        _workerIndex.Remove(workerId);
        try { await _worker.AbortWorkerAsync(workerId, reason); }
        catch (Exception ex) { throw SchedulerError.Worker(ex.ToString()); }
    }

    IssueExecution ResolveFinishedExecution(IssueExecution execution, WorkerOutcomeRecord outcome, TimestampMs observedAt)
    {
        if (NonActiveReleaseReason(execution.Issue.State.Category) is { } reason)
        {
            var releaseResult = execution.Release(observedAt, reason, outcome);
            if (releaseResult.IsErr) throw SchedulerError.FromStateTransition(releaseResult.Error);
            return releaseResult.Value;
        }

        if (outcome.Outcome == WorkerOutcomeKind.Detached || outcome.Outcome == WorkerOutcomeKind.CancelFailed)
        {
            var releaseResult = execution.Release(observedAt, Domain.ReleaseReason.TrackerInactive, outcome);
            if (releaseResult.IsErr) throw SchedulerError.FromStateTransition(releaseResult.Error);
            return releaseResult.Value;
        }

        return QueueRetryForOutcome(execution, outcome, observedAt);
    }

    IssueExecution QueueRetryForOutcome(IssueExecution execution, WorkerOutcomeRecord outcome, TimestampMs observedAt)
    {
        var run = execution.CurrentRun ?? throw new InvalidOperationException("running execution must have a run");
        Result<RetryEntry, RetryCalculationError> retryResult;
        if (RetryReasonForOutcome(outcome.Outcome) is { } reason)
        {
            retryResult = RetryEntry.Failure(
                execution.Issue, run.Attempt, run.NormalRetryCount, observedAt,
                reason, outcome.Error ?? outcome.Summary, _config.RetryPolicy);
        }
        else
        {
            retryResult = RetryEntry.Continuation(
                execution.Issue, run.Attempt, run.NormalRetryCount, observedAt, _config.RetryPolicy);
        }
        if (retryResult.IsErr) throw SchedulerError.FromRetryCalculation(retryResult.Error);
        var queueResult = execution.QueueRetry(retryResult.Value, outcome);
        if (queueResult.IsErr) throw SchedulerError.FromStateTransition(queueResult.Error);
        return queueResult.Value;
    }

    StringIdentifier<WorkerId> NextWorkerId()
    {
        _nextWorkerOrdinal = _nextWorkerOrdinal == ulong.MaxValue ? ulong.MaxValue : _nextWorkerOrdinal + 1;
        var result = StringIdentifier<WorkerId>.New($"scheduler-worker-{_nextWorkerOrdinal}");
        if (result.IsErr) throw SchedulerError.FromIdentifier(result.Error);
        return result.Value;
    }

    IssueExecution? RemoveExecution(StringIdentifier<IssueId> issueId)
    {
        if (!_executions.Remove(issueId, out var execution)) return null;
        DecrementRunningCount(execution);
        return execution;
    }

    void InsertExecution(StringIdentifier<IssueId> issueId, IssueExecution execution)
    {
        var currentKey = RunningStateKeyForExecution(execution);
        if (_executions.ContainsKey(issueId))
        {
            var previous = _executions[issueId];
            DecrementRunningCount(previous);
        }
        _executions[issueId] = execution;
        if (currentKey is { } stateKey)
            _runningCountsByState[stateKey] = (_runningCountsByState.TryGetValue(stateKey, out var c) ? c : 0) + 1;
    }

    int RunningCountForNormalizedState(string stateKey) =>
        _runningCountsByState.TryGetValue(stateKey, out var c) ? c : 0;

    void DecrementRunningCount(IssueExecution execution)
    {
        if (RunningStateKeyForExecution(execution) is not { } stateKey) return;
        if (!_runningCountsByState.TryGetValue(stateKey, out var count)) return;
        count -= 1;
        if (count == 0) _runningCountsByState.Remove(stateKey);
        else _runningCountsByState[stateKey] = count;
    }
}

// ht: internal TrackerSnapshot — not part of public API.
sealed class TrackerSnapshot(
    List<TrackerIssue> active,
    Dictionary<string, int> activeIndex,
    Dictionary<string, string> terminalStateById,
    Dictionary<string, TrackerIssueStateSnapshot> stateById)
{
    public List<TrackerIssue> Active { get; } = active;
    public Dictionary<string, int> ActiveIndex { get; } = activeIndex;
    public Dictionary<string, string> TerminalStateById { get; } = terminalStateById;
    public Dictionary<string, TrackerIssueStateSnapshot> StateById { get; } = stateById;

    public TrackerIssue? ActiveIssue(StringIdentifier<IssueId> issueId) =>
        ActiveIndex.TryGetValue(issueId.Value, out var idx) ? Active[idx] : null;
    public bool ContainsActive(string issueId) => ActiveIndex.ContainsKey(issueId);
    public bool ContainsTerminal(string issueId) => TerminalStateById.ContainsKey(issueId);
    public string? TerminalStateName(string issueId) =>
        TerminalStateById.TryGetValue(issueId, out var name) ? name : null;
}

public static class SchedulerRouting
{
    public static HarnessRouteDecision DecideIssueRoute(NormalizedIssue issue, SchedulerConfig config)
    {
        var capability = HarnessCapabilityFor(config.Routing.Harness);
        if (!capability.Available || !capability.Actions.StartRun)
            throw SchedulerError.InvalidConfiguration(
                $"selected harness `{config.Routing.Harness}` cannot start issue execution");

        return new HarnessRouteDecision
        {
            TaskType = "issue_execution",
            HarnessKind = config.Routing.Harness,
            Model = config.Routing.Model,
            ModelProfile = config.Routing.ModelProfile,
            Reason = RoutingReason(config.Routing),
            DryRun = config.Routing.DryRun,
            UserOverride = config.Routing.HarnessFromEnv
                || config.Routing.ModelFromEnv
                || config.Routing.ModelProfileFromEnv,
        };
    }

    static string RoutingReason(RoutingConfig routing)
    {
        var parts = new List<string>();
        parts.Add(routing.HarnessFromEnv
            ? $"harness selected by {routing.HarnessEnv}"
            : "harness selected by workflow routing.harness");
        if (routing.Model is not null)
            parts.Add(routing.ModelFromEnv
                ? $"model selected by {routing.ModelEnv}"
                : "model selected by workflow routing.model");
        if (routing.ModelProfile is not null)
            parts.Add(routing.ModelProfileFromEnv
                ? $"model profile selected by {routing.ModelProfileEnv}"
                : "model profile selected by workflow routing.model_profile");
        return string.Join("; ", parts);
    }

    static HarnessCapability HarnessCapabilityFor(string kind)
    {
        var parsed = HarnessKindExtensions.Parse(kind);
        if (parsed is null)
            throw SchedulerError.InvalidConfiguration($"unknown routing harness `{kind}`");
        return parsed.Value.Capability();
    }
}

// ht: helper functions ported from scheduler.rs free functions.
internal static class SchedulerHelpers
{
    public static NormalizedIssue NormalizeTrackerIssue(TrackerIssue issue, SchedulerConfig config)
    {
        var idResult = StringIdentifier<IssueId>.New(issue.Id);
        if (idResult.IsErr) throw SchedulerError.FromIdentifier(idResult.Error);
        var identResult = StringIdentifier<IssueIdentifier>.New(issue.Identifier);
        if (identResult.IsErr) throw SchedulerError.FromIdentifier(identResult.Error);

        StringIdentifier<IssueId>? parentId = null;
        if (issue.ParentId is { } pid)
        {
            var parentResult = StringIdentifier<IssueId>.New(pid);
            if (parentResult.IsErr) throw SchedulerError.FromIdentifier(parentResult.Error);
            parentId = parentResult.Value;
        }

        var blockedBy = new List<BlockerRef>();
        foreach (var blocker in issue.BlockedBy)
        {
            var bidResult = StringIdentifier<IssueId>.New(blocker.Id);
            if (bidResult.IsErr) throw SchedulerError.FromIdentifier(bidResult.Error);
            var bidentResult = StringIdentifier<IssueIdentifier>.New(blocker.Identifier);
            if (bidentResult.IsErr) throw SchedulerError.FromIdentifier(bidentResult.Error);
            blockedBy.Add(new BlockerRef(bidResult.Value, bidentResult.Value, blocker.State.Name, null, null));
        }

        var subIssues = new List<IssueRef>();
        foreach (var child in issue.SubIssues)
        {
            var cidResult = StringIdentifier<IssueId>.New(child.Id);
            if (cidResult.IsErr) throw SchedulerError.FromIdentifier(cidResult.Error);
            var cidentResult = StringIdentifier<IssueIdentifier>.New(child.Identifier);
            if (cidentResult.IsErr) throw SchedulerError.FromIdentifier(cidentResult.Error);
            subIssues.Add(new IssueRef(cidResult.Value, cidentResult.Value, child.State));
        }

        return new NormalizedIssue(
            idResult.Value, identResult.Value, issue.Title, issue.Description, issue.Priority,
            IssueStateFromName(issue.State, config),
            null, issue.Url, issue.Labels.ToList(), parentId,
            blockedBy, subIssues,
            DatetimeToTimestamp(issue.CreatedAt), DatetimeToTimestamp(issue.UpdatedAt));
    }

    public static NormalizedIssue MinimalIssueFromStateSnapshot(TrackerIssueStateSnapshot snapshot, SchedulerConfig config)
    {
        var idResult = StringIdentifier<IssueId>.New(snapshot.Id);
        if (idResult.IsErr) throw SchedulerError.FromIdentifier(idResult.Error);
        var identResult = StringIdentifier<IssueIdentifier>.New(snapshot.Identifier);
        if (identResult.IsErr) throw SchedulerError.FromIdentifier(identResult.Error);

        return new NormalizedIssue(
            idResult.Value, identResult.Value, snapshot.Identifier, null, null,
            IssueStateFromName(snapshot.State.Name, config),
            null, null, new List<string>(), null,
            new List<BlockerRef>(), new List<IssueRef>(),
            null, DatetimeToTimestamp(snapshot.UpdatedAt));
    }

    public static IssueState IssueStateFromName(string name, SchedulerConfig config)
    {
        var stateIdResult = StringIdentifier<TrackerStateId>.New(name.ToLowerInvariant().Replace(' ', '-'));
        return new IssueState(stateIdResult.IsOk ? stateIdResult.Value : null, name, StateCategoryFromName(name, config));
    }

    public static IssueStateCategory StateCategoryFromName(string name, SchedulerConfig config) =>
        MatchesStateName(name, config.TerminalStates) ? IssueStateCategory.Terminal
        : MatchesStateName(name, config.ActiveStates) ? IssueStateCategory.Active
        : IssueStateCategory.NonActive;

    public static uint? StateLimitFor(SortedDictionary<string, uint> limits, string stateKey)
    {
        if (limits.TryGetValue(stateKey, out var direct)) return direct;
        foreach (var (configuredState, limit) in limits)
            if (NormalizedStateName(configuredState) == stateKey) return limit;
        return null;
    }

    public static Domain.ReleaseReason? NonActiveReleaseReason(IssueStateCategory category) => category switch
    {
        IssueStateCategory.Terminal => Domain.ReleaseReason.TrackerTerminal,
        IssueStateCategory.NonActive => Domain.ReleaseReason.TrackerInactive,
        _ => null,
    };

    public static RetryReason? RetryReasonForOutcome(WorkerOutcomeKind outcome) => outcome switch
    {
        WorkerOutcomeKind.Succeeded => null,
        WorkerOutcomeKind.Failed or WorkerOutcomeKind.TimedOut => RetryReason.Failure,
        WorkerOutcomeKind.Stalled => RetryReason.Stalled,
        WorkerOutcomeKind.Cancelled => RetryReason.Cancelled,
        WorkerOutcomeKind.Detached or WorkerOutcomeKind.CancelFailed => null,
        _ => null,
    };

    public static HashSet<string> NormalizedStateSet(List<string> states) =>
        states.Select(NormalizedStateName).ToHashSet();

    public static bool MatchesStateName(string name, List<string> states)
    {
        var normalized = NormalizedStateName(name);
        return states.Any(s => NormalizedStateName(s) == normalized);
    }

    public static string? RunningStateKeyForExecution(IssueExecution execution) =>
        execution.Status == SchedulerStatus.Running ? NormalizedStateName(execution.Issue.State.Name) : null;

    public static string NormalizedStateName(string name) => name.Trim().ToLowerInvariant();

    public static DurationMs EffectiveStallTimeout(ulong? stallTimeoutMs) =>
        DurationMs.New(stallTimeoutMs ?? ulong.MaxValue / 4);

    public static TimestampMs DatetimeToTimestamp(DateTimeOffset datetime)
    {
        var millis = datetime.ToUnixTimeMilliseconds();
        return TimestampMs.New(millis <= 0 ? 0 : (ulong)millis);
    }

    public static ulong CurrentEpochMillis() =>
        (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
