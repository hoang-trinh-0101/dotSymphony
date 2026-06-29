using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using OpenSymphony.Domain;
using OpenSymphony.Linear;
using OpenSymphony.OpenHands;
using OpenSymphony.Orchestrator;
using OpenSymphony.Workflow;
using OpenSymphony.Workspace;
using Domain = OpenSymphony.Domain;
using Oh = OpenSymphony.OpenHands;

namespace OpenSymphony.Cli.OrchestratorRun;

// ht: Helper for current epoch milliseconds
static class EpochHelper
{
    public static long CurrentEpochMillis() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// ht: Simplified backend adapters - TODO: Full integration with existing types
// These are minimal implementations to get the CLI compiling. Full porting requires
// deeper understanding of the existing C# type system and proper type mapping.

public sealed class RuntimeTrackerBackend : ITrackerBackend
{
    readonly ILinearClient _client;

    public RuntimeTrackerBackend(ILinearClient client)
    {
        _client = client;
    }

    public async Task<List<TrackerIssue>> CandidateIssuesAsync()
    {
        return await _client.CandidateIssues();
    }

    public async Task<List<TrackerIssue>> TerminalIssuesAsync()
    {
        return await _client.TerminalIssues();
    }

    public async Task<List<TrackerIssueStateSnapshot>> IssueStatesByIdsAsync(IReadOnlyList<string> issueIds)
    {
        return await _client.IssueStatesByIds(issueIds);
    }
}

public sealed class RuntimeWorkspaceBackend : IWorkspaceBackend
{
    readonly WorkspaceManager _manager;
    readonly HashSet<string> _activeStates;
    readonly HashSet<string> _terminalStates;

    public RuntimeWorkspaceBackend(WorkspaceManager manager, ResolvedWorkflow workflow)
    {
        _manager = manager;
        _activeStates = workflow.Config.Tracker.ActiveStates
            .Select(s => s.Trim().ToLowerInvariant())
            .ToHashSet();
        _terminalStates = workflow.Config.Tracker.TerminalStates
            .Select(s => s.Trim().ToLowerInvariant())
            .ToHashSet();
    }

    public async Task<WorkspaceRecord> EnsureWorkspaceAsync(NormalizedIssue issue, TimestampMs observedAt)
    {
        var lastSeen = issue.UpdatedAt is { } ts
            ? (DateTimeOffset?)DateTimeOffset.FromUnixTimeMilliseconds((long)ts.Value)
            : null;
        var descriptor = new IssueDescriptor(
            issue.Id.Value,
            issue.Identifier.Value,
            issue.Title,
            issue.State.Name,
            lastSeen);

        var result = await _manager.Ensure(descriptor);
        if (result.IsErr)
            throw new InvalidOperationException($"failed to ensure workspace: {result.Error.Message}");
        var ensured = result.Value;

        return new WorkspaceRecord(
            ensured.Handle.WorkspacePath,
            WorkspaceKey.New(ensured.Handle.WorkspaceKey).Value,
            ensured.Created,
            TimestampMs.New((ulong)ensured.IssueManifest.CreatedAt.ToUnixTimeMilliseconds()),
            TimestampMs.New((ulong)ensured.IssueManifest.UpdatedAt.ToUnixTimeMilliseconds()),
            ensured.IssueManifest.LastSeenTrackerRefreshAt is { } t
                ? TimestampMs.New((ulong)t.ToUnixTimeMilliseconds())
                : null);
    }

