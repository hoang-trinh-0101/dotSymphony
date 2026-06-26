using System.Collections;
using System.Text.Json;
using OpenSymphony.Domain;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Workflow;

public static class WorkflowConstants
{
    public const string DEFAULT_PROMPT_TEMPLATE = "You are working on an issue from Linear.";
    public const string DEFAULT_LINEAR_ENDPOINT = "https://api.linear.app/graphql";
    public const ulong DEFAULT_POLL_INTERVAL_MS = 30_000;
    public const string DEFAULT_WORKSPACE_ROOT = "/symphony_workspaces";
    public const ulong DEFAULT_HOOK_TIMEOUT_MS = 60_000;
    public const ulong DEFAULT_MAX_CONCURRENT_AGENTS = 10;
    public const ulong DEFAULT_MAX_TURNS = 20;
    public const ulong DEFAULT_MAX_RETRY_BACKOFF_MS = 300_000;
    public const ulong DEFAULT_STALL_TIMEOUT_MS = 300_000;
    public const string DEFAULT_OPENHANDS_BASE_URL = "http://127.0.0.1:8000";
    public const ulong DEFAULT_OPENHANDS_STARTUP_TIMEOUT_MS = 180_000;
    public const string DEFAULT_OPENHANDS_READINESS_PROBE_PATH = "/openapi.json";
    public const string DEFAULT_OPENHANDS_PERSISTENCE_DIR = ".opensymphony/openhands";
    public const ulong DEFAULT_OPENHANDS_MAX_ITERATIONS = 500;
    public const string DEFAULT_OPENHANDS_CONFIRMATION_POLICY_KIND = "NeverConfirm";
    public const string DEFAULT_OPENHANDS_AGENT_KIND = "Agent";
    public static readonly string[] DEFAULT_OPENHANDS_AGENT_TOOLS = ["terminal", "file_editor"];
    public const ulong DEFAULT_OPENHANDS_READY_TIMEOUT_MS = 30_000;
    public const ulong DEFAULT_OPENHANDS_RECONNECT_INITIAL_MS = 1_000;
    public const ulong DEFAULT_OPENHANDS_RECONNECT_MAX_MS = 30_000;
    public const string DEFAULT_OPENHANDS_AUTH_MODE = "auto";
    public const string DEFAULT_OPENHANDS_QUERY_PARAM_NAME = "session_api_key";
    public const string DEFAULT_OPENHANDS_LLM_MODEL = "openai/gpt-5.4";
    public const string DEFAULT_OPENHANDS_LLM_CREDENTIAL_MODE = "api_key";
    public const string DEFAULT_ROUTING_HARNESS = "openhands_agent_server";
    public const string DEFAULT_ROUTING_HARNESS_ENV = "OPENSYMPHONY_HARNESS";
    public const string DEFAULT_ROUTING_MODEL_ENV = "OPENSYMPHONY_MODEL";
    public const string DEFAULT_ROUTING_MODEL_PROFILE_ENV = "OPENSYMPHONY_MODEL_PROFILE";
    public const string OPENHANDS_LLM_CREDENTIAL_MODE_API_KEY = "api_key";
    public const string OPENHANDS_LLM_CREDENTIAL_MODE_OPENAI_SUBSCRIPTION = "openai_subscription";
    public const ulong DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE = 240;
    public const ulong DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST = 2;
}

// ht: IntegerLike — accepts YAML int or string. Custom INodeDeserializer handles both.
public sealed class IntegerLike
{
    public long? Integer { get; }
    public string? StringValue { get; }

    private IntegerLike(long? integer, string? stringValue)
    {
        Integer = integer;
        StringValue = stringValue;
    }

    public static IntegerLike FromInteger(long value) => new(value, null);
    public static IntegerLike FromString(string value) => new(null, value);

    public override string ToString() => Integer?.ToString() ?? StringValue ?? "";
}

