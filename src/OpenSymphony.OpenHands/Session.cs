using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;
using OpenSymphony.Workflow;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands session.rs — public types only.
//   The full 162KB runner is not ported; only the data types and config needed by downstream consumers.

public static class SessionConstants
{
    public const string RUNTIME_CONTRACT_VERSION = "openhands-sdk-agent-server-v1";
}

public enum IssueSessionReusePolicyKind
{
    PerIssue,
    FreshEachRun,
    Unsupported,
}

public sealed class IssueSessionReusePolicy
{
    public IssueSessionReusePolicyKind Kind { get; }
    public string Value { get; }

    private IssueSessionReusePolicy(IssueSessionReusePolicyKind kind, string value)
    {
        Kind = kind; Value = value;
    }

    public static IssueSessionReusePolicy Parse(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "per_issue" => new(IssueSessionReusePolicyKind.PerIssue, "per_issue"),
            "fresh_each_run" => new(IssueSessionReusePolicyKind.FreshEachRun, "fresh_each_run"),
            _ => new(IssueSessionReusePolicyKind.Unsupported, value),
        };
    }

    public string AsStr() => Value;
}

public sealed record IssueSessionRunnerConfig
{
    public IssueSessionReusePolicy ReusePolicy { get; init; } = IssueSessionReusePolicy.Parse("per_issue");
    public RuntimeStreamConfig RuntimeStream { get; init; } = new();
    public TimeSpan TerminalWaitTimeout { get; init; } = TimeSpan.FromSeconds(300);
    public TimeSpan? TotalRuntimeCap { get; init; }
    public TimeSpan FinishedDrainTimeout { get; init; } = TimeSpan.FromMilliseconds(100);
    public MemoryWorkerAccess? Memory { get; init; }

    public static IssueSessionRunnerConfig FromWorkflow(ResolvedWorkflow workflow)
    {
        var ws = workflow.Extensions.OpenHands.Websocket;
        var conversation = workflow.Extensions.OpenHands.Conversation;
        return new()
        {
            ReusePolicy = IssueSessionReusePolicy.Parse(conversation.ReusePolicy),
            RuntimeStream = new RuntimeStreamConfig
            {
                ReadinessTimeout = TimeSpan.FromMilliseconds(ws.ReadyTimeoutMs),
                ReconnectInitialBackoff = TimeSpan.FromMilliseconds(ws.ReconnectInitialMs),
                ReconnectMaxBackoff = TimeSpan.FromMilliseconds(ws.ReconnectMaxMs),
            },
            TerminalWaitTimeout = TimeSpan.FromMilliseconds(300_000),
        };
    }

    public IssueSessionRunnerConfig WithMemory(MemoryWorkerAccess? memory) => this with { Memory = memory };
}

public sealed record MemoryWorkerAccess(
    string Endpoint,
    string? Token,
    string? Project,
    string? ExecutionRepo);

public sealed record WorkpadComment(string Id, string Body, DateTimeOffset UpdatedAt);

public interface IWorkpadCommentSource
{
    Task<Result<WorkpadComment?, string>> FetchWorkpadCommentAsync(string issueId);
}

public interface IIssueSessionObserver
{
    void OnLaunch(ConversationMetadata conversation) { }
    void OnRuntimeEvent(
        TimestampMs observedAt, string? eventId, string? eventKind,
        string? summary, JsonElement? payload) { }
    void OnConversationUpdate(ConversationMetadata conversation) { }
}

public sealed class NullIssueSessionObserver : IIssueSessionObserver { }

public enum IssueSessionPromptKind
{
    Full,
    Continuation,
}

public static class IssueSessionPromptKindExtensions
{
    public static string AsStr(this IssueSessionPromptKind kind) => kind switch
    {
        IssueSessionPromptKind.Full => "full",
        IssueSessionPromptKind.Continuation => "continuation",
        _ => kind.ToString(),
    };
}

public sealed record ConversationLaunchProfile
{
    public string WorkspaceKind { get; init; } = "LocalWorkspace";
    public string ConfirmationPolicyKind { get; init; } = "NeverConfirm";
    public string AgentKind { get; init; } = "Agent";
    public string LlmModel { get; init; } = "";
    public string LlmCredentialMode { get; init; } = "api_key";
    public string? LlmApiKeyEnv { get; init; }
    public string? LlmBaseUrlEnv { get; init; }
    public ConversationLaunchSubscriptionProfile? LlmSubscription { get; init; }
    public ConversationLaunchCondenserProfile? Condenser { get; init; }
    public List<ToolConfig>? AgentTools { get; init; }
    public List<string>? AgentIncludeDefaultTools { get; init; }
    public uint MaxIterations { get; init; } = 500;
    public bool StuckDetection { get; init; } = true;
    public string? LlmApiKeyFingerprint { get; init; }