    public async Task<List<RecoveryRecord>> RecoverWorkspacesAsync()
    {
        var listResult = await _manager.ListAllWorkspaces();
        if (listResult.IsErr)
            throw new InvalidOperationException($"failed to list workspaces: {listResult.Error.Message}");

        var recoveries = new List<RecoveryRecord>();
        foreach (var (handle, manifest) in listResult.Value)
        {
            var runResult = await _manager.LoadRunManifest(handle);
            if (runResult.IsErr)
                throw new InvalidOperationException($"failed to load run manifest for {handle.WorkspaceKey}: {runResult.Error.Message}");

            var hadInFlightRun = runResult.Value is { } run && IsInFlight(run.Status);
            var issue = NormalizedIssueFromManifest(manifest);
            var workspace = new WorkspaceRecord(
                handle.WorkspacePath,
                WorkspaceKey.New(handle.WorkspaceKey).Value,
                false,
                TimestampMs.New((ulong)manifest.CreatedAt.ToUnixTimeMilliseconds()),
                TimestampMs.New((ulong)manifest.UpdatedAt.ToUnixTimeMilliseconds()),
                manifest.LastSeenTrackerRefreshAt is { } t
                    ? TimestampMs.New((ulong)t.ToUnixTimeMilliseconds())
                    : null);

            recoveries.Add(new RecoveryRecord(issue, workspace, hadInFlightRun));
        }
        return recoveries;
    }

    public Task CleanupWorkspaceAsync(WorkspaceRecord workspace, bool terminal)
    {
        if (!terminal)
            return Task.CompletedTask;
        try { Directory.Delete(workspace.Path, recursive: true); }
        catch (DirectoryNotFoundException) { }
        return Task.CompletedTask;
    }

    NormalizedIssue NormalizedIssueFromManifest(IssueManifest manifest)
    {
        var id = RequireIdentifier<IssueId>(manifest.IssueId, "issue id");
        var identifier = RequireIdentifier<IssueIdentifier>(manifest.Identifier, "issue identifier");
        var state = new IssueState(null, manifest.CurrentState, CategorizeState(manifest.CurrentState));

        return new NormalizedIssue(
            id,
            identifier,
            manifest.Title,
            null,
            null,
            state,
            null,
            null,
            new List<string>(),
            null,
            new List<BlockerRef>(),
            new List<IssueRef>(),
            TimestampMs.New((ulong)manifest.CreatedAt.ToUnixTimeMilliseconds()),
            TimestampMs.New((ulong)manifest.UpdatedAt.ToUnixTimeMilliseconds()));
    }

    Domain.IssueStateCategory CategorizeState(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (_terminalStates.Contains(normalized))
            return Domain.IssueStateCategory.Terminal;
        if (_activeStates.Contains(normalized))
            return Domain.IssueStateCategory.Active;
        return Domain.IssueStateCategory.NonActive;
    }

    static StringIdentifier<TTag> RequireIdentifier<TTag>(string value, string context)
        where TTag : IStringIdentifierTag
    {
        var result = StringIdentifier<TTag>.New(value);
        if (result.IsErr)
            throw new InvalidOperationException($"invalid {context}: {result.Error.Message}");
        return result.Value;
    }

    static bool IsInFlight(RunStatus status) =>
        status is RunStatus.Preparing or RunStatus.Prepared or RunStatus.Running;
}

public sealed class RuntimeWorkerBackend : IWorkerBackend
{
    readonly Oh.OpenHandsClient _client;
    readonly ResolvedWorkflow _workflow;
    readonly WorkspaceManager _workspaceManager;
    readonly Oh.IssueSessionRunnerConfig _runnerConfig;
    readonly Channel<WorkerUpdate> _updatesChannel = Channel.CreateUnbounded<WorkerUpdate>();
    readonly ConcurrentDictionary<string, CancellationTokenSource> _workerCts = new();

    public RuntimeWorkerBackend(Oh.OpenHandsClient client, ResolvedWorkflow workflow, WorkspaceManager workspaceManager, RunMemoryEnv? memoryEnv)
    {
        _client = client;
        _workflow = workflow;
        _workspaceManager = workspaceManager;
        _runnerConfig = Oh.IssueSessionRunnerConfig.FromWorkflow(workflow)
            .WithMemory(memoryEnv is null ? null : new Oh.MemoryWorkerAccess(memoryEnv.Endpoint, memoryEnv.Token, memoryEnv.Project, memoryEnv.ExecutionRepo));
    }

