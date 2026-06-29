using System.Text.Json;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;
using OpenSymphony.Workflow;
using static OpenSymphony.Orchestrator.SchedulerHelpers;

namespace OpenSymphony.Orchestrator;

// ht: Port of older/crates/opensymphony-orchestrator/src/scheduler.rs types.
//   thiserror enum → Exception subclass with enum field. tokio traits → C# interfaces.

public enum SchedulerErrorKind
{
    InvalidConfiguration,
    Tracker,
    Workspace,
    Worker,
    StateTransition,
    RetryCalculation,
    Identifier,
}

public sealed class SchedulerError : Exception
{
    public SchedulerErrorKind Kind { get; }
    public new string Message { get; }

    SchedulerError(SchedulerErrorKind kind, string message) : base(message)
    {
        Kind = kind;
        Message = message;
    }

    public static SchedulerError InvalidConfiguration(string detail) =>
        new(SchedulerErrorKind.InvalidConfiguration, $"invalid scheduler configuration: {detail}");
    public static SchedulerError Tracker(string detail) =>
        new(SchedulerErrorKind.Tracker, $"tracker backend failed: {detail}");
    public static SchedulerError Workspace(string detail) =>
        new(SchedulerErrorKind.Workspace, $"workspace backend failed: {detail}");
    public static SchedulerError Worker(string detail) =>
        new(SchedulerErrorKind.Worker, $"worker backend failed: {detail}");
    public static SchedulerError FromStateTransition(StateTransitionError error) =>
        new(SchedulerErrorKind.StateTransition, error.ToString());
    public static SchedulerError FromRetryCalculation(RetryCalculationError error) =>
        new(SchedulerErrorKind.RetryCalculation, error.ToString());
    public static SchedulerError FromIdentifier(IdentifierError error) =>
        new(SchedulerErrorKind.Identifier, error.Message);

    public override string ToString() => Message;
}

public sealed class SchedulerConfig
{
    public ulong PollIntervalMs { get; set; }
    public uint MaxConcurrentAgents { get; set; }
    public uint MaxTurns { get; set; }
    public SortedDictionary<string, uint> MaxConcurrentAgentsByState { get; set; } = new();
    public RetryPolicy RetryPolicy { get; set; }
    public ulong? StallTimeoutMs { get; set; }
    public List<string> ActiveStates { get; set; } = new();
    public List<string> TerminalStates { get; set; } = new();
    public RoutingConfig Routing { get; set; } = null!;

    public static SchedulerConfig FromWorkflow(ResolvedWorkflow workflow)
    {
        if (workflow.Config.Agent.MaxConcurrentAgents > uint.MaxValue)
            throw SchedulerError.InvalidConfiguration(
                $"workflow max_concurrent_agents {workflow.Config.Agent.MaxConcurrentAgents} exceeds uint.MaxValue ({uint.MaxValue})");
        if (workflow.Config.Agent.MaxTurns > uint.MaxValue)
            throw SchedulerError.InvalidConfiguration(
                $"workflow max_turns {workflow.Config.Agent.MaxTurns} exceeds uint.MaxValue ({uint.MaxValue})");

        var byState = new SortedDictionary<string, uint>();
        foreach (var (state, limit) in workflow.Config.Agent.MaxConcurrentAgentsByState)
        {
            if (limit > uint.MaxValue)
                throw SchedulerError.InvalidConfiguration(
                    $"workflow max_concurrent_agents_by_state[{state}] {limit} exceeds uint.MaxValue ({uint.MaxValue})");
            byState[NormalizedStateName(state)] = (uint)limit;
        }

        return new SchedulerConfig
        {
            PollIntervalMs = workflow.Config.Polling.IntervalMs,
            MaxConcurrentAgents = (uint)workflow.Config.Agent.MaxConcurrentAgents,
            MaxTurns = (uint)workflow.Config.Agent.MaxTurns,
            MaxConcurrentAgentsByState = byState,
            RetryPolicy = new RetryPolicy(
                RetryPolicy.Default.ContinuationDelayMs,
                RetryPolicy.Default.FailureBaseDelayMs,
                DurationMs.New(workflow.Config.Agent.MaxRetryBackoffMs)),
            StallTimeoutMs = workflow.Config.Agent.StallTimeoutMs,
            ActiveStates = workflow.Config.Tracker.ActiveStates.ToList(),
            TerminalStates = workflow.Config.Tracker.TerminalStates.ToList(),
            Routing = workflow.Config.Routing,
        };
    }

    public HashSet<string> TerminalStateSet() => NormalizedStateSet(TerminalStates);
}

