using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;
using OpenSymphony.Workflow;
using OpenSymphony.Workspace;
using WorkspacePromptKind = OpenSymphony.Workspace.PromptKind;

namespace OpenSymphony.OpenHands;

// ht: port of opensymphony-openhands session.rs — IssueSessionRunner with real
//   create/resume, prompt, send, run, WebSocket streaming, terminal detection,
//   manifest persistence. P1 extras (condenser retry, context-overflow recovery,
//   Codex harness) are intentionally skipped.

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
            TerminalWaitTimeout = workflow.Config.Agent.StallTimeoutMs is { } ms
                ? TimeSpan.FromMilliseconds(ms)
                : TimeSpan.FromMilliseconds(300_000),
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueSessionPromptKind
{
    [JsonStringEnumMemberName("full")]
    Full,
    [JsonStringEnumMemberName("continuation")]
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

    public static WorkspacePromptKind ToWorkspaceKind(this IssueSessionPromptKind kind) => kind switch
    {
        IssueSessionPromptKind.Full => WorkspacePromptKind.Full,
        IssueSessionPromptKind.Continuation => WorkspacePromptKind.Continuation,
        _ => WorkspacePromptKind.Full,
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

    public Result<ConversationCreateRequest, string> ToCreateRequest(
        IEnvironment env, string workingDir, string persistenceDir, Guid? conversationId = null)
    {
        var llm = ToLlmConfig(env);
        if (llm.IsErr) return Result<ConversationCreateRequest, string>.Err(llm.Error);

        return Result<ConversationCreateRequest, string>.Ok(new ConversationCreateRequest(
            conversationId ?? Guid.NewGuid(),
            new WorkspaceConfig(workingDir, WorkspaceKind),
            persistenceDir,
            MaxIterations,
            StuckDetection,
            new ConfirmationPolicy(ConfirmationPolicyKind),
            new AgentConfig(
                AgentKind,
                llm.Value,
                Condenser is { } c ? CondenserConfig.LlmSummarizing(llm.Value, c.MaxSize, c.KeepFirst) : null,
                AgentTools,
                AgentIncludeDefaultTools)));
    }

    private Result<LlmConfig, string> ToLlmConfig(IEnvironment env)
    {
        if (LlmCredentialMode == "openai_subscription")
            return ToOpenAiSubscriptionLlmConfig(env);

        var apiKey = ResolveProviderOverride(env, "openhands.conversation.agent.llm.api_key_env", LlmApiKeyEnv)
            ?? env.Get("LLM_API_KEY");
        var baseUrl = ResolveProviderOverride(env, "openhands.conversation.agent.llm.base_url_env", LlmBaseUrlEnv)
            ?? env.Get("LLM_BASE_URL");

        return Result<LlmConfig, string>.Ok(new LlmConfig
        {
            Model = LlmModel,
            ApiKey = apiKey,
            BaseUrl = baseUrl,
        });
    }

    private Result<LlmConfig, string> ToOpenAiSubscriptionLlmConfig(IEnvironment env)
    {
        if (LlmSubscription is not { } sub)
            return Result<LlmConfig, string>.Err("openhands.conversation.agent.llm.subscription is required for openai_subscription mode");
        if (sub.Vendor != "openai")
            return Result<LlmConfig, string>.Err($"unsupported OpenHands subscription vendor `{sub.Vendor}`");

        var accessToken = ResolveProviderOverride(env, "openhands.conversation.agent.llm.subscription.access_token_env", sub.AccessTokenEnv);
        var accountId = ResolveProviderOverride(env, "openhands.conversation.agent.llm.subscription.account_id_env", sub.AccountIdEnv);

        var extraHeaders = new Dictionary<string, string>
        {
            ["OpenAI-Beta"] = "responses=experimental",
            ["User-Agent"] = $"openhands-sdk (OpenSymphony; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; OpenSymphony)",
        };
        if (!string.IsNullOrEmpty(accountId))
            extraHeaders["X-OpenAI-Account-ID"] = accountId;

        var litellmExtraBody = new Dictionary<string, JsonElement>
        {
            ["service_tier"] = JsonSerializer.SerializeToElement("auto"),
            ["store"] = JsonSerializer.SerializeToElement(true),
        };

        return Result<LlmConfig, string>.Ok(new LlmConfig
        {
            Model = LlmModel,
            ApiKey = accessToken,
            ExtraHeaders = extraHeaders,
            LitellmExtraBody = litellmExtraBody,
            Stream = false,
        });
    }

    private static string? ResolveProviderOverride(IEnvironment env, string field, string? envName)
    {
        var value = envName is not null ? env.Get(envName) : null;
        if (!string.IsNullOrWhiteSpace(value)) return value;
        value = env.Get(field);
        return string.IsNullOrWhiteSpace(value) ? null : value;
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
    public string? IssueId { get; init; }
    public string? Identifier { get; init; }
    public Guid? ConversationId { get; init; }
    public string ReusePolicy { get; set; } = "per_issue";
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

    public static IssueConversationManifest New(
        string issueId, string identifier, Guid conversationId, string reusePolicy,
        string persistenceDir, DateTimeOffset attachedAt, string? resetReason,
        ConversationLaunchProfile launchProfile, IEnvironment env)
    {
        return new IssueConversationManifest
        {
            IssueId = issueId,
            Identifier = identifier,
            ConversationId = conversationId,
            ReusePolicy = reusePolicy,
            PersistenceDir = persistenceDir,
            CreatedAt = attachedAt,
            UpdatedAt = attachedAt,
            LastAttachedAt = attachedAt,
            LaunchProfile = launchProfile,
            LlmConfigFingerprint = LlmConfigFingerprint.FromLlmConfig(launchProfile.ToCreateRequest(env, "", persistenceDir).Value!.Agent.Llm),
            FreshConversation = true,
            ResetReason = resetReason,
        };
    }

    public IssueSessionPromptKind PromptKind() =>
        WorkflowPromptSeeded ? IssueSessionPromptKind.Continuation : IssueSessionPromptKind.Full;

    public void RecordPrompt(IssueSessionPromptKind kind, string path, DateTimeOffset at)
    {
        LastPromptKind = kind;
        LastPromptAt = at;
        LastPromptPath = path;
        UpdatedAt = at;
    }

    public void ApplyRuntimeSnapshot(RuntimeEventStream stream)
    {
        LastExecutionStatus = stream.StateMirror.ExecutionStatus;
        if (stream.EventCache.Items.Count > 0)
        {
            var last = stream.EventCache.Items[^1];
            LastEventId = last.Id;
            LastEventKind = last.Kind;
            LastEventAt = last.Timestamp;
            LastEventSummary = SummarizeEvent(last);
        }

        if (stream.StateMirror.AccumulatedTokenUsage() is { } usage)
        {
            if (usage.Input > InputTokens) InputTokens = usage.Input;
            if (usage.Output > OutputTokens) OutputTokens = usage.Output;
            if (usage.CacheRead > CacheReadTokens) CacheReadTokens = usage.CacheRead;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyTransportDiagnostics(TransportDiagnostics? diagnostics, string serverBaseUrl)
    {
        ServerBaseUrl = serverBaseUrl;
        TransportTarget = diagnostics?.TargetKind.AsStr();
        HttpAuthMode = diagnostics?.HttpAuthKind.AsStr();
        WebsocketAuthMode = diagnostics?.WebsocketAuthKind.AsStr();
        WebsocketQueryParamName = diagnostics?.WebsocketQueryParamName;
    }

    public ConversationMetadata ToDomainMetadata(RuntimeStreamState streamState) => new()
    {
        ConversationId = ConversationId ?? Guid.Empty,
        ExecutionStatus = LastExecutionStatus ?? "",
        ServerBaseUrl = ServerBaseUrl,
        TransportTarget = TransportTarget,
        HttpAuthMode = HttpAuthMode,
        WebsocketAuthMode = WebsocketAuthMode,
        WebsocketQueryParamName = WebsocketQueryParamName,
        FreshConversation = FreshConversation,
        RuntimeContractVersion = RuntimeContractVersion,
        StreamState = streamState,
        LastEventId = LastEventId,
        LastEventKind = LastEventKind,
        LastEventAt = LastEventAt is { } at ? TimestampMs.New((ulong)at.ToUnixTimeMilliseconds()) : null,
        LastEventSummary = LastEventSummary,
        InputTokens = InputTokens,
        OutputTokens = OutputTokens,
        CacheReadTokens = CacheReadTokens,
        TotalTokens = InputTokens + OutputTokens,
    };

    public bool IsReusableFor(string issueIdentifier, string persistenceDir, string reusePolicy)
    {
        return Identifier == issueIdentifier
            && PersistenceDir == persistenceDir
            && ReusePolicy == reusePolicy
            && RuntimeContractVersion == SessionConstants.RUNTIME_CONTRACT_VERSION;
    }

    private static string SummarizeEvent(EventEnvelope evt) =>
        KnownEvent.FromEnvelope(evt).ActivitySummary()?.Preview ?? evt.Kind;
}

public sealed class IssueSessionContext
{
    public string RunId { get; init; } = "";
    public string? IssueId { get; init; }
    public string? Identifier { get; init; }
    public string? WorkerId { get; init; }
    public uint? Attempt { get; init; }
    public uint NormalRetryCount { get; init; }
    public uint TurnCount { get; init; }
    public uint MaxTurns { get; init; }
    public IssueSessionPromptKind PromptKind { get; init; }
    public string? PromptPath { get; init; }
    public Guid? ConversationId { get; init; }
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
    public WorkerOutcomeRecord? WorkerOutcome { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record IssueSessionResult
{
    public IssueSessionPromptKind PromptKind { get; init; }
    public ConversationMetadata? Conversation { get; init; }
    public WorkerOutcomeRecord? WorkerOutcome { get; init; }
    public string RunStatus { get; init; } = "";
}

public sealed class IssueSessionError : Exception
{
    public IssueSessionError(string message) : base(message) { }
    public static IssueSessionError Workspace(WorkspaceError error) => new($"workspace error: {error.Message}");
    public static IssueSessionError OpenHands(OpenHandsError error) => new($"OpenHands error: {error.Message}");
    public static IssueSessionError Unexpected(string detail) => new($"unexpected issue session error: {detail}");
}

public sealed record RehydrationResult
{
    public bool Reused { get; init; }
    public Guid? ConversationId { get; init; }
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
    public ulong TotalTokens { get; set; }
    public string? ServerBaseUrl { get; init; }
    public string? TransportTarget { get; init; }
    public string? HttpAuthMode { get; init; }
    public string? WebsocketAuthMode { get; init; }
    public string? WebsocketQueryParamName { get; init; }
    public bool FreshConversation { get; init; }
    public string? RuntimeContractVersion { get; init; }
    public RuntimeStreamState StreamState { get; init; }
    public string? LastEventId { get; init; }
    public string? LastEventKind { get; init; }
    public TimestampMs? LastEventAt { get; init; }
    public string? LastEventSummary { get; init; }
}

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

    public async Task<Result<IssueSessionResult, IssueSessionError>> RunAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        NormalizedIssue issue,
        RunAttempt run,
        ResolvedWorkflow workflow,
        IIssueSessionObserver? observer = null,
        IEnvironment? env = null,
        CancellationToken ct = default)
    {
        observer ??= new NullIssueSessionObserver();
        env ??= new ProcessEnvironment();

        try
        {
            var activeSession = await InitializeSessionAsync(workspaceManager, workspace, runManifest, issue, run, workflow, env, observer, ct);
            var preparedTurn = await PrepareTurnAsync(workspaceManager, workspace, runManifest, issue, run, workflow, activeSession, observer, ct);
            await StartTurnAsync(workspaceManager, workspace, runManifest, run, activeSession, preparedTurn, observer, ct);
            var outcome = await AwaitTerminalOutcomeAsync(activeSession, preparedTurn.BaselineEventIds, observer, ct);
            return await FinalizeActiveSessionAsync(workspaceManager, workspace, runManifest, run, activeSession, outcome, ct);
        }
        catch (EarlyResultException ex)
        {
            return Result<IssueSessionResult, IssueSessionError>.Ok(ex.Result);
        }
        catch (IssueSessionError ex)
        {
            return Result<IssueSessionResult, IssueSessionError>.Err(ex);
        }
    }

    // ── session lifecycle ─────────────────────────────────────────────────────

    private async Task<ActiveSession> InitializeSessionAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        NormalizedIssue issue,
        RunAttempt run,
        ResolvedWorkflow workflow,
        IEnvironment env,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        var expectedPersistenceDir = ConfiguredPersistenceDir(workflow, workspace);
        var expectedReusePolicy = _config.ReusePolicy.AsStr();

        if (_config.ReusePolicy.Kind == IssueSessionReusePolicyKind.Unsupported)
        {
            var outcome = FailedOutcome(
                "workflow configured an unsupported OpenHands conversation reuse policy",
                $"unsupported OpenHands conversation reuse policy `{_config.ReusePolicy.Value}`; supported runtime policies: `per_issue`, `fresh_each_run`");
            var early = await PersistFailureWithoutStreamAsync(workspaceManager, workspace, runManifest, run, IssueSessionPromptKind.Full, null, outcome, ct);
            throw new EarlyResultException(early);
        }

        if (_config.ReusePolicy.Kind == IssueSessionReusePolicyKind.PerIssue)
        {
            var manifest = await LoadManifestAsync(workspaceManager, workspace, ct);
            if (manifest is not null && manifest.IsReusableFor(issue.Identifier.Value, expectedPersistenceDir, expectedReusePolicy))
            {
                var reused = await TryReuseSessionAsync(workspaceManager, workspace, runManifest, issue, run, workflow, manifest, env, observer, ct);
                if (reused is not null) return reused;
            }
            return await CreateFreshSessionAsync(workspaceManager, workspace, runManifest, issue, run, workflow, manifest?.ResetReason, env, observer, ct);
        }

        // FreshEachRun
        return await CreateFreshSessionAsync(workspaceManager, workspace, runManifest, issue, run, workflow,
            "workflow reuse policy `fresh_each_run` requested a new conversation for this run", env, observer, ct);
    }

    private async Task<IssueConversationManifest?> LoadManifestAsync(
        WorkspaceManager workspaceManager, WorkspaceHandle workspace, CancellationToken ct)
    {
        var result = await workspaceManager.ReadTextArtifact(workspace, workspace.ConversationManifestPath());
        if (result.IsErr) throw IssueSessionError.Workspace(result.Error);
        var raw = result.Value;
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<IssueConversationManifest>(raw, OpenHandsJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            return new IssueConversationManifest { ResetReason = $"invalid conversation manifest: {ex.Message}" };
        }
    }

    private async Task<ActiveSession?> TryReuseSessionAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        NormalizedIssue issue,
        RunAttempt run,
        ResolvedWorkflow workflow,
        IssueConversationManifest manifest,
        IEnvironment env,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        if (manifest.ConversationId is not { } conversationId)
            return null;

        var streamResult = await _client.ConnectStreamAsync(conversationId, _config.RuntimeStream, ct);
        if (streamResult.IsErr)
        {
            return await CreateFreshSessionAsync(workspaceManager, workspace, runManifest, issue, run, workflow,
                $"failed to attach existing conversation {conversationId}: {streamResult.Error.Message}", env, observer, ct);
        }

        var stream = streamResult.Value;
        var attachedAt = DateTimeOffset.UtcNow;
        manifest.FreshConversation = false;
        manifest.ReusePolicy = _config.ReusePolicy.AsStr();
        manifest.LlmConfigFingerprint ??= LlmConfigFingerprint.FromLlmConfig(stream.Conversation.Agent.Llm);
        var diagResult = _client.TransportDiagnostics();
        var diagnostics = diagResult.IsOk ? diagResult.Value : null;
        manifest.ApplyTransportDiagnostics(diagnostics, _client.BaseUrl);
        manifest.RuntimeContractVersion = SessionConstants.RUNTIME_CONTRACT_VERSION;
        manifest.LastAttachedAt = attachedAt;
        manifest.UpdatedAt = attachedAt;
        manifest.ResetReason = null;
        manifest.ApplyRuntimeSnapshot(stream);

        await WriteManifestAsync(workspaceManager, workspace, workspace.ConversationManifestPath(), manifest, ct);
        await WriteJsonArtifactAsync(workspaceManager, workspace, LastConversationStatePath(workspace), stream.Conversation, ct);

        var session = new ActiveSession(IssueSessionPromptKind.Continuation, stream, manifest, null);
        AccumulateTokens(session);
        return session;
    }

    private async Task<ActiveSession> CreateFreshSessionAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        NormalizedIssue issue,
        RunAttempt run,
        ResolvedWorkflow workflow,
        string? resetReason,
        IEnvironment env,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        var launchProfileResult = ConversationLaunchProfile.FromWorkflow(workflow);
        if (launchProfileResult.IsErr)
        {
            var outcome = FailedOutcome("failed to build conversation launch profile", launchProfileResult.Error);
            var early = await PersistFailureWithoutStreamAsync(workspaceManager, workspace, runManifest, run, IssueSessionPromptKind.Full, null, outcome, ct);
            throw new EarlyResultException(early);
        }
        var launchProfile = launchProfileResult.Value;

        var requestResult = launchProfile.ToCreateRequest(env, workspace.WorkspacePath, ConfiguredPersistenceDir(workflow, workspace));
        if (requestResult.IsErr)
        {
            var outcome = FailedOutcome("failed to build OpenHands conversation create request", requestResult.Error);
            var early = await PersistFailureWithoutStreamAsync(workspaceManager, workspace, runManifest, run, IssueSessionPromptKind.Full, null, outcome, ct);
            throw new EarlyResultException(early);
        }
        var request = requestResult.Value;
        await WriteJsonArtifactAsync(workspaceManager, workspace, CreateConversationRequestPath(workspace), request, ct);

        var conversationResult = await _client.CreateConversationAsync(request, ct);
        if (conversationResult.IsErr)
        {
            var outcome = FailedOutcome("failed to create OpenHands conversation", conversationResult.Error.Message);
            var early = await PersistFailureWithoutStreamAsync(workspaceManager, workspace, runManifest, run, IssueSessionPromptKind.Full, null, outcome, ct);
            throw new EarlyResultException(early);
        }
        var conversation = conversationResult.Value;

        var streamResult = await _client.ConnectStreamAsync(conversation.ConversationId, _config.RuntimeStream, ct);
        if (streamResult.IsErr)
        {
            var diagResult = _client.TransportDiagnostics();
            var metadata = BuildSummaryMetadata(conversation, true, RuntimeStreamState.Failed, diagResult.IsOk ? diagResult.Value : null, _client.BaseUrl);
            var outcome = FailedOutcome("failed to attach runtime stream for a fresh conversation", streamResult.Error.Message);
            var early = await PersistFailureWithoutStreamAsync(workspaceManager, workspace, runManifest, run, IssueSessionPromptKind.Full, metadata, outcome, ct);
            throw new EarlyResultException(early);
        }
        var stream = streamResult.Value;

        var attachedAt = DateTimeOffset.UtcNow;
        var manifest = IssueConversationManifest.New(
            issue.Id.Value, issue.Identifier.Value, conversation.ConversationId,
            _config.ReusePolicy.AsStr(), ConfiguredPersistenceDir(workflow, workspace), attachedAt,
            resetReason, launchProfile, env);
        manifest.LlmConfigFingerprint = LlmConfigFingerprint.FromLlmConfig(request.Agent.Llm);
        var diagResult2 = _client.TransportDiagnostics();
        manifest.ApplyTransportDiagnostics(diagResult2.IsOk ? diagResult2.Value : null, _client.BaseUrl);
        manifest.ApplyRuntimeSnapshot(stream);

        await WriteManifestAsync(workspaceManager, workspace, workspace.ConversationManifestPath(), manifest, ct);
        await WriteJsonArtifactAsync(workspaceManager, workspace, LastConversationStatePath(workspace), conversation, ct);

        var session = new ActiveSession(IssueSessionPromptKind.Full, stream, manifest, null);
        AccumulateTokens(session);
        return session;
    }

    // ── turn preparation ──────────────────────────────────────────────────────

    private async Task<PreparedTurn> PrepareTurnAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        NormalizedIssue issue,
        RunAttempt run,
        ResolvedWorkflow workflow,
        ActiveSession activeSession,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        if (activeSession.Stream.StateMirror.ExecutionStatus is "running" or "queued")
        {
            await WaitForActiveTurnToFinishAsync(activeSession.Stream, observer, ct);
            activeSession.Manifest.ApplyRuntimeSnapshot(activeSession.Stream);
        }

        var promptResult = workflow.RenderPrompt(issue, run.Attempt?.Get());
        var prompt = activeSession.PromptKind switch
        {
            IssueSessionPromptKind.Full => promptResult.IsOk
                ? promptResult.Value
                : throw IssueSessionError.Unexpected($"failed to render full prompt: {promptResult.Error.Message}"),
            IssueSessionPromptKind.Continuation => BuildContinuationGuidance(issue, run),
            _ => throw IssueSessionError.Unexpected($"unknown prompt kind {activeSession.PromptKind}"),
        };

        var promptPath = workspace.LatestPromptPath(activeSession.PromptKind.ToWorkspaceKind());
        await WriteTextArtifactAsync(workspaceManager, workspace, promptPath, prompt, ct);
        var promptRecordedAt = DateTimeOffset.UtcNow;
        activeSession.Manifest.RecordPrompt(activeSession.PromptKind, promptPath, promptRecordedAt);
        activeSession.PromptPath = promptPath;
        await WriteManifestAsync(workspaceManager, workspace, workspace.ConversationManifestPath(), activeSession.Manifest, ct);

        if (activeSession.Manifest.ConversationId is not { } conversationId)
            throw IssueSessionError.Unexpected("conversation manifest contained an invalid conversation ID");

        var baselineEventIds = activeSession.Stream.EventCache.Items.Select(e => e.Id).ToHashSet();
        Console.WriteLine($"[PrepareTurn] baseline count={baselineEventIds.Count}, ids=[{string.Join(",", baselineEventIds)}]");
        return new PreparedTurn(conversationId, prompt, baselineEventIds);
    }

    // ── turn start ───────────────────────────────────────────────────────────

    private async Task StartTurnAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        RunAttempt run,
        ActiveSession activeSession,
        PreparedTurn preparedTurn,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        var sendResult = await _client.SendMessageAsync(preparedTurn.ConversationId, SendMessageRequest.UserText(preparedTurn.Prompt), ct);
        if (sendResult.IsErr)
        {
            var outcome = FailedOutcome($"failed to send {activeSession.PromptKind.AsStr()} prompt event", sendResult.Error.Message);
            var final = await FinalizeActiveSessionAsync(workspaceManager, workspace, runManifest, run, activeSession, outcome, ct);
            if (final.IsErr) throw final.Error;
            throw new EarlyResultException(final.Value);
        }

        if (activeSession.PromptKind == IssueSessionPromptKind.Full)
            activeSession.Manifest.WorkflowPromptSeeded = true;
        await WriteManifestAsync(workspaceManager, workspace, workspace.ConversationManifestPath(), activeSession.Manifest, ct);

        var hadConflict = false;
        while (true)
        {
            var runResult = await _client.RunConversationAsync(preparedTurn.ConversationId, ct);
            Console.WriteLine($"[StartTurn] RunConversation result ok={runResult.IsOk}, status={runResult.Error?.StatusCode}, items={activeSession.Stream.EventCache.Items.Count}");
            if (runResult.IsOk) break;

            if (runResult.Error.StatusCode == 409 && !hadConflict)
            {
                hadConflict = true;
                observer.OnLaunch(activeSession.Manifest.ToDomainMetadata(RuntimeStreamState.Ready));
                await activeSession.Stream.ReconcileEventsAsync(ct);
                await WaitForActiveTurnToFinishAsync(activeSession.Stream, observer, ct);
                activeSession.Manifest.ApplyRuntimeSnapshot(activeSession.Stream);
                AccumulateTokens(activeSession);
                preparedTurn.BaselineEventIds.UnionWith(activeSession.Stream.EventCache.Items.Select(e => e.Id));
                continue;
            }

            var outcome = FailedOutcome("failed to trigger conversation run", runResult.Error.Message);
            var final = await FinalizeActiveSessionAsync(workspaceManager, workspace, runManifest, run, activeSession, outcome, ct);
            if (final.IsErr) throw final.Error;
            throw new EarlyResultException(final.Value);
        }

        runManifest.Status = RunStatus.Running;
        runManifest.StatusDetail = $"{activeSession.PromptKind.AsStr()} prompt sent to conversation {activeSession.Manifest.ConversationId}";
        await WriteRunManifestAsync(workspaceManager, workspace, runManifest, ct);
        await WriteJsonArtifactAsync(workspaceManager, workspace, workspace.SessionContextPath(),
            BuildSessionContext(runManifest, run, activeSession.Manifest, activeSession.PromptKind, activeSession.PromptPath, null), ct);
        AccumulateTokens(activeSession);
        observer.OnLaunch(activeSession.Manifest.ToDomainMetadata(RuntimeStreamState.Ready));
    }

    // ── terminal outcome ────────────────────────────────────────────────────

    private async Task<NormalizedOutcome> AwaitTerminalOutcomeAsync(
        ActiveSession session,
        HashSet<string> baselineEventIds,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        var idleTimeout = _config.TerminalWaitTimeout;
        var totalCap = _config.TotalRuntimeCap;
        var startedAt = DateTimeOffset.UtcNow;
        var lastActivityAt = startedAt;
        var nextTokenAccumulation = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);

        while (true)
        {
            if (DateTimeOffset.UtcNow >= nextTokenAccumulation)
            {
                AccumulateTokens(session);
                UpdateConversationMetadata(observer, session);
                nextTokenAccumulation = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
            }

            var check = await TerminalOutcomeFromStateAsync(session.Stream, baselineEventIds, observer, ct);
            Console.WriteLine($"[AwaitTerminal] check={check.GetType().Name}, state={session.Stream.StateMirror.ExecutionStatus}, terminal={session.Stream.StateMirror.TerminalStatus()}, cache={session.Stream.EventCache.Items.Count}");
            if (check is TerminalCheckResult.Terminal terminal)
            {
                AccumulateTokens(session);
                UpdateConversationMetadata(observer, session);
                return terminal.Outcome;
            }
            if (check is TerminalCheckResult.Progress)
                lastActivityAt = DateTimeOffset.UtcNow;

            var now = DateTimeOffset.UtcNow;
            var remainingIdle = idleTimeout - (now - lastActivityAt);
            if (remainingIdle <= TimeSpan.Zero)
            {
                var reconciled = await session.Stream.ReconcileEventsAsync(ct);
                if (reconciled.IsOk && reconciled.Value > 0)
                    ObserveLatestEvent(observer, session.Stream);
                var afterReconcile = await TerminalOutcomeFromStateAsync(session.Stream, baselineEventIds, observer, ct);
                if (afterReconcile is TerminalCheckResult.Terminal t2)
                    return t2.Outcome;
                return FailedOutcome(
                    "runtime did not reach a terminal state before the stall timeout",
                    $"no progress observed within {idleTimeout.TotalMilliseconds} ms idle timeout");
            }

            if (totalCap is { } cap && now - startedAt >= cap)
            {
                return FailedOutcome(
                    "runtime exceeded total runtime cap",
                    $"total runtime cap {cap.TotalMilliseconds} ms exceeded");
            }

            using var eventCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            eventCts.CancelAfter(remainingIdle);
            Result<EventEnvelope?, OpenHandsError> next;
            try
            {
                next = await session.Stream.NextEventAsync(eventCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine("[AwaitTerminal] NextEventAsync canceled, continue");
                continue;
            }

            Console.WriteLine($"[AwaitTerminal] NextEventAsync ok={next.IsOk}, value={next.Value?.Id ?? "null"}, err={next.Error?.Message}");

            if (next.IsErr)
            {
                var reconciled = await session.Stream.ReconcileEventsAsync(ct);
                if (reconciled.IsOk && reconciled.Value > 0)
                    ObserveLatestEvent(observer, session.Stream);
                var after = await TerminalOutcomeFromStateAsync(session.Stream, baselineEventIds, observer, ct);
                if (after is TerminalCheckResult.Terminal t3)
                    return t3.Outcome;
                return FailedOutcome("runtime event stream failed before terminal status", next.Error.Message);
            }

            if (next.Value is { } evt)
            {
                ObserveEvent(observer, evt);
                lastActivityAt = DateTimeOffset.UtcNow;
                if (session.Stream.StateMirror.ExecutionStatus is { } status)
                    observer.OnConversationUpdate(session.Manifest.ToDomainMetadata(RuntimeStreamState.Ready));
                continue;
            }

            // Stream ended gracefully.
            var finalReconcile = await session.Stream.ReconcileEventsAsync(ct);
            if (finalReconcile.IsOk && finalReconcile.Value > 0)
                ObserveLatestEvent(observer, session.Stream);
            var finalCheck = await TerminalOutcomeFromStateAsync(session.Stream, baselineEventIds, observer, ct);
            if (finalCheck is TerminalCheckResult.Terminal t4)
                return t4.Outcome;
            return FailedOutcome("runtime event stream ended before terminal status", "runtime event stream closed before a terminal state was observed");
        }
    }

    private async Task<TerminalCheckResult> TerminalOutcomeFromStateAsync(
        RuntimeEventStream stream,
        HashSet<string> baselineEventIds,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        var hasCurrentTurnActivity = stream.EventCache.Items.Any(e => !baselineEventIds.Contains(e.Id));
        if (!hasCurrentTurnActivity)
            return new TerminalCheckResult.NoProgress();

        if (LatestCurrentTurnError(stream.EventCache.Items, baselineEventIds) is { } errorDetail)
        {
            return new TerminalCheckResult.Terminal(FailedOutcome(
                "received ConversationErrorEvent during the current run", errorDetail));
        }

        switch (stream.StateMirror.TerminalStatus())
        {
            case TerminalExecutionStatus.Finished:
                if (await ConfirmFinishedTerminalStateAsync(stream, baselineEventIds, observer, ct))
                    return new TerminalCheckResult.Terminal(new NormalizedOutcome(WorkerOutcomeKind.Succeeded, "OpenHands execution_status `finished`", null));
                return new TerminalCheckResult.NoProgress();
            case TerminalExecutionStatus.Error:
                var stateError = ExtractErrorDetailFromState(stream.StateMirror) ?? "execution_status error";
                return new TerminalCheckResult.Terminal(FailedOutcome("OpenHands execution_status `error`", stateError));
            case TerminalExecutionStatus.Stuck:
                return new TerminalCheckResult.Terminal(new NormalizedOutcome(WorkerOutcomeKind.Stalled, "OpenHands execution_status `stuck`", stream.StateMirror.ExecutionStatus));
            default:
                return new TerminalCheckResult.Progress();
        }
    }

    private async Task<bool> ConfirmFinishedTerminalStateAsync(
        RuntimeEventStream stream,
        HashSet<string> baselineEventIds,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _config.FinishedDrainTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (LatestCurrentTurnError(stream.EventCache.Items, baselineEventIds) is not null)
                return false;
            if (stream.StateMirror.TerminalStatus() != TerminalExecutionStatus.Finished)
                return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_config.FinishedDrainTimeout);
            try
            {
                var next = await stream.NextEventAsync(cts.Token);
                if (next.IsErr)
                    return FinishedStreamErrorIsTolerable(next.Error);
                if (next.Value is { } evt)
                {
                    ObserveEvent(observer, evt);
                    continue;
                }
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return true;
            }
        }
        return true;
    }

    // ── finalization ─────────────────────────────────────────────────────────

    private async Task<Result<IssueSessionResult, IssueSessionError>> FinalizeActiveSessionAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        RunAttempt run,
        ActiveSession session,
        NormalizedOutcome outcome,
        CancellationToken ct)
    {
        session.Manifest.ApplyRuntimeSnapshot(session.Stream);
        await WriteJsonArtifactAsync(workspaceManager, workspace, LastConversationStatePath(workspace), session.Stream.Conversation, ct);

        var runStatus = RunStatusFor(outcome.Kind);
        runManifest.Status = runStatus;
        runManifest.StatusDetail = outcome.Error ?? outcome.Summary;
        await WriteRunManifestAsync(workspaceManager, workspace, runManifest, ct);

        var workerOutcome = WorkerOutcomeRecord.FromRun(
            run, outcome.Kind, TimestampMs.New((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            outcome.Summary, outcome.Error);

        await WriteJsonArtifactAsync(workspaceManager, workspace, workspace.SessionContextPath(),
            BuildSessionContext(runManifest, run, session.Manifest, session.PromptKind, session.PromptPath, workerOutcome), ct);
        await WriteManifestAsync(workspaceManager, workspace, workspace.ConversationManifestPath(), session.Manifest, ct);

        var conversation = session.Manifest.ToDomainMetadata(RuntimeStreamState.Closed);
        await session.Stream.CloseAsync();

        return Result<IssueSessionResult, IssueSessionError>.Ok(new IssueSessionResult
        {
            PromptKind = session.PromptKind,
            Conversation = conversation,
            WorkerOutcome = workerOutcome,
            RunStatus = runStatus.ToSnakeCaseString(),
        });
    }

    private sealed class EarlyResultException : Exception
    {
        public IssueSessionResult Result { get; }
        public EarlyResultException(IssueSessionResult result) : base("early result") => Result = result;
    }

    private async Task<IssueSessionResult> PersistFailureWithoutStreamAsync(
        WorkspaceManager workspaceManager,
        WorkspaceHandle workspace,
        RunManifest runManifest,
        RunAttempt run,
        IssueSessionPromptKind promptKind,
        ConversationMetadata? conversation,
        NormalizedOutcome outcome,
        CancellationToken ct)
    {
        var runStatus = RunStatusFor(outcome.Kind);
        runManifest.Status = runStatus;
        runManifest.StatusDetail = outcome.Error ?? outcome.Summary;
        await WriteRunManifestAsync(workspaceManager, workspace, runManifest, ct);

        var workerOutcome = WorkerOutcomeRecord.FromRun(
            run, outcome.Kind, TimestampMs.New((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            outcome.Summary, outcome.Error);

        await WriteJsonArtifactAsync(workspaceManager, workspace, workspace.SessionContextPath(),
            BuildSessionContext(runManifest, run, null, promptKind, null, workerOutcome), ct);

        return new IssueSessionResult
        {
            PromptKind = promptKind,
            Conversation = conversation,
            WorkerOutcome = workerOutcome,
            RunStatus = runStatus.ToSnakeCaseString(),
        };
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task WaitForActiveTurnToFinishAsync(
        RuntimeEventStream stream,
        IIssueSessionObserver observer,
        CancellationToken ct)
    {
        if (stream.StateMirror.ExecutionStatus is not ("running" or "queued"))
            return;

        var deadline = DateTimeOffset.UtcNow + _config.TerminalWaitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (stream.StateMirror.ExecutionStatus is "finished" or "error" or "stuck")
                return;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_config.TerminalWaitTimeout);
            try
            {
                var next = await stream.NextEventAsync(cts.Token);
                if (next.IsErr)
                {
                    if (stream.StateMirror.ExecutionStatus is "finished" or "error" or "stuck" && FinishedStreamErrorIsTolerable(next.Error))
                        return;
                    throw IssueSessionError.OpenHands(next.Error);
                }
                if (next.Value is { } evt)
                    ObserveEvent(observer, evt);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // keep waiting until deadline
            }
        }

        throw IssueSessionError.OpenHands(OpenHandsError.Protocol(
            "wait for active turn to finish",
            $"execution_status `{stream.StateMirror.ExecutionStatus ?? "unknown"}` did not stop within {_config.TerminalWaitTimeout.TotalMilliseconds} ms"));
    }

    private static void AccumulateTokens(ActiveSession session)
    {
        ulong input = 0, output = 0, cacheRead = 0;
        foreach (var evt in session.Stream.EventCache.Items)
        {
            if (KnownEvent.FromEnvelope(evt) is KnownEvent.LlmCompletionLog log && log.Event.TokenUsage() is { } usage)
            {
                input += usage.Input;
                output += usage.Output;
            }
        }
        session.Manifest.InputTokens = Math.Max(session.Manifest.InputTokens, input);
        session.Manifest.OutputTokens = Math.Max(session.Manifest.OutputTokens, output);
        session.Manifest.CacheReadTokens = Math.Max(session.Manifest.CacheReadTokens, cacheRead);
        session.Manifest.LastTokenAccumulationAt = DateTimeOffset.UtcNow;
    }

    private static void UpdateConversationMetadata(IIssueSessionObserver observer, ActiveSession session)
    {
        observer.OnConversationUpdate(session.Manifest.ToDomainMetadata(RuntimeStreamState.Ready));
    }

    private static void ObserveEvent(IIssueSessionObserver observer, EventEnvelope evt)
    {
        var ts = TimestampMs.New((ulong)evt.Timestamp.ToUnixTimeMilliseconds());
        observer.OnRuntimeEvent(ts, evt.Id, evt.Kind, SummarizeEvent(evt), evt.Payload);
    }

    private static void ObserveLatestEvent(IIssueSessionObserver observer, RuntimeEventStream stream)
    {
        if (stream.EventCache.Items.Count > 0)
            ObserveEvent(observer, stream.EventCache.Items[^1]);
    }

    private static string SummarizeEvent(EventEnvelope evt) =>
        KnownEvent.FromEnvelope(evt).ActivitySummary()?.Preview ?? evt.Kind;

    private static string BuildContinuationGuidance(NormalizedIssue issue, RunAttempt run) =>
        $"Continue working on issue {issue.Identifier.Value} ({issue.Title}). This is run attempt {run.Attempt?.Get() ?? 1}. Pick up from where the previous turn left off and continue toward resolving the issue.";

    private static NormalizedOutcome FailedOutcome(string summary, string error) =>
        new(WorkerOutcomeKind.Failed, summary, error);

    private static RunStatus RunStatusFor(WorkerOutcomeKind kind) => kind switch
    {
        WorkerOutcomeKind.Succeeded => RunStatus.Succeeded,
        WorkerOutcomeKind.Cancelled => RunStatus.Cancelled,
        _ => RunStatus.Failed,
    };

    private static string? LatestCurrentTurnError(IReadOnlyList<EventEnvelope> events, HashSet<string> baselineEventIds)
    {
        for (var i = events.Count - 1; i >= 0; i--)
        {
            var evt = events[i];
            if (baselineEventIds.Contains(evt.Id)) continue;
            if (KnownEvent.FromEnvelope(evt) is KnownEvent.ConversationError err)
            {
                if (err.Event.Payload.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString();
                return $"conversation error {evt.Id}";
            }
        }
        return null;
    }

    private static string? ExtractErrorDetailFromState(ConversationStateMirror state)
    {
        var raw = state.RawState;
        if (raw.TryGetProperty("state_delta", out var delta) &&
            delta.TryGetProperty("last_error", out var le) && le.ValueKind == JsonValueKind.String)
            return le.GetString();
        if (raw.TryGetProperty("stats", out var stats) &&
            stats.TryGetProperty("last_error", out var le2) && le2.ValueKind == JsonValueKind.String)
            return le2.GetString();
        if (raw.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            return err.GetString();
        return null;
    }

    private static bool FinishedStreamErrorIsTolerable(OpenHandsError error) =>
        error.ErrorKind is "WebSocketClosed" or "WebSocketTransport";

    private static ConversationMetadata BuildSummaryMetadata(
        Conversation conversation, bool freshConversation, RuntimeStreamState state,
        TransportDiagnostics? diagnostics, string serverBaseUrl) => new()
    {
        ConversationId = conversation.ConversationId,
        ExecutionStatus = conversation.ExecutionStatus,
        FreshConversation = freshConversation,
        StreamState = state,
        ServerBaseUrl = serverBaseUrl,
        TransportTarget = diagnostics?.TargetKind.AsStr(),
        HttpAuthMode = diagnostics?.HttpAuthKind.AsStr(),
        WebsocketAuthMode = diagnostics?.WebsocketAuthKind.AsStr(),
        WebsocketQueryParamName = diagnostics?.WebsocketQueryParamName,
    };

    private static IssueSessionContext BuildSessionContext(
        RunManifest runManifest, RunAttempt run, IssueConversationManifest? manifest,
        IssueSessionPromptKind promptKind, string? promptPath, WorkerOutcomeRecord? workerOutcome) => new()
    {
        RunId = runManifest.RunId,
        IssueId = manifest?.IssueId ?? runManifest.IssueId,
        Identifier = manifest?.Identifier ?? runManifest.Identifier,
        WorkerId = run.WorkerId.Value,
        Attempt = run.Attempt?.Get(),
        NormalRetryCount = run.NormalRetryCount,
        TurnCount = run.TurnCount,
        MaxTurns = run.MaxTurns,
        PromptKind = promptKind,
        PromptPath = promptPath,
        ConversationId = manifest?.ConversationId,
        ReusePolicy = manifest?.ReusePolicy ?? "per_issue",
        FreshConversation = manifest?.FreshConversation ?? true,
        WorkflowPromptSeeded = manifest?.WorkflowPromptSeeded ?? false,
        ServerBaseUrl = manifest?.ServerBaseUrl,
        TransportTarget = manifest?.TransportTarget,
        HttpAuthMode = manifest?.HttpAuthMode,
        WebsocketAuthMode = manifest?.WebsocketAuthMode,
        WebsocketQueryParamName = manifest?.WebsocketQueryParamName,
        PersistenceDir = manifest?.PersistenceDir ?? "",
        LastExecutionStatus = manifest?.LastExecutionStatus,
        LastEventId = manifest?.LastEventId,
        LastEventKind = manifest?.LastEventKind,
        LastEventAt = manifest?.LastEventAt,
        LastEventSummary = manifest?.LastEventSummary,
        WorkerOutcome = workerOutcome,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static string ConfiguredPersistenceDir(ResolvedWorkflow workflow, WorkspaceHandle workspace) =>
        Path.Combine(workspace.WorkspacePath, workflow.Extensions.OpenHands.Conversation.PersistenceDirRelative);

    private static string CreateConversationRequestPath(WorkspaceHandle workspace) =>
        Path.Join(workspace.OpenhandsDir(), "create-conversation-request.json");

    private static string LastConversationStatePath(WorkspaceHandle workspace) =>
        Path.Join(workspace.OpenhandsDir(), "last-conversation-state.json");

    private static async Task WriteManifestAsync<T>(WorkspaceManager m, WorkspaceHandle w, string path, T artifact, CancellationToken ct)
    {
        var result = await m.WriteJsonArtifact(w, path, artifact);
        if (result.IsErr) throw IssueSessionError.Workspace(result.Error);
    }

    private static async Task WriteJsonArtifactAsync<T>(WorkspaceManager m, WorkspaceHandle w, string path, T artifact, CancellationToken ct)
    {
        var result = await m.WriteJsonArtifact(w, path, artifact);
        if (result.IsErr) throw IssueSessionError.Workspace(result.Error);
    }

    private static async Task WriteTextArtifactAsync(WorkspaceManager m, WorkspaceHandle w, string path, string contents, CancellationToken ct)
    {
        var result = await m.WriteTextArtifact(w, path, contents);
        if (result.IsErr) throw IssueSessionError.Workspace(result.Error);
    }

    private static async Task WriteRunManifestAsync(WorkspaceManager m, WorkspaceHandle w, RunManifest manifest, CancellationToken ct)
    {
        var result = await m.FinishRun(w, manifest, manifest.Status);
        if (result.IsErr) throw IssueSessionError.Workspace(result.Error);
    }

    private sealed class ActiveSession
    {
        public IssueSessionPromptKind PromptKind { get; set; }
        public RuntimeEventStream Stream { get; }
        public IssueConversationManifest Manifest { get; }
        public string? PromptPath { get; set; }

        public ActiveSession(IssueSessionPromptKind promptKind, RuntimeEventStream stream, IssueConversationManifest manifest, string? promptPath)
        {
            PromptKind = promptKind;
            Stream = stream;
            Manifest = manifest;
            PromptPath = promptPath;
        }
    }

    private sealed record PreparedTurn(Guid ConversationId, string Prompt, HashSet<string> BaselineEventIds);

    private abstract record TerminalCheckResult
    {
        public sealed record Terminal(NormalizedOutcome Outcome) : TerminalCheckResult;
        public sealed record Progress : TerminalCheckResult;
        public sealed record NoProgress : TerminalCheckResult;
    }

    private sealed record NormalizedOutcome(WorkerOutcomeKind Kind, string Summary, string? Error);
}

internal static class ResultExtensions
{
    public static T? ValueOrNull<T, E>(this Result<T, E> result) where T : class =>
        result.IsOk ? result.Value : null;
}
