using System.Collections;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Workflow;

public static class WorkflowResolver
{
    public static Result<ResolvedWorkflow, WorkflowConfigError> ResolveWorkflow(
        WorkflowDefinition workflow, string baseDir, IEnvironment env)
    {
        var trackerResult = ResolveTracker(workflow.FrontMatter.Tracker, env);
        if (trackerResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(trackerResult.Error);
        var pollingResult = ResolvePolling(workflow.FrontMatter.Polling);
        if (pollingResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(pollingResult.Error);
        var workspaceResult = ResolveWorkspace(workflow.FrontMatter.Workspace, baseDir, env);
        if (workspaceResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(workspaceResult.Error);
        var hooksResult = ResolveHooks(workflow.FrontMatter.Hooks);
        if (hooksResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(hooksResult.Error);
        var agentResult = ResolveAgent(workflow.FrontMatter.Agent);
        if (agentResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(agentResult.Error);
        var routingResult = ResolveRouting(workflow.FrontMatter.Routing, env);
        if (routingResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(routingResult.Error);

        var config = new WorkflowConfig(
            trackerResult.Value, pollingResult.Value, workspaceResult.Value,
            hooksResult.Value, agentResult.Value, routingResult.Value);

        var openhandsResult = ResolveOpenHands(workflow.FrontMatter.OpenHands, baseDir, env);
        if (openhandsResult.IsErr) return Result<ResolvedWorkflow, WorkflowConfigError>.Err(openhandsResult.Error);

        var extensions = new WorkflowExtensions(openhandsResult.Value);
        ApplySelectedModelToOpenHands(config.Routing, ref extensions);

        return Result<ResolvedWorkflow, WorkflowConfigError>.Ok(
            new ResolvedWorkflow(config, extensions, workflow.PromptTemplate));
    }

    private static void ApplySelectedModelToOpenHands(RoutingConfig routing, ref WorkflowExtensions extensions)
    {
        if (routing.Harness != WorkflowConstants.DEFAULT_ROUTING_HARNESS) return;
        if (routing.Model is null) return;

        var openhands = extensions.OpenHands;
        if (openhands.Conversation.Agent.Llm is { } llm)
        {
            var newLlm = llm with { Model = routing.Model };
            var newAgent = openhands.Conversation.Agent with { Llm = newLlm };
            var newConv = openhands.Conversation with { Agent = newAgent };
            extensions = extensions with { OpenHands = openhands with { Conversation = newConv } };
        }
    }

    private static Result<TrackerConfig, WorkflowConfigError> ResolveTracker(TrackerFrontMatter tracker, IEnvironment env)
    {
        var kindStr = NormalizeOptionalLiteral(tracker.Kind);
        TrackerKind kind;
        if (kindStr is string k && k.Equals("linear", StringComparison.OrdinalIgnoreCase))
        {
            kind = TrackerKind.Linear;
        }
        else if (kindStr is null)
        {
            return Result<TrackerConfig, WorkflowConfigError>.Err(new MissingRequiredField("tracker.kind"));
        }
        else
        {
            return Result<TrackerConfig, WorkflowConfigError>.Err(new UnsupportedTrackerKind(kindStr));
        }

        var endpointResult = ResolveStringOrDefault(tracker.Endpoint, env, "tracker.endpoint", WorkflowConstants.DEFAULT_LINEAR_ENDPOINT);
        if (endpointResult.IsErr) return Result<TrackerConfig, WorkflowConfigError>.Err(endpointResult.Error);

        var projectSlugResult = RequireLiteral(tracker.ProjectSlug, "tracker.project_slug");
        if (projectSlugResult.IsErr) return Result<TrackerConfig, WorkflowConfigError>.Err(projectSlugResult.Error);

        var apiKeyResult = ResolveTrackerApiKey(tracker, env);
        if (apiKeyResult.IsErr) return Result<TrackerConfig, WorkflowConfigError>.Err(apiKeyResult.Error);

        var activeResult = ResolveStateList(tracker.ActiveStates, "tracker.active_states");
        if (activeResult.IsErr) return Result<TrackerConfig, WorkflowConfigError>.Err(activeResult.Error);
        var terminalResult = ResolveStateList(tracker.TerminalStates, "tracker.terminal_states");
        if (terminalResult.IsErr) return Result<TrackerConfig, WorkflowConfigError>.Err(terminalResult.Error);

        return Result<TrackerConfig, WorkflowConfigError>.Ok(new TrackerConfig(
            kind, endpointResult.Value, apiKeyResult.Value, projectSlugResult.Value,
            activeResult.Value, terminalResult.Value));
    }

    private static Result<string, WorkflowConfigError> ResolveTrackerApiKey(TrackerFrontMatter tracker, IEnvironment env)
    {
        if (tracker.ApiKey is string configured)
        {
            var literalResult = RequireLiteral(configured, "tracker.api_key");
            if (literalResult.IsErr) return Result<string, WorkflowConfigError>.Err(literalResult.Error);
            return ResolveString(literalResult.Value, env, "tracker.api_key");
        }

        var envVal = NormalizeOptionalOwned(env.Get("LINEAR_API_KEY"));
        if (envVal is null)
        {
            return Result<string, WorkflowConfigError>.Err(new MissingRequiredField("tracker.api_key"));
        }
        return Result<string, WorkflowConfigError>.Ok(envVal);
    }

    private static Result<PollingConfig, WorkflowConfigError> ResolvePolling(PollingFrontMatter polling)
    {
        var intervalResult = ResolvePositiveU64(polling.IntervalMs, "polling.interval_ms", WorkflowConstants.DEFAULT_POLL_INTERVAL_MS);
        if (intervalResult.IsErr) return Result<PollingConfig, WorkflowConfigError>.Err(intervalResult.Error);
        return Result<PollingConfig, WorkflowConfigError>.Ok(new PollingConfig(intervalResult.Value));
    }

    private static Result<WorkspaceConfig, WorkflowConfigError> ResolveWorkspace(WorkspaceFrontMatter workspace, string baseDir, IEnvironment env)
    {
        var rootValue = workspace.Root ?? WorkflowConstants.DEFAULT_WORKSPACE_ROOT;
        var rootResult = ResolveWorkspaceRoot(rootValue, baseDir, env);
        if (rootResult.IsErr) return Result<WorkspaceConfig, WorkflowConfigError>.Err(rootResult.Error);
        return Result<WorkspaceConfig, WorkflowConfigError>.Ok(new WorkspaceConfig(rootResult.Value));
    }

    private static Result<HooksConfig, WorkflowConfigError> ResolveHooks(HooksFrontMatter hooks)
    {
        var timeoutResult = ResolveNonPositiveToDefault(hooks.TimeoutMs, "hooks.timeout_ms", WorkflowConstants.DEFAULT_HOOK_TIMEOUT_MS);
        if (timeoutResult.IsErr) return Result<HooksConfig, WorkflowConfigError>.Err(timeoutResult.Error);
        return Result<HooksConfig, WorkflowConfigError>.Ok(new HooksConfig(
            hooks.AfterCreate, hooks.BeforeRun, hooks.AfterRun, hooks.BeforeRemove, timeoutResult.Value));
    }

    private static Result<AgentConfig, WorkflowConfigError> ResolveAgent(AgentFrontMatter agent)
    {
        var maxConcurrentResult = ResolvePositiveU64(agent.MaxConcurrentAgents, "agent.max_concurrent_agents", WorkflowConstants.DEFAULT_MAX_CONCURRENT_AGENTS);
        if (maxConcurrentResult.IsErr) return Result<AgentConfig, WorkflowConfigError>.Err(maxConcurrentResult.Error);
        var maxTurnsResult = ResolvePositiveU64(agent.MaxTurns, "agent.max_turns", WorkflowConstants.DEFAULT_MAX_TURNS);
        if (maxTurnsResult.IsErr) return Result<AgentConfig, WorkflowConfigError>.Err(maxTurnsResult.Error);
        var maxRetryResult = ResolvePositiveU64(agent.MaxRetryBackoffMs, "agent.max_retry_backoff_ms", WorkflowConstants.DEFAULT_MAX_RETRY_BACKOFF_MS);
        if (maxRetryResult.IsErr) return Result<AgentConfig, WorkflowConfigError>.Err(maxRetryResult.Error);
        var stallResult = ResolveStallTimeout(agent.StallTimeoutMs);
        if (stallResult.IsErr) return Result<AgentConfig, WorkflowConfigError>.Err(stallResult.Error);
        var stateLimitsResult = ResolveStateLimits(agent.MaxConcurrentAgentsByState);
        if (stateLimitsResult.IsErr) return Result<AgentConfig, WorkflowConfigError>.Err(stateLimitsResult.Error);

        return Result<AgentConfig, WorkflowConfigError>.Ok(new AgentConfig(
            maxConcurrentResult.Value, maxTurnsResult.Value, maxRetryResult.Value,
            stallResult.Value, stateLimitsResult.Value));
    }

    private static Result<RoutingConfig, WorkflowConfigError> ResolveRouting(RoutingFrontMatter routing, IEnvironment env)
    {
        var harnessEnvResult = ResolveStringOrDefault(routing.HarnessEnv, env, "routing.harness_env", WorkflowConstants.DEFAULT_ROUTING_HARNESS_ENV);
        if (harnessEnvResult.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(harnessEnvResult.Error);
        var harnessEnv = harnessEnvResult.Value;
        var validateHarnessEnv = ValidateEnvName(harnessEnv, "routing.harness_env");
        if (validateHarnessEnv.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(validateHarnessEnv.Error);

        var modelEnvResult = ResolveStringOrDefault(routing.ModelEnv, env, "routing.model_env", WorkflowConstants.DEFAULT_ROUTING_MODEL_ENV);
        if (modelEnvResult.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(modelEnvResult.Error);
        var modelEnv = modelEnvResult.Value;
        var validateModelEnv = ValidateEnvName(modelEnv, "routing.model_env");
        if (validateModelEnv.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(validateModelEnv.Error);

        var modelProfileEnvResult = ResolveStringOrDefault(routing.ModelProfileEnv, env, "routing.model_profile_env", WorkflowConstants.DEFAULT_ROUTING_MODEL_PROFILE_ENV);
        if (modelProfileEnvResult.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(modelProfileEnvResult.Error);
        var modelProfileEnv = modelProfileEnvResult.Value;
        var validateModelProfileEnv = ValidateEnvName(modelProfileEnv, "routing.model_profile_env");
        if (validateModelProfileEnv.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(validateModelProfileEnv.Error);

        var configuredHarnessResult = ResolveStringOrDefault(routing.Harness, env, "routing.harness", WorkflowConstants.DEFAULT_ROUTING_HARNESS);
        if (configuredHarnessResult.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(configuredHarnessResult.Error);
        var harnessOverride = NormalizeOptionalOwned(env.Get(harnessEnv));
        var harnessFromEnv = harnessOverride is not null;
        var harness = harnessOverride ?? configuredHarnessResult.Value;
        var validateHarness = ValidateHarnessKind(harness, "routing.harness");
        if (validateHarness.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(validateHarness.Error);

        string? configuredModel = null;
        if (routing.Model is string modelVal)
        {
            var modelResult = ResolveString(modelVal, env, "routing.model");
            if (modelResult.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(modelResult.Error);
            configuredModel = NormalizeOptionalOwned(modelResult.Value);
        }
        var modelOverride = NormalizeOptionalOwned(env.Get(modelEnv));
        var modelFromEnv = modelOverride is not null;
        var model = modelOverride ?? configuredModel;

        string? configuredModelProfile = null;
        if (routing.ModelProfile is string profileVal)
        {
            var profileResult = ResolveString(profileVal, env, "routing.model_profile");
            if (profileResult.IsErr) return Result<RoutingConfig, WorkflowConfigError>.Err(profileResult.Error);
            configuredModelProfile = NormalizeOptionalOwned(profileResult.Value);
        }
        var modelProfileOverride = NormalizeOptionalOwned(env.Get(modelProfileEnv));
        var modelProfileFromEnv = modelProfileOverride is not null;
        var modelProfile = modelProfileOverride ?? configuredModelProfile;

        return Result<RoutingConfig, WorkflowConfigError>.Ok(new RoutingConfig(
            harness, model, modelProfile, harnessEnv, modelEnv, modelProfileEnv,
            harnessFromEnv, modelFromEnv, modelProfileFromEnv, false));
    }

    private static Result<Unit, WorkflowConfigError> ValidateHarnessKind(string value, string field)
    {
        if (HarnessKindExtensions.Parse(value) is not null)
        {
            return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
        }
        return Result<Unit, WorkflowConfigError>.Err(new InvalidField(field,
            $"must be one of `{string.Join("`, `", HarnessKindExtensions.SupportedNames())}`"));
    }

    private static Result<Unit, WorkflowConfigError> ValidateEnvName(string value, string field)
    {
        bool valid = !string.IsNullOrEmpty(value)
            && value.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')
            && (value[0] == '_' || char.IsAsciiLetterUpper(value[0]));
        if (valid) return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
        return Result<Unit, WorkflowConfigError>.Err(new InvalidField(field,
            "must be an environment variable name such as OPENSYMPHONY_HARNESS"));
    }

    private static Result<ulong?, WorkflowConfigError> ResolveStallTimeout(IntegerLike? stallTimeoutMs)
    {
        if (stallTimeoutMs is null)
        {
            return Result<ulong?, WorkflowConfigError>.Ok(WorkflowConstants.DEFAULT_STALL_TIMEOUT_MS);
        }
        var parsedResult = ParseI64(stallTimeoutMs, "agent.stall_timeout_ms");
        if (parsedResult.IsErr) return Result<ulong?, WorkflowConfigError>.Err(parsedResult.Error);
        var parsed = parsedResult.Value;
        if (parsed <= 0) return Result<ulong?, WorkflowConfigError>.Ok(null);
        return Result<ulong?, WorkflowConfigError>.Ok((ulong)parsed);
    }

    private static Result<List<string>, WorkflowConfigError> ResolveStateList(List<string>? raw, string field)
    {
        if (raw is null)
        {
            return Result<List<string>, WorkflowConfigError>.Err(new MissingRequiredField(field));
        }
        if (raw.Count == 0)
        {
            return Result<List<string>, WorkflowConfigError>.Err(new InvalidField(field, "must contain at least one state"));
        }
        var result = new List<string>();
        foreach (var state in raw)
        {
            var normalized = NormalizeOptional(state);
            if (normalized is null)
            {
                return Result<List<string>, WorkflowConfigError>.Err(new InvalidField(field, "state names must not be empty"));
            }
            result.Add(normalized);
        }
        return Result<List<string>, WorkflowConfigError>.Ok(result);
    }

    private static Result<SortedDictionary<string, ulong>, WorkflowConfigError> ResolveStateLimits(
        SortedDictionary<string, IntegerLike>? raw)
    {
        var resolved = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
        if (raw is null) return Result<SortedDictionary<string, ulong>, WorkflowConfigError>.Ok(resolved);

        foreach (var (state, value) in raw)
        {
            var normalizedState = NormalizeOptional(state);
            if (normalizedState is null)
            {
                return Result<SortedDictionary<string, ulong>, WorkflowConfigError>.Err(
                    new InvalidField("agent.max_concurrent_agents_by_state", "state names must not be empty"));
            }
            var parsedResult = ParseI64(value, "agent.max_concurrent_agents_by_state");
            if (parsedResult.IsErr) return Result<SortedDictionary<string, ulong>, WorkflowConfigError>.Err(parsedResult.Error);
            if (parsedResult.Value <= 0)
            {
                return Result<SortedDictionary<string, ulong>, WorkflowConfigError>.Err(
                    new InvalidField("agent.max_concurrent_agents_by_state", "state limits must be greater than zero"));
            }
            resolved[normalizedState.ToLowerInvariant()] = (ulong)parsedResult.Value;
        }
        return Result<SortedDictionary<string, ulong>, WorkflowConfigError>.Ok(resolved);
    }

    private static Result<OpenHandsConfig, WorkflowConfigError> ResolveOpenHands(
        OpenHandsFrontMatter openhands, string baseDir, IEnvironment env)
    {
        var rejectBridge = RejectRemovedLegacyLinearBridgeConfig(openhands.LegacyLinearBridge);
        if (rejectBridge.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(rejectBridge.Error);
        var rejectLocal = RejectUnsupportedOpenHandsLocalServerOverrides(openhands.LocalServer);
        if (rejectLocal.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(rejectLocal.Error);
        var rejectWs = RejectUnsupportedOpenHandsWebsocketOverrides(openhands.Websocket);
        if (rejectWs.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(rejectWs.Error);

        var baseUrlResult = ResolveOpenHandsBaseUrl(openhands.Transport.BaseUrl, env);
        if (baseUrlResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(baseUrlResult.Error);
        var transportBaseUrl = baseUrlResult.Value;

        var sessionApiKeyEnv = NormalizeOptionalLiteral(openhands.Transport.SessionApiKeyEnv);

        var wsAuthModeResult = ResolveStringOrDefault(openhands.Websocket.AuthMode, env, "openhands.websocket.auth_mode", WorkflowConstants.DEFAULT_OPENHANDS_AUTH_MODE);
        if (wsAuthModeResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(wsAuthModeResult.Error);
        var websocketAuthMode = wsAuthModeResult.Value;
        var validateAuthMode = ValidateOpenHandsWebsocketAuthMode(websocketAuthMode);
        if (validateAuthMode.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(validateAuthMode.Error);

        var wsQueryParamResult = ResolveStringOrDefault(openhands.Websocket.QueryParamName, env, "openhands.websocket.query_param_name", WorkflowConstants.DEFAULT_OPENHANDS_QUERY_PARAM_NAME);
        if (wsQueryParamResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(wsQueryParamResult.Error);

        var readyTimeoutResult = ResolvePositiveU64(openhands.Websocket.ReadyTimeoutMs, "openhands.websocket.ready_timeout_ms", WorkflowConstants.DEFAULT_OPENHANDS_READY_TIMEOUT_MS);
        if (readyTimeoutResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(readyTimeoutResult.Error);
        var reconnectInitialResult = ResolvePositiveU64(openhands.Websocket.ReconnectInitialMs, "openhands.websocket.reconnect_initial_ms", WorkflowConstants.DEFAULT_OPENHANDS_RECONNECT_INITIAL_MS);
        if (reconnectInitialResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(reconnectInitialResult.Error);
        var reconnectMaxResult = ResolvePositiveU64(openhands.Websocket.ReconnectMaxMs, "openhands.websocket.reconnect_max_ms", WorkflowConstants.DEFAULT_OPENHANDS_RECONNECT_MAX_MS);
        if (reconnectMaxResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(reconnectMaxResult.Error);

        var websocket = new OpenHandsWebSocketConfig(
            openhands.Websocket.Enabled ?? true,
            readyTimeoutResult.Value, reconnectInitialResult.Value, reconnectMaxResult.Value,
            websocketAuthMode, wsQueryParamResult.Value);

        var validateRemote = ValidateRemoteOpenHandsTransportRequirements(transportBaseUrl, sessionApiKeyEnv, websocket);
        if (validateRemote.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(validateRemote.Error);

        // local_server
        List<string>? command = null;
        if (openhands.LocalServer.Command is { } configuredCmd)
        {
            var cmdResult = ResolveCommand(configuredCmd, "openhands.local_server.command", new List<string>());
            if (cmdResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(cmdResult.Error);
            command = cmdResult.Value;
        }
        var startupTimeoutResult = ResolvePositiveU64(openhands.LocalServer.StartupTimeoutMs, "openhands.local_server.startup_timeout_ms", WorkflowConstants.DEFAULT_OPENHANDS_STARTUP_TIMEOUT_MS);
        if (startupTimeoutResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(startupTimeoutResult.Error);
        var readinessProbeResult = ResolveStringOrDefault(openhands.LocalServer.ReadinessProbePath, env, "openhands.local_server.readiness_probe_path", WorkflowConstants.DEFAULT_OPENHANDS_READINESS_PROBE_PATH);
        if (readinessProbeResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(readinessProbeResult.Error);
        var envMapResult = ResolveStringMap(openhands.LocalServer.Env, env, "openhands.local_server.env");
        if (envMapResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(envMapResult.Error);

        var localServer = new OpenHandsLocalServerConfig(
            openhands.LocalServer.Enabled ?? true,
            command, startupTimeoutResult.Value, readinessProbeResult.Value, envMapResult.Value);

        // conversation
        var conversationResult = ResolveOpenHandsConversation(openhands.Conversation, env);
        if (conversationResult.IsErr) return Result<OpenHandsConfig, WorkflowConfigError>.Err(conversationResult.Error);

        return Result<OpenHandsConfig, WorkflowConfigError>.Ok(new OpenHandsConfig(
            new OpenHandsTransportConfig(transportBaseUrl, sessionApiKeyEnv),
            localServer,
            conversationResult.Value,
            websocket));
    }

    private static Result<Unit, WorkflowConfigError> RejectRemovedLegacyLinearBridgeConfig(object? legacyLinearBridge)
    {
        if (legacyLinearBridge is not null)
        {
            return Result<Unit, WorkflowConfigError>.Err(new RemovedField("openhands.mcp",
                "Legacy Linear bridge configuration at `openhands.mcp` was removed in OpenSymphony 1.0.0. Use GraphQL-only Linear access through `LINEAR_API_KEY` and the repo-local `linear` skill assets instead."));
        }
        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<Unit, WorkflowConfigError> RejectUnsupportedOpenHandsLocalServerOverrides(OpenHandsLocalServerFrontMatter localServer)
    {
        if (localServer.Enabled is false)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.local_server.enabled",
                "is not supported until the runtime supervisor can honor workflow-owned local-server disablement"));
        }
        if (localServer.StartupTimeoutMs is not null)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.local_server.startup_timeout_ms",
                "is not supported until the runtime supervisor creation path consumes workflow-owned startup timeouts"));
        }
        if (localServer.ReadinessProbePath is not null)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.local_server.readiness_probe_path",
                "is not supported until the runtime supervisor launch path consumes workflow-owned readiness probe settings"));
        }
        if (localServer.Env.Count > 0)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.local_server.env",
                "is not supported until the runtime supervisor creation path forwards workflow-owned launcher environment overrides"));
        }
        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<Unit, WorkflowConfigError> RejectUnsupportedOpenHandsWebsocketOverrides(OpenHandsWebSocketFrontMatter websocket)
    {
        if (websocket.Enabled is not null)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.websocket.enabled",
                "is not supported until the runtime readiness path can honor workflow-owned websocket enablement"));
        }
        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<string, WorkflowConfigError> ResolveOpenHandsBaseUrl(string? configured, IEnvironment env)
    {
        var baseUrlResult = ResolveStringOrDefault(configured, env, "openhands.transport.base_url", WorkflowConstants.DEFAULT_OPENHANDS_BASE_URL);
        if (baseUrlResult.IsErr) return Result<string, WorkflowConfigError>.Err(baseUrlResult.Error);
        var validate = ValidateOpenHandsBaseUrl(baseUrlResult.Value);
        if (validate.IsErr) return Result<string, WorkflowConfigError>.Err(validate.Error);
        return Result<string, WorkflowConfigError>.Ok(baseUrlResult.Value);
    }

    private static Result<Unit, WorkflowConfigError> ValidateOpenHandsBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must be an absolute http or https URL"));
        }

        if (parsed.Scheme != "http" && parsed.Scheme != "https")
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must use the http or https scheme"));
        }

        // ht: IPv6 detection — Uri.HostNameType == IPv6 means bracketed IPv6 host.
        if (parsed.HostNameType == UriHostNameType.IPv6)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must not use bracketed IPv6 hosts until supervisor readiness probes support them"));
        }

        if (string.IsNullOrEmpty(parsed.Host))
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must include a host"));
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must not embed credentials"));
        }

        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must not include query or fragment suffixes"));
        }

        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<Unit, WorkflowConfigError> ValidateOpenHandsWebsocketAuthMode(string authMode)
    {
        var normalized = authMode.Trim().ToLowerInvariant();
        if (normalized == "auto" || normalized == "header" || normalized == "query_param")
        {
            return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
        }
        return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.websocket.auth_mode",
            "must be one of `auto`, `header`, or `query_param`"));
    }

    private static Result<Unit, WorkflowConfigError> ValidateRemoteOpenHandsTransportRequirements(
        string baseUrl, string? sessionApiKeyEnv, OpenHandsWebSocketConfig websocket)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must be an absolute http or https URL"));
        }

        bool loopbackTarget = false;
        if (parsed.HostNameType == UriHostNameType.IPv4 && IPAddress.TryParse(parsed.Host, out var ip4))
        {
            loopbackTarget = IPAddress.IsLoopback(ip4);
        }
        else if (parsed.HostNameType == UriHostNameType.IPv6 && IPAddress.TryParse(parsed.Host, out var ip6))
        {
            loopbackTarget = IPAddress.IsLoopback(ip6);
        }
        else if (parsed.HostNameType == UriHostNameType.Dns && parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            loopbackTarget = true;
        }

        if (!loopbackTarget && parsed.Scheme != "https")
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.base_url",
                "must use https for non-loopback remote agent-server targets"));
        }

        if (!loopbackTarget && sessionApiKeyEnv is null)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.transport.session_api_key_env",
                "is required for non-loopback remote agent-server targets"));
        }

        if (sessionApiKeyEnv is null && websocket.AuthMode != WorkflowConstants.DEFAULT_OPENHANDS_AUTH_MODE)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField("openhands.websocket.auth_mode",
                "requires `openhands.transport.session_api_key_env`"));
        }

        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<OpenHandsConversationConfig, WorkflowConfigError> ResolveOpenHandsConversation(
        OpenHandsConversationFrontMatter conversation, IEnvironment env)
    {
        var reusePolicyResult = ResolveOpenHandsReusePolicy(conversation.ReusePolicy, env);
        if (reusePolicyResult.IsErr) return Result<OpenHandsConversationConfig, WorkflowConfigError>.Err(reusePolicyResult.Error);

        OpenHandsConfirmationPolicy confirmationPolicy;
        if (conversation.ConfirmationPolicy is { } policy)
        {
            var policyResult = ResolveOpenHandsConfirmationPolicy(policy);
            if (policyResult.IsErr) return Result<OpenHandsConversationConfig, WorkflowConfigError>.Err(policyResult.Error);
            confirmationPolicy = policyResult.Value;
        }
        else
        {
            confirmationPolicy = new OpenHandsConfirmationPolicy { Kind = WorkflowConstants.DEFAULT_OPENHANDS_CONFIRMATION_POLICY_KIND };
        }

        OpenHandsConversationAgentConfig agent;
        if (conversation.Agent is { } agentFm)
        {
            var agentResult = ResolveOpenHandsAgent(agentFm, env);
            if (agentResult.IsErr) return Result<OpenHandsConversationConfig, WorkflowConfigError>.Err(agentResult.Error);
            agent = agentResult.Value;
        }
        else
        {
            agent = new OpenHandsConversationAgentConfig(
                WorkflowConstants.DEFAULT_OPENHANDS_AGENT_KIND,
                DefaultOpenHandsLlmConfig(),
                new OpenHandsConversationCondenserConfig(
                    WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE,
                    WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST),
                DefaultOpenHandsAgentTools(),
                null, false, new SortedDictionary<string, object?>(StringComparer.Ordinal));
        }

        if (string.IsNullOrWhiteSpace(agent.Kind))
        {
            return Result<OpenHandsConversationConfig, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.kind", "must not be empty"));
        }

        var persistenceResult = ResolveRelativePath(
            conversation.PersistenceDirRelative, env,
            "openhands.conversation.persistence_dir_relative", WorkflowConstants.DEFAULT_OPENHANDS_PERSISTENCE_DIR);
        if (persistenceResult.IsErr) return Result<OpenHandsConversationConfig, WorkflowConfigError>.Err(persistenceResult.Error);

        var maxIterationsResult = ResolveOpenHandsMaxIterations(conversation.MaxIterations);
        if (maxIterationsResult.IsErr) return Result<OpenHandsConversationConfig, WorkflowConfigError>.Err(maxIterationsResult.Error);

        return Result<OpenHandsConversationConfig, WorkflowConfigError>.Ok(new OpenHandsConversationConfig(
            reusePolicyResult.Value, persistenceResult.Value, maxIterationsResult.Value,
            conversation.StuckDetection ?? true, confirmationPolicy, agent));
    }

    private static Result<OpenHandsConfirmationPolicy, WorkflowConfigError> ResolveOpenHandsConfirmationPolicy(
        OpenHandsConfirmationPolicyFrontMatter policy)
    {
        if (policy.Options.Count > 0)
        {
            var unsupported = string.Join(", ", policy.Options.Keys);
            return Result<OpenHandsConfirmationPolicy, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.confirmation_policy",
                $"unsupported options cannot be forwarded to the current OpenHands request subset: {unsupported}"));
        }

        string kind;
        if (policy.Kind is string kindStr)
        {
            var normalized = NormalizeOptional(kindStr);
            if (normalized is null)
            {
                return Result<OpenHandsConfirmationPolicy, WorkflowConfigError>.Err(new InvalidField(
                    "openhands.conversation.confirmation_policy.kind", "must not be empty"));
            }
            kind = normalized;
        }
        else
        {
            kind = WorkflowConstants.DEFAULT_OPENHANDS_CONFIRMATION_POLICY_KIND;
        }

        return Result<OpenHandsConfirmationPolicy, WorkflowConfigError>.Ok(new OpenHandsConfirmationPolicy { Kind = kind });
    }

