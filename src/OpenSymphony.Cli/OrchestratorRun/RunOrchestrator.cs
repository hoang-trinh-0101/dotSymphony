using System.Collections.Immutable;
using System.Net;
using OpenSymphony.Control;
using OpenSymphony.Domain;
using OpenSymphony.Gateway;
using OpenSymphony.Orchestrator;
using OpenSymphony.Workflow;
using OpenSymphony.Workspace;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Cli.OrchestratorRun;

// ht: Port of older/crates/opensymphony-cli/src/orchestrator_run/mod.rs
//   Main run command implementation - loads workflow, creates scheduler, runs until shutdown.
//   tokio::net::TcpListener → System.Net.Sockets. tokio time → PeriodicTimer.

public sealed class RunCommandError : Exception
{
    public RunCommandError(string message) : base(message) { }
    public RunCommandError(string message, Exception inner) : base(message, inner) { }
}

public static class RunOrchestrator
{
    public static async Task<int> RunCommandAsync(
        string? configPath,
        bool dryRun,
        CancellationToken ct = default)
    {
        try
        {
            await RunOrchestratorAsync(configPath, dryRun, ct);
            return 0;
        }
        catch (RunCommandError ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex}");
            return 1;
        }
    }

    public static async Task RunOrchestratorAsync(
        string? configPath,
        bool dryRun,
        CancellationToken ct = default)
    {
        // Resolve runtime config
        var runtime = await RunConfigResolver.ResolveRuntimeConfig(configPath, dryRun, ct);

        Console.WriteLine($"Starting OpenSymphony orchestrator");
        Console.WriteLine($"  Config: {runtime.ConfigPath ?? "<none>"}");
        Console.WriteLine($"  Target repo: {runtime.TargetRepo}");
        Console.WriteLine($"  Workflow: {runtime.WorkflowPath}");
        Console.WriteLine($"  Bind: {runtime.Bind}");

        // Build backends
        var tracker = RunBackendBuilder.BuildLinearClient(runtime.Workflow);
        var trackerBackend = new RuntimeTrackerBackend(tracker);

        var workspaceConfig = RunBackendBuilder.BuildWorkspaceManagerConfig(runtime.Workflow);
        var workspaceManager = new WorkspaceManager(workspaceConfig);
        var workspaceBackend = new RuntimeWorkspaceBackend(workspaceManager, runtime.Workflow);

        // Prepare conversation store
        var preparation = RunBackendBuilder.PrepareActiveConversationStore(
            runtime, trackerBackend, workspaceManager);

        if (preparation.LegacyConversations.MovedToArchived > 0)
        {
            Console.WriteLine($"  Migrated {preparation.LegacyConversations.MovedToArchived} terminal conversations to archived store");
        }

        if (preparation.ActiveConversations.Moved > 0)
        {
            Console.WriteLine($"  Prepared {preparation.ActiveConversations.Moved} active conversations");
        }

        // Build transport and worker backend
        var memoryEnv = runtime.Memory.Server is { } ms
            ? new RunMemoryEnv(
                ms.Bind.ToString(),
                ms.Token,
                runtime.Workflow.Config.Tracker.ProjectSlug,
                runtime.TargetRepo)
            : null;

        var (client, _) = RunBackendBuilder.BuildRuntimeTransport(
            runtime, preparation.Tooling, memoryEnv);

        // TODO: Probe OpenHands - implement OpenApiProbeAsync or equivalent
        // await client.OpenApiProbeAsync();

        var workerBackend = new RuntimeWorkerBackend();

        // Create scheduler config
        var schedulerConfig = SchedulerConfig.FromWorkflow(runtime.Workflow);

        // Create scheduler
        var scheduler = new Scheduler<RuntimeTrackerBackend, RuntimeWorkspaceBackend, RuntimeWorkerBackend>(
            trackerBackend,
            workspaceBackend,
            workerBackend,
            schedulerConfig);

        // Bootstrap scheduler
        var now = TimestampMs.New((ulong)EpochHelper.CurrentEpochMillis());
        var bootstrapSnapshot = await scheduler.BootstrapAsync(now);
        Console.WriteLine($"  Bootstrap: {bootstrapSnapshot.Issues.Count} issues loaded");

        // Start control plane server
        // TODO: Implement proper Control.SnapshotStore initialization
        // For now, create a minimal snapshot
        var initialSnapshot = new ControlPlaneDaemonSnapshot(
            DateTimeOffset.UtcNow,
            new ControlPlaneDaemonStatus(ControlPlaneDaemonState.Starting, DateTimeOffset.UtcNow, runtime.TargetRepo, "initializing"),
            new ControlPlaneAgentServerStatus(false, "", 0, "not started"),
            ControlPlaneMemoryServerStatus.Default,
            new ControlPlaneMetricsSnapshot(0, 0, 0, 0, 0, 0, 0),
            new List<ControlPlaneIssueSnapshot>(),
            new List<ControlPlaneRecentEvent>()
        );
        var snapshotStore = new SnapshotStore(initialSnapshot);
        var gatewayServer = await StartControlPlaneServerAsync(
            runtime.Bind, scheduler, snapshotStore, runtime.Workflow, ct);

        Console.WriteLine($"  Control plane listening on {runtime.Bind}");

        // Run scheduler loop
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(schedulerConfig.PollIntervalMs));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await timer.WaitForNextTickAsync(ct);

                var tickNow = TimestampMs.New((ulong)EpochHelper.CurrentEpochMillis());
                var snapshot = await scheduler.TickAsync(tickNow);

                // Update snapshot store
                var terminalStates = schedulerConfig.TerminalStateSet();
                var agentServerStatus = new ControlPlaneAgentServerStatus(
                    true,
                    runtime.Workflow.Extensions.OpenHands.Transport.BaseUrl,
                    0,
                    "ready"
                );

                var memoryServerStatus = runtime.Memory.Server is not null
                    ? new ControlPlaneMemoryServerStatus(
                        true,
                        true,
                        memoryEnv?.Endpoint,
                        "listening")
                    : ControlPlaneMemoryServerStatus.Default;

                var daemonSnapshot = RunSnapshotMapper.MapSnapshot(
                    snapshot,
                    runtime.TargetRepo,
                    terminalStates,
                    agentServerStatus,
                    memoryServerStatus,
                    ImmutableArray<ControlPlaneRecentEvent>.Empty);

                snapshotStore.Publish(daemonSnapshot);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            Console.WriteLine("Shutting down orchestrator");
            // TODO: Implement proper GatewayServer shutdown
            // await gatewayServer.StopAsync(ct);
        }
    }

    static async Task<GatewayServer> StartControlPlaneServerAsync(
        IPEndPoint bind,
        Scheduler<RuntimeTrackerBackend, RuntimeWorkspaceBackend, RuntimeWorkerBackend> scheduler,
        SnapshotStore snapshotStore,
        ResolvedWorkflow workflow,
        CancellationToken ct)
    {
        // ht: Simplified control plane server startup
        // Full implementation would:
        // - Create GatewayServer with proper configuration
        // - Wire up snapshot streaming
        // - Handle SSE events
        // TODO: Implement proper GatewayState initialization with correct parameters
        var gatewayState = GatewayState.Create(snapshotStore);

        var server = new GatewayServer(snapshotStore);
        // TODO: Implement proper server startup
        // await server.StartAsync(ct);
        return server;
    }
}