    public async Task<WorkerLaunch> StartWorkerAsync(WorkerStartRequest request)
    {
        var workerId = request.Run.WorkerId.Value;
        var cts = new CancellationTokenSource();
        _workerCts[workerId] = cts;

        var launchTcs = new TaskCompletionSource<Oh.ConversationMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new WorkerObserver(request.Run.WorkerId, _updatesChannel, launchTcs);

        var task = RunWorkerAsync(request, observer, cts.Token);
        _ = task.ContinueWith(_ =>
        {
            if (_workerCts.TryRemove(workerId, out var removed))
                removed.Dispose();
        }, TaskContinuationOptions.ExecuteSynchronously);

        var openHandsMetadata = await launchTcs.Task;
        return new WorkerLaunch(ToDomain(openHandsMetadata));
    }

    public Task AbortWorkerAsync(StringIdentifier<WorkerId> workerId, WorkerAbortReason reason)
    {
        if (_workerCts.TryGetValue(workerId.Value, out var cts))
            cts.Cancel();
        return Task.CompletedTask;
    }

    public async Task<List<WorkerUpdate>> PollUpdatesAsync()
    {
        var updates = new List<WorkerUpdate>();
        while (_updatesChannel.Reader.TryRead(out var update))
        {
            updates.Add(update);
        }
        return updates;
    }