// ht: custom YAML deserializer for IntegerLike — accepts scalar int or string.
// ht: custom YAML deserializer for JsonElement — reads any node as a raw object then serializes to JsonElement.
// Needed because YamlDotNet can't cast scalar strings to JsonElement (used by OpenHands tool params).
public sealed class JsonElementNodeDeserializer : INodeDeserializer
{
    public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer objectDeserializer)
    {
        if (expectedType != typeof(JsonElement))
        {
            value = null;
            return false;
        }

        var raw = nestedObjectDeserializer(reader, typeof(object));
        value = JsonSerializer.SerializeToElement(raw);
        return true;
    }
}

public sealed class IntegerLikeNodeDeserializer : INodeDeserializer
{
    public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer objectDeserializer)
    {
        if (expectedType != typeof(IntegerLike))
        {
            value = null;
            return false;
        }

        if (reader.TryConsume<Scalar>(out var s))
        {
            if (long.TryParse(s.Value, out var integer))
            {
                value = IntegerLike.FromInteger(integer);
            }
            else
            {
                value = IntegerLike.FromString(s.Value);
            }
            return true;
        }

        value = null;
        return false;
    }
}

public sealed record WorkflowDefinition(WorkflowFrontMatter FrontMatter, string PromptTemplate);

// ht: SortedDictionary has reference equality, but Rust BTreeMap has value equality. Records ported from Rust
// need value equality for collection members. Helper compares dictionaries/lists by content.
internal static class CollectionEquality
{
    public static bool SortedDictEquals<TKey, TValue>(SortedDictionary<TKey, TValue>? a, SortedDictionary<TKey, TValue>? b)
        where TKey : notnull
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv)) return false;
            if (!Equals(v, bv)) return false;
        }
        return true;
    }

    public static bool ListEquals<T>(List<T>? a, List<T>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!Equals(a[i], b[i])) return false;
        return true;
    }
}

// ht: front matter types. YamlDotNet applies camelCase by default; we use ApplyNamingConvention=false
// and explicit [YamlMember(Alias=...)] for snake_case field names matching serde.
public sealed record WorkflowFrontMatter
{
    [YamlMember(Alias = "tracker", ApplyNamingConventions = false)]
    public TrackerFrontMatter Tracker { get; init; } = new();
    [YamlMember(Alias = "polling", ApplyNamingConventions = false)]
    public PollingFrontMatter Polling { get; init; } = new();
    [YamlMember(Alias = "workspace", ApplyNamingConventions = false)]
    public WorkspaceFrontMatter Workspace { get; init; } = new();
    [YamlMember(Alias = "hooks", ApplyNamingConventions = false)]
    public HooksFrontMatter Hooks { get; init; } = new();
    [YamlMember(Alias = "agent", ApplyNamingConventions = false)]
    public AgentFrontMatter Agent { get; init; } = new();
    [YamlMember(Alias = "openhands", ApplyNamingConventions = false)]
    public OpenHandsFrontMatter OpenHands { get; init; } = new();
    [YamlMember(Alias = "routing", ApplyNamingConventions = false)]
    public RoutingFrontMatter Routing { get; init; } = new();
    [YamlMember(Alias = "codex", ApplyNamingConventions = false)]
    public SortedDictionary<string, object?>? Codex { get; init; }
    [YamlMember(Alias = "logging", ApplyNamingConventions = false)]
    public SortedDictionary<string, object?>? Logging { get; init; }
    // ht: extensions flatten — captured via custom deserializer to detect unknown top-level keys.
    [YamlIgnore]
    public SortedDictionary<string, object?> Extensions { get; init; } = new();

    // ht: override equality so SortedDictionary members compare by content (matches Rust BTreeMap semantics).
    public bool Equals(WorkflowFrontMatter? other) =>
        other is not null
        && Tracker == other.Tracker
        && Polling == other.Polling
        && Workspace == other.Workspace
        && Hooks == other.Hooks
        && Agent == other.Agent
        && OpenHands == other.OpenHands
        && Routing == other.Routing
        && CollectionEquality.SortedDictEquals(Codex, other.Codex)
        && CollectionEquality.SortedDictEquals(Logging, other.Logging)
        && CollectionEquality.SortedDictEquals(Extensions, other.Extensions);

    public override int GetHashCode() =>
        HashCode.Combine(Tracker, Polling, Workspace, Hooks, Agent, OpenHands, Routing);
}

