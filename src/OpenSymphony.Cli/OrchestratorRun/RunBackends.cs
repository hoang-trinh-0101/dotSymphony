using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using OpenSymphony.Domain;
using OpenSymphony.Linear;
using OpenSymphony.OpenHands;
using OpenSymphony.Orchestrator;
using OpenSymphony.Workflow;
using OpenSymphony.Workspace;

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
    readonly LinearClient _client;

    public RuntimeTrackerBackend(LinearClient client)
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
    readonly ResolvedWorkflow _workflow;

    public RuntimeWorkspaceBackend(WorkspaceManager manager, ResolvedWorkflow workflow)
    {
        _manager = manager;
        _workflow = workflow;
    }

    public async Task<WorkspaceRecord> EnsureWorkspaceAsync(NormalizedIssue issue, TimestampMs observedAt)
    {
        // TODO: Implement proper workspace creation using WorkspaceManager
        // For now, return a minimal WorkspaceRecord
        var workspaceKeyResult = WorkspaceKey.New(issue.Identifier.Value);
        if (workspaceKeyResult.IsErr)
        {
            throw new Exception($"Invalid workspace key: {workspaceKeyResult.Error.Message}");
        }
        return new WorkspaceRecord(
            issue.Identifier.Value,
            workspaceKeyResult.Value,
            true,
            observedAt,
            null,
            null
        );
    }

    public async Task<List<RecoveryRecord>> RecoverWorkspacesAsync()
    {
        // TODO: Implement workspace recovery
        return new List<RecoveryRecord>();
    }

    public async Task CleanupWorkspaceAsync(WorkspaceRecord workspace, bool terminal)
    {
        // TODO: Implement workspace cleanup
        await Task.CompletedTask;
    }
}

public sealed class RuntimeWorkerBackend : IWorkerBackend
{
    readonly Channel<WorkerUpdate> _updatesChannel = Channel.CreateUnbounded<WorkerUpdate>();

    public RuntimeWorkerBackend()
    {
        // TODO: Initialize with proper OpenHands client and workflow
    }

    public async Task<WorkerLaunch> StartWorkerAsync(WorkerStartRequest request)
    {
        // TODO: Implement proper worker launch with OpenHands client
        // For now, return a minimal WorkerLaunch
        var conversationIdResult = StringIdentifier<ConversationId>.New(Guid.NewGuid().ToString("N"));
        if (conversationIdResult.IsErr)
        {
            throw new Exception($"Invalid conversation ID: {conversationIdResult.Error.Message}");
        }
        var metadata = new Domain.ConversationMetadata(conversationIdResult.Value)
        {
            ServerBaseUrl = "http://localhost:8000",
            FreshConversation = true
        };

        return new WorkerLaunch(metadata);
    }

    public Task AbortWorkerAsync(StringIdentifier<WorkerId> workerId, WorkerAbortReason reason)
    {
        // TODO: Implement worker abortion
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