    async Task RunWorkerAsync(WorkerStartRequest request, WorkerObserver observer, CancellationToken ct)
    {
        var workerId = request.Run.WorkerId.Value;
        Oh.IssueSessionResult? result = null;
        Exception? error = null;
        try
        {
            var issue = request.Issue;
            var descriptor = new IssueDescriptor(issue.Id.Value, issue.Identifier.Value, issue.Title, issue.State.Name, null);
            var ensureResult = await _workspaceManager.Ensure(descriptor);
            if (ensureResult.IsErr)
            {
                observer.SetLaunchFailed(new InvalidOperationException($"failed to ensure workspace: {ensureResult.Error.Message}"));
                return;
            }
            var workspace = ensureResult.Value.Handle;

            var attempt = request.Run.Attempt?.Get() ?? 1;
            var runDescriptor = new RunDescriptor($"run-{workerId}", attempt);
            var runManifestResult = await _workspaceManager.StartRun(workspace, runDescriptor);
            if (runManifestResult.IsErr)
            {
                observer.SetLaunchFailed(new InvalidOperationException($"failed to prepare workspace run: {runManifestResult.Error.Message}"));
                return;
            }
            var runManifest = runManifestResult.Value;

            var runner = new Oh.IssueSessionRunner(_client, _runnerConfig);
            var runResult = await runner.RunAsync(_workspaceManager, workspace, runManifest, issue, request.Run, _workflow, observer, null, ct);
            if (runResult.IsOk)
                result = runResult.Value;
            else
                error = runResult.Error;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (!observer.LaunchCompleted)
        {
            observer.SetLaunchFailed(error ?? new InvalidOperationException("worker task completed without launch"));
            return;
        }

        var finishedAt = TimestampMs.New((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var outcome = result?.WorkerOutcome ?? WorkerOutcomeRecord.FromRun(
            request.Run,
            WorkerOutcomeKind.Failed,
            finishedAt,
            error?.Message ?? "worker task failed",
            error?.ToString());
        await _updatesChannel.Writer.WriteAsync(new WorkerUpdate.Finished(request.Run.WorkerId, outcome), ct);
    }

    static Domain.ConversationMetadata ToDomain(Oh.ConversationMetadata src)
    {
        var conversationId = StringIdentifier<ConversationId>.New(src.ConversationId.ToString("N")).Value;
        return new Domain.ConversationMetadata(conversationId)
        {
            ServerBaseUrl = src.ServerBaseUrl,
            TransportTarget = src.TransportTarget,
            HttpAuthMode = src.HttpAuthMode,
            WebsocketAuthMode = src.WebsocketAuthMode,
            WebsocketQueryParamName = src.WebsocketQueryParamName,
            FreshConversation = src.FreshConversation,
            RuntimeContractVersion = src.RuntimeContractVersion,
            StreamState = src.StreamState,
            LastEventId = src.LastEventId,
            LastEventKind = src.LastEventKind,
            LastEventAt = src.LastEventAt,
            LastEventSummary = src.LastEventSummary,
            InputTokens = src.InputTokens,
            OutputTokens = src.OutputTokens,
            CacheReadTokens = src.CacheReadTokens,
            TotalTokens = src.TotalTokens,
        };
    }

    sealed class WorkerObserver : Oh.IIssueSessionObserver
    {
        readonly StringIdentifier<WorkerId> _workerId;
        readonly Channel<WorkerUpdate> _updates;
        readonly TaskCompletionSource<Oh.ConversationMetadata> _launchTcs;

        public bool LaunchCompleted => _launchTcs.Task.IsCompleted;

        public WorkerObserver(StringIdentifier<WorkerId> workerId, Channel<WorkerUpdate> updates, TaskCompletionSource<Oh.ConversationMetadata> launchTcs)
        {
            _workerId = workerId;
            _updates = updates;
            _launchTcs = launchTcs;
        }

        public void SetLaunchFailed(Exception error) => _launchTcs.TrySetException(error);

        public void OnLaunch(Oh.ConversationMetadata conversation)
        {
            _launchTcs.TrySetResult(conversation);
        }

        public void OnRuntimeEvent(
            TimestampMs observedAt, string? eventId, string? eventKind, string? summary, JsonElement? payload)
        {
            _updates.Writer.TryWrite(new WorkerUpdate.RuntimeEvent(_workerId, observedAt, eventId, eventKind, summary, payload));
        }

        public void OnConversationUpdate(Oh.ConversationMetadata conversation)
        {
            _updates.Writer.TryWrite(new WorkerUpdate.ConversationMetadataUpdate(_workerId, ToDomain(conversation)));
        }
    }
}

// Helper functions for building backends
public static class RunBackendBuilder
{
    public static LinearClient BuildLinearClient(ResolvedWorkflow workflow)
    {
        var tracker = workflow.Config.Tracker;
        var config = new LinearConfig(tracker.ApiKey, tracker.ProjectSlug)
        {
            BaseUrl = tracker.Endpoint,
            ActiveStates = tracker.ActiveStates,
            TerminalStates = tracker.TerminalStates
        };
        return new LinearClient(config);
    }

    public static WorkspaceManagerConfig BuildWorkspaceManagerConfig(ResolvedWorkflow workflow)
    {
        // TODO: Implement proper workspace manager config building
        return new WorkspaceManagerConfig(
            workflow.Config.Workspace.Root ?? "/tmp/workspaces",
            HookConfig.Default(),
            CleanupConfig.Default()
        );
    }

    public static (OpenHandsClient, LocalServerSupervisor?) BuildRuntimeTransport(
        RunRuntimeConfig runtime,
        LocalServerTooling? tooling,
        RunMemoryEnv? memoryEnv)
    {
        // TODO: Implement proper transport building
        var transportConfig = new TransportConfig(
            runtime.Workflow.Extensions.OpenHands.Transport.BaseUrl
        );
        var client = new OpenHandsClient(transportConfig);
        return (client, null);
    }

    public static ManagedLocalPreparation PrepareActiveConversationStore(
        RunRuntimeConfig runtime,
        RuntimeTrackerBackend tracker,
        WorkspaceManager workspaceManager)
    {
        // TODO: Implement conversation store preparation
        return new ManagedLocalPreparation(
            new ActiveConversationStorePreparation(),
            new LegacyConversationStoreMigration(),
            null
        );
    }
}

public sealed record ActiveConversationStorePreparation(
    int Moved = 0,
    int AlreadyActive = 0,
    int Missing = 0,
    int SkippedWithoutWorkspace = 0,
    int SkippedWithoutManifest = 0,
    int SkippedInvalidManifest = 0);

public sealed record LegacyConversationStoreMigration(
    int MovedToArchived = 0,
    int AlreadyArchived = 0,
    int Missing = 0,
    int SkippedNonTerminal = 0,
    int SkippedWithoutManifest = 0,
    int SkippedInvalidManifest = 0);

public sealed record ManagedLocalPreparation(
    ActiveConversationStorePreparation ActiveConversations,
    LegacyConversationStoreMigration LegacyConversations,
    LocalServerTooling? Tooling);

// Memory environment for memory server integration
public sealed record RunMemoryEnv(
    string Endpoint,
    string? Token,
    string Project,
    string ExecutionRepo);