public sealed record TrackerFrontMatter
{
    [YamlMember(Alias = "kind", ApplyNamingConventions = false)]
    public string? Kind { get; init; }
    [YamlMember(Alias = "endpoint", ApplyNamingConventions = false)]
    public string? Endpoint { get; init; }
    [YamlMember(Alias = "api_key", ApplyNamingConventions = false)]
    public string? ApiKey { get; init; }
    [YamlMember(Alias = "project_slug", ApplyNamingConventions = false)]
    public string? ProjectSlug { get; init; }
    [YamlMember(Alias = "active_states", ApplyNamingConventions = false)]
    public List<string>? ActiveStates { get; init; }
    [YamlMember(Alias = "terminal_states", ApplyNamingConventions = false)]
    public List<string>? TerminalStates { get; init; }
}

public sealed record PollingFrontMatter
{
    [YamlMember(Alias = "interval_ms", ApplyNamingConventions = false)]
    public IntegerLike? IntervalMs { get; init; }
}

public sealed record WorkspaceFrontMatter
{
    [YamlMember(Alias = "root", ApplyNamingConventions = false)]
    public string? Root { get; init; }
}

public sealed record HooksFrontMatter
{
    [YamlMember(Alias = "after_create", ApplyNamingConventions = false)]
    public string? AfterCreate { get; init; }
    [YamlMember(Alias = "before_run", ApplyNamingConventions = false)]
    public string? BeforeRun { get; init; }
    [YamlMember(Alias = "after_run", ApplyNamingConventions = false)]
    public string? AfterRun { get; init; }
    [YamlMember(Alias = "before_remove", ApplyNamingConventions = false)]
    public string? BeforeRemove { get; init; }
    [YamlMember(Alias = "timeout_ms", ApplyNamingConventions = false)]
    public IntegerLike? TimeoutMs { get; init; }
}

public sealed record AgentFrontMatter
{
    [YamlMember(Alias = "max_concurrent_agents", ApplyNamingConventions = false)]
    public IntegerLike? MaxConcurrentAgents { get; init; }
    [YamlMember(Alias = "max_turns", ApplyNamingConventions = false)]
    public IntegerLike? MaxTurns { get; init; }
    [YamlMember(Alias = "max_retry_backoff_ms", ApplyNamingConventions = false)]
    public IntegerLike? MaxRetryBackoffMs { get; init; }
    [YamlMember(Alias = "stall_timeout_ms", ApplyNamingConventions = false)]
    public IntegerLike? StallTimeoutMs { get; init; }
    [YamlMember(Alias = "max_concurrent_agents_by_state", ApplyNamingConventions = false)]
    public SortedDictionary<string, IntegerLike>? MaxConcurrentAgentsByState { get; init; }
}

public sealed record RoutingFrontMatter
{
    [YamlMember(Alias = "harness", ApplyNamingConventions = false)]
    public string? Harness { get; init; }
    [YamlMember(Alias = "model", ApplyNamingConventions = false)]
    public string? Model { get; init; }
    [YamlMember(Alias = "model_profile", ApplyNamingConventions = false)]
    public string? ModelProfile { get; init; }
    [YamlMember(Alias = "harness_env", ApplyNamingConventions = false)]
    public string? HarnessEnv { get; init; }
    [YamlMember(Alias = "model_env", ApplyNamingConventions = false)]
    public string? ModelEnv { get; init; }
    [YamlMember(Alias = "model_profile_env", ApplyNamingConventions = false)]
    public string? ModelProfileEnv { get; init; }
}

public sealed record OpenHandsFrontMatter
{
    [YamlMember(Alias = "transport", ApplyNamingConventions = false)]
    public OpenHandsTransportFrontMatter Transport { get; init; } = new();
    [YamlMember(Alias = "local_server", ApplyNamingConventions = false)]
    public OpenHandsLocalServerFrontMatter LocalServer { get; init; } = new();
    [YamlMember(Alias = "conversation", ApplyNamingConventions = false)]
    public OpenHandsConversationFrontMatter Conversation { get; init; } = new();
    [YamlMember(Alias = "websocket", ApplyNamingConventions = false)]
    public OpenHandsWebSocketFrontMatter Websocket { get; init; } = new();
    [YamlMember(Alias = "mcp", ApplyNamingConventions = false)]
    public object? LegacyLinearBridge { get; init; }
}