    public string? ApiKeyFingerprint(IEnvironment env)
    {
        string? apiKey = LlmCredentialMode == "openai_subscription"
            ? LlmSubscription is { } sub ? env.Get(sub.AccessTokenEnv) : null
            : LlmApiKeyEnv is { } envName ? env.Get(envName) : env.Get("LLM_API_KEY");

        if (apiKey is null or "") return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static Result<ConversationLaunchProfile, string> FromWorkflow(ResolvedWorkflow workflow)
    {
        var conversation = workflow.Extensions.OpenHands.Conversation;
        var maxIterations = conversation.MaxIterations > uint.MaxValue
            ? $"workflow max_iterations {conversation.MaxIterations} exceeds uint.MaxValue"
            : null;
        if (maxIterations is not null)
            return Result<ConversationLaunchProfile, string>.Err(maxIterations);

        var llmModel = conversation.Agent.Llm?.Model;
        if (string.IsNullOrEmpty(llmModel))
            return Result<ConversationLaunchProfile, string>.Err(
                "workflow openhands.conversation.agent.llm.model is required");

        var subscription = conversation.Agent.Llm?.Subscription;
        var sub = subscription is not null
            ? new ConversationLaunchSubscriptionProfile(
                subscription.Vendor, subscription.AccessTokenEnv,
                subscription.AccountIdEnv, subscription.AuthDirectoryEnv,
                subscription.AuthMethod, subscription.OpenBrowser, subscription.ForceLogin)
            : null;

        var condenser = conversation.Agent.Condenser is { } c
            ? new ConversationLaunchCondenserProfile(c.MaxSize, c.KeepFirst)
            : null;

        var tools = conversation.Agent.Tools?.Select(t => new ToolConfig(t.Name, new Dictionary<string, JsonElement>(t.Params))).ToList();
        var includeDefaults = conversation.Agent.IncludeDefaultTools;

        return Result<ConversationLaunchProfile, string>.Ok(new ConversationLaunchProfile
        {
            ConfirmationPolicyKind = conversation.ConfirmationPolicy.Kind,
            AgentKind = conversation.Agent.Kind,
            LlmModel = llmModel!,
            LlmCredentialMode = conversation.Agent.Llm?.CredentialMode ?? "api_key",
            LlmApiKeyEnv = conversation.Agent.Llm?.ApiKeyEnv,
            LlmBaseUrlEnv = conversation.Agent.Llm?.BaseUrlEnv,
            LlmSubscription = sub,
            Condenser = condenser,
            AgentTools = tools,
            AgentIncludeDefaultTools = includeDefaults,
            MaxIterations = (uint)conversation.MaxIterations,
            StuckDetection = true,
        });
    }
}

public sealed record ConversationLaunchSubscriptionProfile(
    string Vendor,
    string AccessTokenEnv,
    string? AccountIdEnv,
    string? AuthDirectoryEnv,
    string AuthMethod,
    bool OpenBrowser,
    bool ForceLogin);

public sealed record ConversationLaunchCondenserProfile(ulong MaxSize, ulong KeepFirst);

public sealed record LlmConfigFingerprint
{
    public string? ApiKeyHash { get; init; }
    public string? BaseUrlHash { get; init; }
    public string Model { get; init; } = "";