public sealed record RecoveryRecord(NormalizedIssue Issue, WorkspaceRecord Workspace, bool HadInFlightRun);

public sealed record WorkerStartRequest(NormalizedIssue Issue, WorkspaceRecord Workspace, RunAttempt Run, HarnessRouteDecision Route);

public sealed class HarnessRouteDecision
{
    public string TaskType { get; set; } = "";
    public string HarnessKind { get; set; } = "";
    public string? Model { get; set; }
    public string? ModelProfile { get; set; }
    public string Reason { get; set; } = "";
    public bool DryRun { get; set; }
    public bool UserOverride { get; set; }

    public string Summary()
    {
        var profile = ModelProfile ?? "<default model profile>";
        var model = Model ?? "<harness default model>";
        var mode = DryRun ? "dry-run " : "";
        return $"{mode}selected harness `{HarnessKind}` with model `{model}` and profile `{profile}`: {Reason}";
    }
}

public sealed record WorkerLaunch(ConversationMetadata Conversation);

public abstract class WorkerUpdate
{
    public StringIdentifier<WorkerId> WorkerId { get; }

    protected WorkerUpdate(StringIdentifier<WorkerId> workerId) => WorkerId = workerId;

    public sealed class RuntimeEvent : WorkerUpdate
    {
        public TimestampMs ObservedAt { get; }
        public string? EventId { get; }
        public string? EventKind { get; }
        public string? Summary { get; }
        public JsonElement? Payload { get; }
        public RuntimeEvent(StringIdentifier<WorkerId> workerId, TimestampMs observedAt,
            string? eventId, string? eventKind, string? summary, JsonElement? payload)
            : base(workerId)
        { ObservedAt = observedAt; EventId = eventId; EventKind = eventKind; Summary = summary; Payload = payload; }
    }

    public sealed class ConversationMetadataUpdate : WorkerUpdate
    {
        public ConversationMetadata Conversation { get; }
        public ConversationMetadataUpdate(StringIdentifier<WorkerId> workerId, ConversationMetadata conversation)
            : base(workerId) => Conversation = conversation;
    }

    public sealed class TokenUsageUpdate : WorkerUpdate
    {
        public ulong InputTokens { get; }
        public ulong OutputTokens { get; }
        public ulong CacheReadTokens { get; }
        public ulong TotalTokens { get; }
        public TokenUsageUpdate(StringIdentifier<WorkerId> workerId, ulong input, ulong output, ulong cacheRead, ulong total)
            : base(workerId)
        { InputTokens = input; OutputTokens = output; CacheReadTokens = cacheRead; TotalTokens = total; }
    }

    public sealed class Finished : WorkerUpdate
    {
        public WorkerOutcomeRecord Outcome { get; }
        public Finished(StringIdentifier<WorkerId> workerId, WorkerOutcomeRecord outcome)
            : base(workerId) => Outcome = outcome;
    }
}

public enum WorkerAbortReason
{
    TrackerInactive,
    TrackerTerminal,
    Stalled,
}

// ht: async trait → C# interface with Task-returning methods. Error type is string (Display).
public interface ITrackerBackend
{
    Task<List<TrackerIssue>> CandidateIssuesAsync();
    Task<List<TrackerIssue>> TerminalIssuesAsync();
    Task<List<TrackerIssueStateSnapshot>> IssueStatesByIdsAsync(IReadOnlyList<string> issueIds);
}

public interface IWorkspaceBackend
{
    Task<WorkspaceRecord> EnsureWorkspaceAsync(NormalizedIssue issue, TimestampMs observedAt);
    Task<List<RecoveryRecord>> RecoverWorkspacesAsync();
    Task CleanupWorkspaceAsync(WorkspaceRecord workspace, bool terminal);
}

public interface IWorkerBackend
{
    Task<WorkerLaunch> StartWorkerAsync(WorkerStartRequest request);
    async Task<List<Result<WorkerLaunch, string>>> StartWorkersAsync(IReadOnlyList<WorkerStartRequest> requests)
    {
        var launches = new List<Result<WorkerLaunch, string>>(requests.Count);
        foreach (var request in requests)
        {
            try { launches.Add(Result<WorkerLaunch, string>.Ok(await StartWorkerAsync(request))); }
            catch (Exception ex) { launches.Add(Result<WorkerLaunch, string>.Err(ex.ToString())); }
        }
        return launches;
    }
    Task<List<WorkerUpdate>> PollUpdatesAsync();
    Task AbortWorkerAsync(StringIdentifier<WorkerId> workerId, WorkerAbortReason reason);
}