public sealed record OpenHandsTransportFrontMatter
{
    [YamlMember(Alias = "base_url", ApplyNamingConventions = false)]
    public string? BaseUrl { get; init; }
    [YamlMember(Alias = "session_api_key_env", ApplyNamingConventions = false)]
    public string? SessionApiKeyEnv { get; init; }
}

public sealed record OpenHandsLocalServerFrontMatter
{
    [YamlMember(Alias = "enabled", ApplyNamingConventions = false)]
    public bool? Enabled { get; init; }
    [YamlMember(Alias = "command", ApplyNamingConventions = false)]
    public List<string>? Command { get; init; }
    [YamlMember(Alias = "startup_timeout_ms", ApplyNamingConventions = false)]
    public IntegerLike? StartupTimeoutMs { get; init; }
    [YamlMember(Alias = "readiness_probe_path", ApplyNamingConventions = false)]
    public string? ReadinessProbePath { get; init; }
    [YamlMember(Alias = "env", ApplyNamingConventions = false)]
    public SortedDictionary<string, string> Env { get; init; } = new();

    // ht: override equality so Env (SortedDictionary) and Command (List) compare by content.
    public bool Equals(OpenHandsLocalServerFrontMatter? other) =>
        other is not null
        && Enabled == other.Enabled
        && CollectionEquality.ListEquals(Command, other.Command)
        && Equals(StartupTimeoutMs, other.StartupTimeoutMs)
        && ReadinessProbePath == other.ReadinessProbePath
        && CollectionEquality.SortedDictEquals(Env, other.Env);

    public override int GetHashCode() =>
        HashCode.Combine(Enabled, StartupTimeoutMs, ReadinessProbePath);
}

public sealed record OpenHandsConversationFrontMatter
{
    [YamlMember(Alias = "reuse_policy", ApplyNamingConventions = false)]
    public string? ReusePolicy { get; init; }
    [YamlMember(Alias = "persistence_dir_relative", ApplyNamingConventions = false)]
    public string? PersistenceDirRelative { get; init; }
    [YamlMember(Alias = "max_iterations", ApplyNamingConventions = false)]
    public IntegerLike? MaxIterations { get; init; }
    [YamlMember(Alias = "stuck_detection", ApplyNamingConventions = false)]
    public bool? StuckDetection { get; init; }
    [YamlMember(Alias = "confirmation_policy", ApplyNamingConventions = false)]
    public OpenHandsConfirmationPolicyFrontMatter? ConfirmationPolicy { get; init; }
    [YamlMember(Alias = "agent", ApplyNamingConventions = false)]
    public OpenHandsConversationAgentFrontMatter? Agent { get; init; }
}

// ht: confirmation_policy has flatten options. Use custom deserializer to capture unknown keys.
public sealed record OpenHandsConfirmationPolicyFrontMatter
{
    [YamlMember(Alias = "kind", ApplyNamingConventions = false)]
    public string? Kind { get; init; }
    [YamlIgnore]
    public SortedDictionary<string, object?> Options { get; init; } = new();
}

public sealed record OpenHandsConfirmationPolicy
{
    public string Kind { get; init; } = "";
}

public sealed record OpenHandsConversationAgentFrontMatter
{
    [YamlMember(Alias = "kind", ApplyNamingConventions = false)]
    public string? Kind { get; init; }
    [YamlMember(Alias = "llm", ApplyNamingConventions = false)]
    public OpenHandsLlmFrontMatter? Llm { get; init; }
    [YamlMember(Alias = "condenser", ApplyNamingConventions = false)]
    public OpenHandsConversationCondenserFrontMatter? Condenser { get; init; }
    [YamlMember(Alias = "tools", ApplyNamingConventions = false)]
    public List<OpenHandsConversationToolFrontMatter>? Tools { get; init; }
    [YamlMember(Alias = "include_default_tools", ApplyNamingConventions = false)]
    public List<string>? IncludeDefaultTools { get; init; }
    [YamlMember(Alias = "log_completions", ApplyNamingConventions = false)]
    public bool? LogCompletions { get; init; }
    [YamlIgnore]
    public SortedDictionary<string, object?> Options { get; init; } = new();
}