    private static Result<OpenHandsConversationAgentConfig, WorkflowConfigError> ResolveOpenHandsAgent(
        OpenHandsConversationAgentFrontMatter agent, IEnvironment env)
    {
        var rejectResult = RejectUnsupportedOpenHandsAgentOptions(agent);
        if (rejectResult.IsErr) return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Err(rejectResult.Error);

        string kind;
        if (agent.Kind is string kindStr)
        {
            var normalized = NormalizeOptional(kindStr);
            if (normalized is null)
            {
                return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Err(new InvalidField(
                    "openhands.conversation.agent.kind", "must not be empty"));
            }
            kind = normalized;
        }
        else
        {
            kind = WorkflowConstants.DEFAULT_OPENHANDS_AGENT_KIND;
        }

        OpenHandsLlmConfig? llm;
        if (agent.Llm is { } llmFm)
        {
            var llmResult = ResolveOpenHandsLlm(llmFm, env);
            if (llmResult.IsErr) return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Err(llmResult.Error);
            llm = llmResult.Value;
        }
        else
        {
            llm = DefaultOpenHandsLlmConfig();
        }

        var condenserResult = ResolveOpenHandsCondenser(agent.Condenser);
        if (condenserResult.IsErr) return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Err(condenserResult.Error);

        List<OpenHandsConversationToolConfig>? tools;
        if (agent.Tools is { } toolsFm)
        {
            var toolsResult = ResolveOpenHandsAgentTools(toolsFm, env);
            if (toolsResult.IsErr) return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Err(toolsResult.Error);
            tools = toolsResult.Value;
        }
        else
        {
            tools = DefaultOpenHandsAgentTools();
        }

        List<string>? includeDefaultTools = null;
        if (agent.IncludeDefaultTools is { } defaultTools)
        {
            var defaultToolsResult = ResolveOpenHandsDefaultTools(defaultTools, env);
            if (defaultToolsResult.IsErr) return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Err(defaultToolsResult.Error);
            includeDefaultTools = defaultToolsResult.Value;
        }

        return Result<OpenHandsConversationAgentConfig, WorkflowConfigError>.Ok(new OpenHandsConversationAgentConfig(
            kind, llm, condenserResult.Value, tools, includeDefaultTools, false,
            new SortedDictionary<string, object?>(StringComparer.Ordinal)));
    }