    public static LlmConfigFingerprint FromLlmConfig(LlmConfig llm) => new()
    {
        ApiKeyHash = null,
        BaseUrlHash = null,
        Model = llm.Model,
    };
}

public sealed class IssueConversationManifest
{
    public StringIdentifier<IssueId>? IssueId { get; init; }
    public StringIdentifier<IssueIdentifier>? Identifier { get; init; }
    public StringIdentifier<ConversationId>? ConversationId { get; init; }
    public string ReusePolicy { get; init; } = "per_issue";
    public string? ServerBaseUrl { get; set; }
    public string? TransportTarget { get; set; }
    public string? HttpAuthMode { get; set; }
    public string? WebsocketAuthMode { get; set; }
    public string? WebsocketQueryParamName { get; set; }
    public string PersistenceDir { get; init; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastAttachedAt { get; set; }
    public ConversationLaunchProfile? LaunchProfile { get; set; }
    public LlmConfigFingerprint? LlmConfigFingerprint { get; set; }
    public bool FreshConversation { get; set; } = true;
    public bool WorkflowPromptSeeded { get; set; }
    public string? ResetReason { get; set; }
    public string? RuntimeContractVersion { get; set; } = SessionConstants.RUNTIME_CONTRACT_VERSION;
    public IssueSessionPromptKind? LastPromptKind { get; set; }
    public DateTimeOffset? LastPromptAt { get; set; }
    public string? LastPromptPath { get; set; }
    public string? LastExecutionStatus { get; set; }
    public string? LastEventId { get; set; }
    public string? LastEventKind { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public string? LastEventSummary { get; set; }
    public ulong InputTokens { get; set; }
    public ulong OutputTokens { get; set; }
    public ulong CacheReadTokens { get; set; }
    public DateTimeOffset? LastTokenAccumulationAt { get; set; }

    public IssueSessionPromptKind PromptKind() =>
        WorkflowPromptSeeded ? IssueSessionPromptKind.Continuation : IssueSessionPromptKind.Full;

    public void RecordPrompt(IssueSessionPromptKind kind, string path, DateTimeOffset at)
    {
        LastPromptKind = kind;
        LastPromptAt = at;
        LastPromptPath = path;
        UpdatedAt = at;
    }
}

public sealed class IssueSessionContext
{
    public string RunId { get; init; } = "";
    public StringIdentifier<IssueId>? IssueId { get; init; }
    public StringIdentifier<IssueIdentifier>? Identifier { get; init; }
    public StringIdentifier<WorkerId>? WorkerId { get; init; }
    public uint? Attempt { get; init; }
    public uint NormalRetryCount { get; init; }
    public uint TurnCount { get; init; }
    public uint MaxTurns { get; init; }
    public IssueSessionPromptKind PromptKind { get; init; }
    public string? PromptPath { get; init; }
    public StringIdentifier<ConversationId>? ConversationId { get; init; }
    public string ReusePolicy { get; init; } = "per_issue";
    public bool FreshConversation { get; init; }
    public bool WorkflowPromptSeeded { get; init; }
    public string? ServerBaseUrl { get; init; }
    public string? TransportTarget { get; init; }
    public string? HttpAuthMode { get; init; }
    public string? WebsocketAuthMode { get; init; }
    public string? WebsocketQueryParamName { get; init; }
    public string PersistenceDir { get; init; } = "";
    public string? LastExecutionStatus { get; init; }
    public string? LastEventId { get; init; }
    public string? LastEventKind { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public string? LastEventSummary { get; init; }
    public object? WorkerOutcome { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record IssueSessionResult
{
    public IssueSessionPromptKind PromptKind { get; init; }
    public ConversationMetadata? Conversation { get; init; }
    public object? WorkerOutcome { get; init; }
    public string RunStatus { get; init; } = "";
}

public sealed class IssueSessionError : Exception
{
    public IssueSessionError(string message) : base(message) { }
}

public sealed record RehydrationResult
{
    public bool Reused { get; init; }
    public StringIdentifier<ConversationId>? ConversationId { get; init; }
    public string? ResetReason { get; init; }
}

public sealed record RehydrationOptions
{
    public bool ForceFresh { get; init; }
    public string? ResetReason { get; init; }
}

// ht: ConversationMetadata — lightweight metadata used by observer callbacks.
public sealed class ConversationMetadata
{
    public Guid ConversationId { get; init; }
    public string ExecutionStatus { get; set; } = "";
    public ulong InputTokens { get; set; }
    public ulong OutputTokens { get; set; }
    public ulong CacheReadTokens { get; set; }
}

// ht: IssueSessionRunner — skeleton. Full runner logic (162KB) not ported.
//   Downstream consumers wire this up with OpenHandsClient + RuntimeMirror.
public sealed class IssueSessionRunner
{
    private readonly OpenHandsClient _client;
    private readonly IssueSessionRunnerConfig _config;

    public IssueSessionRunner(OpenHandsClient client, IssueSessionRunnerConfig config)
    {
        _client = client;
        _config = config;
    }

    public OpenHandsClient Client => _client;
    public IssueSessionRunnerConfig Config => _config;
}