public sealed record OpenHandsConversationToolFrontMatter
{
    [YamlMember(Alias = "name", ApplyNamingConventions = false)]
    public string Name { get; init; } = "";
    [YamlMember(Alias = "params", ApplyNamingConventions = false)]
    public SortedDictionary<string, JsonElement> Params { get; init; } = new();
}

public sealed record OpenHandsConversationCondenserFrontMatter
{
    [YamlMember(Alias = "enabled", ApplyNamingConventions = false)]
    public bool? Enabled { get; init; }
    [YamlMember(Alias = "max_size", ApplyNamingConventions = false)]
    public IntegerLike? MaxSize { get; init; }
    [YamlMember(Alias = "keep_first", ApplyNamingConventions = false)]
    public IntegerLike? KeepFirst { get; init; }
}

public sealed record OpenHandsLlmFrontMatter
{
    [YamlMember(Alias = "model", ApplyNamingConventions = false)]
    public string? Model { get; init; }
    [YamlMember(Alias = "api_key_env", ApplyNamingConventions = false)]
    public string? ApiKeyEnv { get; init; }
    [YamlMember(Alias = "base_url_env", ApplyNamingConventions = false)]
    public string? BaseUrlEnv { get; init; }
    [YamlMember(Alias = "credential_mode", ApplyNamingConventions = false)]
    public string? CredentialMode { get; init; }
    [YamlMember(Alias = "subscription", ApplyNamingConventions = false)]
    public OpenHandsSubscriptionCredentialFrontMatter? Subscription { get; init; }
    [YamlIgnore]
    public SortedDictionary<string, object?> Options { get; init; } = new();
}

public sealed record OpenHandsSubscriptionCredentialFrontMatter
{
    [YamlMember(Alias = "vendor", ApplyNamingConventions = false)]
    public string? Vendor { get; init; }
    [YamlMember(Alias = "access_token_env", ApplyNamingConventions = false)]
    public string? AccessTokenEnv { get; init; }
    [YamlMember(Alias = "account_id_env", ApplyNamingConventions = false)]
    public string? AccountIdEnv { get; init; }
    [YamlMember(Alias = "auth_directory_env", ApplyNamingConventions = false)]
    public string? AuthDirectoryEnv { get; init; }
    [YamlMember(Alias = "auth_method", ApplyNamingConventions = false)]
    public string? AuthMethod { get; init; }
    [YamlMember(Alias = "open_browser", ApplyNamingConventions = false)]
    public bool? OpenBrowser { get; init; }
    [YamlMember(Alias = "force_login", ApplyNamingConventions = false)]
    public bool? ForceLogin { get; init; }
}

public sealed record OpenHandsWebSocketFrontMatter
{
    [YamlMember(Alias = "enabled", ApplyNamingConventions = false)]
    public bool? Enabled { get; init; }
    [YamlMember(Alias = "ready_timeout_ms", ApplyNamingConventions = false)]
    public IntegerLike? ReadyTimeoutMs { get; init; }
    [YamlMember(Alias = "reconnect_initial_ms", ApplyNamingConventions = false)]
    public IntegerLike? ReconnectInitialMs { get; init; }
    [YamlMember(Alias = "reconnect_max_ms", ApplyNamingConventions = false)]
    public IntegerLike? ReconnectMaxMs { get; init; }
    [YamlMember(Alias = "auth_mode", ApplyNamingConventions = false)]
    public string? AuthMode { get; init; }
    [YamlMember(Alias = "query_param_name", ApplyNamingConventions = false)]
    public string? QueryParamName { get; init; }
}

// Resolved config types
public sealed record ResolvedWorkflow(WorkflowConfig Config, WorkflowExtensions Extensions, string PromptTemplate)
{
    public string EffectivePromptTemplate() =>
        string.IsNullOrWhiteSpace(PromptTemplate) ? WorkflowConstants.DEFAULT_PROMPT_TEMPLATE : PromptTemplate;