    private static Result<List<OpenHandsConversationToolConfig>, WorkflowConfigError> ResolveOpenHandsAgentTools(
        List<OpenHandsConversationToolFrontMatter> tools, IEnvironment env)
    {
        var result = new List<OpenHandsConversationToolConfig>();
        for (int i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            var nameField = "openhands.conversation.agent.tools[].name";
            var nameResult = ResolveString(tool.Name, env, nameField);
            if (nameResult.IsErr) return Result<List<OpenHandsConversationToolConfig>, WorkflowConfigError>.Err(nameResult.Error);
            var name = NormalizeOptionalOwned(nameResult.Value);
            if (name is null)
            {
                return Result<List<OpenHandsConversationToolConfig>, WorkflowConfigError>.Err(new InvalidField(
                    nameField, $"entry {i} must not be empty"));
            }
            result.Add(new OpenHandsConversationToolConfig(name, tool.Params));
        }
        return Result<List<OpenHandsConversationToolConfig>, WorkflowConfigError>.Ok(result);
    }

    private static Result<List<string>, WorkflowConfigError> ResolveOpenHandsDefaultTools(
        List<string> tools, IEnvironment env)
    {
        var result = new List<string>();
        for (int i = 0; i < tools.Count; i++)
        {
            var field = "openhands.conversation.agent.include_default_tools[]";
            var resolvedResult = ResolveString(tools[i], env, field);
            if (resolvedResult.IsErr) return Result<List<string>, WorkflowConfigError>.Err(resolvedResult.Error);
            var normalized = NormalizeOptionalOwned(resolvedResult.Value);
            if (normalized is null)
            {
                return Result<List<string>, WorkflowConfigError>.Err(new InvalidField(field, $"entry {i} must not be empty"));
            }
            result.Add(normalized);
        }
        return Result<List<string>, WorkflowConfigError>.Ok(result);
    }