    public Result<string, PromptTemplateError> RenderPrompt<T>(T issue, uint? attempt) =>
        WorkflowTemplate.RenderPrompt(EffectivePromptTemplate(), issue, attempt);
}

public sealed record WorkflowConfig(
    TrackerConfig Tracker,
    PollingConfig Polling,
    WorkspaceConfig Workspace,
    HooksConfig Hooks,
    AgentConfig Agent,
    RoutingConfig Routing);

public sealed record WorkflowExtensions(OpenHandsConfig OpenHands);

public enum TrackerKind
{
    Linear,
}

public sealed record TrackerConfig(
    TrackerKind Kind,
    string Endpoint,
    string ApiKey,
    string ProjectSlug,
    List<string> ActiveStates,
    List<string> TerminalStates);

public sealed record PollingConfig(ulong IntervalMs);

public sealed record WorkspaceConfig(string Root);

public sealed record HooksConfig(
    string? AfterCreate,
    string? BeforeRun,
    string? AfterRun,
    string? BeforeRemove,
    ulong TimeoutMs);

public sealed record AgentConfig(
    ulong MaxConcurrentAgents,
    ulong MaxTurns,
    ulong MaxRetryBackoffMs,
    ulong? StallTimeoutMs,
    SortedDictionary<string, ulong> MaxConcurrentAgentsByState);

public sealed record RoutingConfig(
    string Harness,
    string? Model,
    string? ModelProfile,
    string HarnessEnv,
    string ModelEnv,
    string ModelProfileEnv,
    bool HarnessFromEnv,
    bool ModelFromEnv,
    bool ModelProfileFromEnv,
    bool DryRun);

public sealed record OpenHandsConfig(
    OpenHandsTransportConfig Transport,
    OpenHandsLocalServerConfig LocalServer,
    OpenHandsConversationConfig Conversation,
    OpenHandsWebSocketConfig Websocket);

public sealed record OpenHandsTransportConfig(string BaseUrl, string? SessionApiKeyEnv);

public sealed record OpenHandsLocalServerConfig(
    bool Enabled,
    List<string>? Command,
    ulong StartupTimeoutMs,
    string ReadinessProbePath,
    SortedDictionary<string, string> Env);

public sealed record OpenHandsConversationConfig(
    string ReusePolicy,
    string PersistenceDirRelative,
    ulong MaxIterations,
    bool StuckDetection,
    OpenHandsConfirmationPolicy ConfirmationPolicy,
    OpenHandsConversationAgentConfig Agent);

public sealed record OpenHandsConversationAgentConfig(
    string Kind,
    OpenHandsLlmConfig? Llm,
    OpenHandsConversationCondenserConfig? Condenser,
    List<OpenHandsConversationToolConfig>? Tools,
    List<string>? IncludeDefaultTools,
    bool LogCompletions,
    SortedDictionary<string, object?> Options);

public sealed record OpenHandsConversationToolConfig(
    string Name,
    SortedDictionary<string, JsonElement> Params);

public sealed record OpenHandsConversationCondenserConfig(ulong MaxSize, ulong KeepFirst);

public sealed record OpenHandsLlmConfig(
    string? Model,
    string? ApiKeyEnv,
    string? BaseUrlEnv,
    string CredentialMode,
    OpenHandsSubscriptionCredentialConfig? Subscription,
    SortedDictionary<string, object?> Options);

public sealed record OpenHandsSubscriptionCredentialConfig(
    string Vendor,
    string AccessTokenEnv,
    string? AccountIdEnv,
    string? AuthDirectoryEnv,
    string AuthMethod,
    bool OpenBrowser,
    bool ForceLogin);

public sealed record OpenHandsWebSocketConfig(
    bool Enabled,
    ulong ReadyTimeoutMs,
    ulong ReconnectInitialMs,
    ulong ReconnectMaxMs,
    string AuthMode,
    string QueryParamName);

public interface IEnvironment
{
    string? Get(string name);
}

public readonly record struct ProcessEnvironment : IEnvironment
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}

public sealed record PromptContext<T>(T Issue, uint? Attempt);