    private static List<OpenHandsConversationToolConfig> DefaultOpenHandsAgentTools() =>
        WorkflowConstants.DEFAULT_OPENHANDS_AGENT_TOOLS
            .Select(name => new OpenHandsConversationToolConfig(name, new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)))
            .ToList();

    private static OpenHandsLlmConfig DefaultOpenHandsLlmConfig() => new(
        WorkflowConstants.DEFAULT_OPENHANDS_LLM_MODEL, null, null,
        WorkflowConstants.OPENHANDS_LLM_CREDENTIAL_MODE_API_KEY, null,
        new SortedDictionary<string, object?>(StringComparer.Ordinal));

    private static Result<OpenHandsConversationCondenserConfig?, WorkflowConfigError> ResolveOpenHandsCondenser(
        OpenHandsConversationCondenserFrontMatter? condenser)
    {
        if (condenser is null)
        {
            return Result<OpenHandsConversationCondenserConfig?, WorkflowConfigError>.Ok(
                new OpenHandsConversationCondenserConfig(
                    WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE,
                    WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST));
        }

        if (condenser.Enabled is false)
        {
            return Result<OpenHandsConversationCondenserConfig?, WorkflowConfigError>.Ok(null);
        }

        var maxSizeResult = ResolvePositiveU64(condenser.MaxSize, "openhands.conversation.agent.condenser.max_size", WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE);
        if (maxSizeResult.IsErr) return Result<OpenHandsConversationCondenserConfig?, WorkflowConfigError>.Err(maxSizeResult.Error);
        var keepFirstResult = ResolvePositiveU64(condenser.KeepFirst, "openhands.conversation.agent.condenser.keep_first", WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST);
        if (keepFirstResult.IsErr) return Result<OpenHandsConversationCondenserConfig?, WorkflowConfigError>.Err(keepFirstResult.Error);

        return Result<OpenHandsConversationCondenserConfig?, WorkflowConfigError>.Ok(
            new OpenHandsConversationCondenserConfig(maxSizeResult.Value, keepFirstResult.Value));
    }

    private static Result<string, WorkflowConfigError> ResolveOpenHandsReusePolicy(string? configured, IEnvironment env)
    {
        var reusePolicyResult = ResolveStringOrDefault(configured, env, "openhands.conversation.reuse_policy", "per_issue");
        if (reusePolicyResult.IsErr) return Result<string, WorkflowConfigError>.Err(reusePolicyResult.Error);
        var normalized = NormalizeOptional(reusePolicyResult.Value);
        if (normalized is null)
        {
            return Result<string, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.reuse_policy", "must not be empty"));
        }
        return Result<string, WorkflowConfigError>.Ok(normalized.ToLowerInvariant());
    }

    private static Result<Unit, WorkflowConfigError> RejectUnsupportedOpenHandsAgentOptions(
        OpenHandsConversationAgentFrontMatter agent)
    {
        if (agent.LogCompletions is not null)
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.log_completions",
                "is not supported until the runtime conversation-create adapter can forward agent logging options"));
        }
        if (agent.Options.Count > 0)
        {
            var unsupported = string.Join(", ", agent.Options.Keys);
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent",
                $"unsupported options cannot be forwarded to the current OpenHands agent request subset: {unsupported}"));
        }
        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<OpenHandsLlmConfig, WorkflowConfigError> ResolveOpenHandsLlm(
        OpenHandsLlmFrontMatter llm, IEnvironment env)
    {
        var rejectResult = RejectUnsupportedOpenHandsLlmOptions(llm);
        if (rejectResult.IsErr) return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(rejectResult.Error);

        var field = "openhands.conversation.agent.llm.model";
        if (llm.Model is null)
        {
            return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(new MissingRequiredField(field));
        }
        var modelResult = ResolveString(llm.Model, env, field);
        if (modelResult.IsErr) return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(modelResult.Error);
        var model = modelResult.Value;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(new InvalidField(field, "must not be empty"));
        }

        var credentialModeResult = ResolveStringOrDefault(llm.CredentialMode, env, "openhands.conversation.agent.llm.credential_mode", WorkflowConstants.DEFAULT_OPENHANDS_LLM_CREDENTIAL_MODE);
        if (credentialModeResult.IsErr) return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(credentialModeResult.Error);
        var normalizeCredResult = NormalizeOpenHandsLlmCredentialMode(credentialModeResult.Value);
        if (normalizeCredResult.IsErr) return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(normalizeCredResult.Error);
        var credentialMode = normalizeCredResult.Value;

        OpenHandsSubscriptionCredentialConfig? subscription;
        if (credentialMode == WorkflowConstants.OPENHANDS_LLM_CREDENTIAL_MODE_API_KEY)
        {
            if (llm.Subscription is not null)
            {
                return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(new InvalidField(
                    "openhands.conversation.agent.llm.subscription",
                    "is only valid when credential_mode is `openai_subscription`"));
            }
            subscription = null;
        }
        else // openai_subscription
        {
            var subResult = ResolveOpenHandsSubscriptionCredential(llm.Subscription, env);
            if (subResult.IsErr) return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(subResult.Error);
            subscription = subResult.Value;
        }

        if (subscription is not null && (llm.ApiKeyEnv is not null || llm.BaseUrlEnv is not null))
        {
            return Result<OpenHandsLlmConfig, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm",
                "`api_key_env` and `base_url_env` are API-key settings; use `subscription.access_token_env` for subscription credentials"));
        }

        return Result<OpenHandsLlmConfig, WorkflowConfigError>.Ok(new OpenHandsLlmConfig(
            model, NormalizeOptionalLiteral(llm.ApiKeyEnv), NormalizeOptionalLiteral(llm.BaseUrlEnv),
            credentialMode, subscription, llm.Options));
    }

    private static Result<string, WorkflowConfigError> NormalizeOpenHandsLlmCredentialMode(string value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return Result<string, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm.credential_mode", "must not be empty"));
        }
        normalized = normalized.ToLowerInvariant();

        if (normalized == WorkflowConstants.OPENHANDS_LLM_CREDENTIAL_MODE_API_KEY)
        {
            return Result<string, WorkflowConfigError>.Ok(WorkflowConstants.OPENHANDS_LLM_CREDENTIAL_MODE_API_KEY);
        }
        if (normalized == "subscription" || normalized == "openai" || normalized == WorkflowConstants.OPENHANDS_LLM_CREDENTIAL_MODE_OPENAI_SUBSCRIPTION)
        {
            // ht: subscription feature is enabled in this port (no feature gating).
            return Result<string, WorkflowConfigError>.Ok(WorkflowConstants.OPENHANDS_LLM_CREDENTIAL_MODE_OPENAI_SUBSCRIPTION);
        }
        return Result<string, WorkflowConfigError>.Err(new InvalidField(
            "openhands.conversation.agent.llm.credential_mode",
            $"unsupported credential mode `{normalized}`; supported values are `api_key` and `openai_subscription`"));
    }

    private static Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError> ResolveOpenHandsSubscriptionCredential(
        OpenHandsSubscriptionCredentialFrontMatter? subscription, IEnvironment env)
    {
        if (subscription is null)
        {
            return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(
                new MissingRequiredField("openhands.conversation.agent.llm.subscription"));
        }

        var vendorResult = ResolveStringOrDefault(subscription.Vendor, env, "openhands.conversation.agent.llm.subscription.vendor", "openai");
        if (vendorResult.IsErr) return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(vendorResult.Error);
        var vendor = NormalizeOptional(vendorResult.Value);
        if (vendor is null)
        {
            return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm.subscription.vendor", "must not be empty"));
        }
        vendor = vendor.ToLowerInvariant();
        if (vendor != "openai")
        {
            return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm.subscription.vendor",
                "only `openai` subscription credentials are supported"));
        }

        var accessTokenEnv = NormalizeOptionalLiteral(subscription.AccessTokenEnv);
        if (accessTokenEnv is null)
        {
            return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(
                new MissingRequiredField("openhands.conversation.agent.llm.subscription.access_token_env"));
        }
        var validateToken = ValidateEnvironmentName(accessTokenEnv, "openhands.conversation.agent.llm.subscription.access_token_env");
        if (validateToken.IsErr) return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(validateToken.Error);

        var accountIdEnv = NormalizeOptionalLiteral(subscription.AccountIdEnv);
        if (accountIdEnv is string aid)
        {
            var validateAid = ValidateEnvironmentName(aid, "openhands.conversation.agent.llm.subscription.account_id_env");
            if (validateAid.IsErr) return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(validateAid.Error);
        }

        var authDirectoryEnv = NormalizeOptionalLiteral(subscription.AuthDirectoryEnv);
        if (authDirectoryEnv is string adir)
        {
            var validateAdir = ValidateEnvironmentName(adir, "openhands.conversation.agent.llm.subscription.auth_directory_env");
            if (validateAdir.IsErr) return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(validateAdir.Error);
        }

        var authMethodResult = ResolveStringOrDefault(subscription.AuthMethod, env, "openhands.conversation.agent.llm.subscription.auth_method", "browser");
        if (authMethodResult.IsErr) return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(authMethodResult.Error);
        var authMethod = NormalizeOptional(authMethodResult.Value);
        if (authMethod is null)
        {
            return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm.subscription.auth_method", "must not be empty"));
        }
        authMethod = authMethod.ToLowerInvariant();
        if (authMethod != "browser" && authMethod != "device_code" && authMethod != "cached")
        {
            return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm.subscription.auth_method",
                "must be `browser`, `device_code`, or `cached`"));
        }

        return Result<OpenHandsSubscriptionCredentialConfig, WorkflowConfigError>.Ok(new OpenHandsSubscriptionCredentialConfig(
            vendor, accessTokenEnv, accountIdEnv, authDirectoryEnv, authMethod,
            subscription.OpenBrowser ?? true, subscription.ForceLogin ?? false));
    }

    private static Result<Unit, WorkflowConfigError> RejectUnsupportedOpenHandsLlmOptions(OpenHandsLlmFrontMatter llm)
    {
        if (llm.Options.Count > 0)
        {
            var unsupported = string.Join(", ", llm.Options.Keys);
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.agent.llm",
                $"unsupported options cannot be forwarded to the current OpenHands llm request subset: {unsupported}"));
        }
        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<ulong, WorkflowConfigError> ResolveOpenHandsMaxIterations(IntegerLike? value)
    {
        var maxIterationsResult = ResolvePositiveU64(value, "openhands.conversation.max_iterations", WorkflowConstants.DEFAULT_OPENHANDS_MAX_ITERATIONS);
        if (maxIterationsResult.IsErr) return Result<ulong, WorkflowConfigError>.Err(maxIterationsResult.Error);
        if (maxIterationsResult.Value > uint.MaxValue)
        {
            return Result<ulong, WorkflowConfigError>.Err(new InvalidField(
                "openhands.conversation.max_iterations", $"must be less than or equal to {uint.MaxValue}"));
        }
        return Result<ulong, WorkflowConfigError>.Ok(maxIterationsResult.Value);
    }

    private static Result<List<string>, WorkflowConfigError> ResolveCommand(
        List<string>? configured, string field, List<string> @default)
    {
        var command = configured ?? @default;
        if (command.Count == 0)
        {
            return Result<List<string>, WorkflowConfigError>.Err(new InvalidField(field, "must contain at least one argument"));
        }
        if (command.Any(p => string.IsNullOrWhiteSpace(p)))
        {
            return Result<List<string>, WorkflowConfigError>.Err(new InvalidField(field, "must not contain empty arguments"));
        }
        return Result<List<string>, WorkflowConfigError>.Ok(command);
    }

    private static Result<SortedDictionary<string, string>, WorkflowConfigError> ResolveStringMap(
        SortedDictionary<string, string> raw, IEnvironment env, string field)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            var valueResult = ResolveString(value, env, field);
            if (valueResult.IsErr) return Result<SortedDictionary<string, string>, WorkflowConfigError>.Err(valueResult.Error);
            result[key] = valueResult.Value;
        }
        return Result<SortedDictionary<string, string>, WorkflowConfigError>.Ok(result);
    }

    private static Result<string, WorkflowConfigError> ResolveWorkspaceRoot(string value, string baseDir, IEnvironment env)
    {
        var resolvedResult = ResolveString(value, env, "workspace.root");
        if (resolvedResult.IsErr) return Result<string, WorkflowConfigError>.Err(resolvedResult.Error);
        var resolved = resolvedResult.Value;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return Result<string, WorkflowConfigError>.Err(new InvalidField("workspace.root", "must not be empty"));
        }

        var expanded = ExpandHomeDirectory(resolved, env);
        if (Path.IsPathRooted(expanded))
        {
            return Result<string, WorkflowConfigError>.Ok(NormalizePath(expanded));
        }

        var normalizedBaseDir = NormalizeWorkflowBaseDir(baseDir);
        if (normalizedBaseDir.IsErr) return Result<string, WorkflowConfigError>.Err(normalizedBaseDir.Error);
        return Result<string, WorkflowConfigError>.Ok(NormalizePath(Path.Combine(normalizedBaseDir.Value, expanded)));
    }

    private static Result<string, WorkflowConfigError> NormalizeWorkflowBaseDir(string baseDir)
    {
        if (Path.IsPathRooted(baseDir))
        {
            return Result<string, WorkflowConfigError>.Ok(NormalizePath(baseDir));
        }

        var cwd = Directory.GetCurrentDirectory();
        return Result<string, WorkflowConfigError>.Ok(NormalizePath(Path.Combine(cwd, baseDir)));
    }

    private static Result<string, WorkflowConfigError> ResolveRelativePath(
        string? configured, IEnvironment env, string field, string @default)
    {
        var value = configured ?? @default;
        var resolvedResult = ResolveString(value, env, field);
        if (resolvedResult.IsErr) return Result<string, WorkflowConfigError>.Err(resolvedResult.Error);
        var resolved = resolvedResult.Value;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return Result<string, WorkflowConfigError>.Err(new InvalidField(field, "must not be empty"));
        }
        if (Path.IsPathRooted(resolved) || resolved.StartsWith('~'))
        {
            return Result<string, WorkflowConfigError>.Err(new InvalidField(field, "must stay relative to the issue workspace"));
        }

        var normalized = NormalizePath(resolved);
        if (!StaysWithinRelativeRoot(resolved))
        {
            return Result<string, WorkflowConfigError>.Err(new InvalidField(field, "must not escape the issue workspace"));
        }
        return Result<string, WorkflowConfigError>.Ok(normalized);
    }

    private static Result<string, WorkflowConfigError> ResolveStringOrDefault(
        string? configured, IEnvironment env, string field, string @default)
    {
        var normalized = NormalizeOptional(configured);
        if (normalized is null) return Result<string, WorkflowConfigError>.Ok(@default);
        return ResolveString(normalized, env, field);
    }

    private static Result<string, WorkflowConfigError> ResolveString(string value, IEnvironment env, string field)
    {
        if (ParseEnvToken(value) is string variable)
        {
            var resolved = NormalizeOptionalOwned(env.Get(variable));
            if (resolved is null)
            {
                return Result<string, WorkflowConfigError>.Err(new MissingEnvironmentVariable(field, variable));
            }
            return Result<string, WorkflowConfigError>.Ok(resolved);
        }
        return Result<string, WorkflowConfigError>.Ok(value);
    }

    private static Result<string, WorkflowConfigError> RequireLiteral(string? value, string field)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return Result<string, WorkflowConfigError>.Err(new MissingRequiredField(field));
        }
        return Result<string, WorkflowConfigError>.Ok(normalized);
    }

    private static Result<Unit, WorkflowConfigError> ValidateEnvironmentName(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || !value.All(c => c == '_' || char.IsAsciiLetterOrDigit(c)))
        {
            return Result<Unit, WorkflowConfigError>.Err(new InvalidField(field, "must be an environment variable name"));
        }
        return Result<Unit, WorkflowConfigError>.Ok(Unit.Value);
    }

    private static Result<ulong, WorkflowConfigError> ResolvePositiveU64(IntegerLike? value, string field, ulong @default)
    {
        if (value is null) return Result<ulong, WorkflowConfigError>.Ok(@default);
        var parsedResult = ParseI64(value, field);
        if (parsedResult.IsErr) return Result<ulong, WorkflowConfigError>.Err(parsedResult.Error);
        if (parsedResult.Value <= 0)
        {
            return Result<ulong, WorkflowConfigError>.Err(new InvalidField(field, "must be greater than zero"));
        }
        return Result<ulong, WorkflowConfigError>.Ok((ulong)parsedResult.Value);
    }

    private static Result<ulong, WorkflowConfigError> ResolveNonPositiveToDefault(IntegerLike? value, string field, ulong @default)
    {
        if (value is null) return Result<ulong, WorkflowConfigError>.Ok(@default);
        var parsedResult = ParseI64(value, field);
        if (parsedResult.IsErr) return Result<ulong, WorkflowConfigError>.Err(parsedResult.Error);
        if (parsedResult.Value <= 0) return Result<ulong, WorkflowConfigError>.Ok(@default);
        return Result<ulong, WorkflowConfigError>.Ok((ulong)parsedResult.Value);
    }

    private static Result<long, WorkflowConfigError> ParseI64(IntegerLike value, string field)
    {
        if (value.Integer is long i) return Result<long, WorkflowConfigError>.Ok(i);
        if (value.StringValue is string s)
        {
            if (long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return Result<long, WorkflowConfigError>.Ok(parsed);
            }
            return Result<long, WorkflowConfigError>.Err(new InvalidInteger(field, s));
        }
        return Result<long, WorkflowConfigError>.Err(new InvalidInteger(field, value.ToString()));
    }

    private static string ExpandHomeDirectory(string value, IEnvironment env)
    {
        if (value == "~")
        {
            return HomeDirectory(env);
        }
        if (value.StartsWith("~/"))
        {
            return Path.Combine(HomeDirectory(env), value.Substring(2));
        }
        return value;
    }

    private static string HomeDirectory(IEnvironment env)
    {
        var home = NormalizeOptionalOwned(env.Get("HOME"));
        if (home is null)
        {
            home = NormalizeOptionalOwned(env.Get("USERPROFILE"));
        }
        if (home is null)
        {
            // ht: Rust returns MissingEnvironmentVariable error; here we throw to be caught by caller.
            throw new MissingEnvironmentVariableException("HOME");
        }
        return home;
    }

    // ht: capture the home-directory-missing error to convert to WorkflowConfigError.
    private sealed class MissingEnvironmentVariableException : Exception
    {
        public string Variable { get; }
        public MissingEnvironmentVariableException(string variable) : base($"missing env var: {variable}") => Variable = variable;
    }

    private static string NormalizePath(string path)
    {
        // ht: .NET Path.GetFullPath normalizes separators and resolves . and ..
        // but we want to preserve the Rust behavior of not resolving symlinks.
        // Use a manual normalization that mirrors Rust's Component-based logic.
        var segments = new List<string>();
        bool sawRoot = false;
        bool isWindows = path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]);

        // Split on both / and \ for cross-platform.
        var parts = path.Split('/', '\\');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == 0 && part.Length == 0)
            {
                // Leading / — root dir on Unix.
                sawRoot = true;
                segments.Add("/");
                continue;
            }
            if (i == 0 && isWindows && part.Length == 2 && part[1] == ':')
            {
                // Windows drive prefix.
                segments.Add(part);
                continue;
            }
            if (part == "" || part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (segments.Count > 0 && segments[^1] != "/" && segments[^1] != ".."
                    && !(segments[^1].Length == 2 && segments[^1][1] == ':'))
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else if (!sawRoot)
                {
                    segments.Add("..");
                }
                continue;
            }
            segments.Add(part);
        }

        if (segments.Count == 0)
        {
            return sawRoot ? "/" : ".";
        }

        // Reconstruct. On Windows with drive prefix, use \ separator.
        if (isWindows)
        {
            var result = segments[0];
            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i] == "/") continue;
                result = Path.Combine(result, segments[i]);
            }
            return result;
        }

        // ht: Unix root is stored as "/" segment; joining naively yields "//x". Use it as a prefix instead.
        if (segments.Count > 0 && segments[0] == "/")
        {
            return "/" + string.Join("/", segments.Skip(1));
        }
        return string.Join("/", segments);
    }

    private static bool StaysWithinRelativeRoot(string path)
    {
        int depth = 0;
        var parts = path.Split('/', '\\');
        foreach (var part in parts)
        {
            if (part == "" || part == ".") continue;
            if (part == "..")
            {
                if (depth == 0) return false;
                depth--;
            }
            else
            {
                depth++;
            }
        }
        return true;
    }

    private static string? ParseEnvToken(string value)
    {
        if (value.StartsWith("${") && value.EndsWith('}') && value.Length > 3)
        {
            var variable = value.Substring(2, value.Length - 3);
            return IsEnvName(variable) ? variable : null;
        }
        if (value.StartsWith('$') && value.Length > 1)
        {
            var variable = value.Substring(1);
            return IsEnvName(variable) ? variable : null;
        }
        return null;
    }

    private static bool IsEnvName(string value) =>
        !string.IsNullOrEmpty(value) && value.All(c => c == '_' || char.IsAsciiLetterOrDigit(c));

    private static string? NormalizeOptional(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? NormalizeOptionalOwned(string? value) => NormalizeOptional(value);

    private static string? NormalizeOptionalLiteral(string? value) => NormalizeOptional(value);
}
