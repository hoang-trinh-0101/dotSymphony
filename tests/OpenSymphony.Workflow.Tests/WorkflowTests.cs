using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;
using OpenSymphony.Workflow;

namespace OpenSymphony.Workflow.Tests;

public class WorkflowTests
{
    // ht: TestEnvironment wraps a Dictionary<string,string> as IEnvironment, mirroring Rust's BTreeMap<String,String>.
    private sealed class TestEnvironment : IEnvironment
    {
        private readonly Dictionary<string, string> _vars;
        public TestEnvironment(Dictionary<string, string> vars) => _vars = vars;
        public string? Get(string name) =>
            _vars.TryGetValue(name, out var value) ? value : null;
    }

    // ht: TestIssue mirrors the Rust TestIssue struct. JsonPropertyName attributes ensure
    // the Fluid template engine sees snake_case keys matching the Rust serde field names.
    private sealed class TestIssue
    {
        [JsonPropertyName("identifier")]
        public string Identifier { get; init; } = "";
        [JsonPropertyName("title")]
        public string Title { get; init; } = "";
        [JsonPropertyName("state")]
        public string State { get; init; } = "";
        [JsonPropertyName("description")]
        public string? Description { get; init; }
        [JsonPropertyName("labels")]
        public List<string> Labels { get; init; } = new();
    }

    private static TestEnvironment Env(params (string, string)[] pairs)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (key, value) in pairs)
            dict[key] = value;
        return new TestEnvironment(dict);
    }

    // ht: Rust's WorkflowDefinition::render_prompt uses effective_prompt_template() then renders.
    // C# has RenderPrompt on ResolvedWorkflow, but the Rust tests call it on WorkflowDefinition.
    // This helper replicates the Rust behavior: use prompt_template or default if blank.
    private static Result<string, PromptTemplateError> RenderPrompt(
        WorkflowDefinition workflow, TestIssue issue, uint? attempt)
    {
        var template = string.IsNullOrWhiteSpace(workflow.PromptTemplate)
            ? WorkflowConstants.DEFAULT_PROMPT_TEMPLATE
            : workflow.PromptTemplate;
        return WorkflowTemplate.RenderPrompt(template, issue, attempt);
    }

    private static string SampleWorkflow() => """
---
tracker:
  kind: linear
  project_slug: sample-project
  active_states:
    - Todo
    - In Progress
  terminal_states:
    - Done
    - Closed
polling:
  interval_ms: 5000
workspace:
  root: ~/workspaces
hooks:
  timeout_ms: 60000
agent:
  max_concurrent_agents: 4
  max_turns: 8
  max_retry_backoff_ms: 120000
  stall_timeout_ms: 90000
openhands:
  transport:
    base_url: http://127.0.0.1:8000
  conversation:
    persistence_dir_relative: .opensymphony/openhands
    agent:
      llm:
        model: ${LLM_MODEL}
---

# Assignment

Ticket: {{ issue.identifier }}
""";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "WORKFLOW.md")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new FileNotFoundException("Could not find repo root with WORKFLOW.md");
    }

    [Fact]
    public void ParsesValidFrontMatterAndPromptBody()
    {
        var workflow = WorkflowLoader.ParseWorkflow(SampleWorkflow());
        Assert.True(workflow.IsOk, "sample workflow should parse");

        Assert.Equal("linear", workflow.Value.FrontMatter.Tracker.Kind);
        Assert.Equal(8, workflow.Value.FrontMatter.Agent.MaxTurns?.Integer);
        Assert.Equal("\n# Assignment\n\nTicket: {{ issue.identifier }}\n",
            workflow.Value.PromptTemplate);
    }

    [Fact]
    public void ParsesWorkflowWithoutFrontMatter()
    {
        var workflow = WorkflowLoader.ParseWorkflow("\n\nPrompt only\n");
        Assert.True(workflow.IsOk, "prompt-only workflow should parse");

        Assert.Equal(new WorkflowFrontMatter(), workflow.Value.FrontMatter);
        Assert.Equal("\n\nPrompt only\n", workflow.Value.PromptTemplate);
    }

    [Fact]
    public void TreatsNonMapDelimitedBlockAsPromptBody()
    {
        var source = "---\n- nope\n---\nbody\n";
        var workflow = WorkflowLoader.ParseWorkflow(source);
        Assert.True(workflow.IsOk, "non-mapping delimited blocks should fall back to prompt body");

        Assert.Equal(new WorkflowFrontMatter(), workflow.Value.FrontMatter);
        Assert.Equal(source, workflow.Value.PromptTemplate);
    }

    [Fact]
    public void TreatsUnmatchedLeadingDelimiterAsPromptBody()
    {
        var source = "---\n# Assignment\n";
        var workflow = WorkflowLoader.ParseWorkflow(source);
        Assert.True(workflow.IsOk, "unterminated leading delimiter should fall back to prompt body");

        Assert.Equal(new WorkflowFrontMatter(), workflow.Value.FrontMatter);
        Assert.Equal(source, workflow.Value.PromptTemplate);
    }

    [Fact]
    public void ParsesLeadingThematicBreaksAsPromptBodyWhenFrontMatterIsNotAMap()
    {
        var source = "---\n# Assignment\n---\n\nContinue.\n";
        var workflow = WorkflowLoader.ParseWorkflow(source);
        Assert.True(workflow.IsOk, "plain markdown thematic breaks should not consume prompt content");

        Assert.Equal(new WorkflowFrontMatter(), workflow.Value.FrontMatter);
        Assert.Equal(source, workflow.Value.PromptTemplate);
    }

    [Fact]
    public void PreservesIndentedDelimiterLinesInsideYamlBlockScalars()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\nhooks:\n  before_run: |\n    cat <<'EOF'\n    ---\n    EOF\n---\nPrompt body\n");
        Assert.True(workflow.IsOk, "indented delimiter-like lines in block scalars should parse");

        Assert.Equal("cat <<'EOF'\n---\nEOF\n",
            workflow.Value.FrontMatter.Hooks.BeforeRun);
        Assert.Equal("Prompt body\n", workflow.Value.PromptTemplate);
    }

    [Fact]
    public void RejectsUnknownTopLevelNamespaces()
    {
        var error = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "openhadns:\n  transport:\n    base_url: http://127.0.0.1:8000\n---\n{{ issue.identifier }}\n");
        Assert.True(error.IsErr, "unknown namespaces should fail deterministically");

        Assert.IsType<UnknownTopLevelNamespace>(error.Error);
        Assert.Equal("openhadns", ((UnknownTopLevelNamespace)error.Error).Namespace);
    }

    [Fact]
    public void AcceptsRepoLocalNamespaces()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "logging:\n  level: debug\ncodex:\n  command: codex app-server\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "repo-local namespaces should be accepted");

        Assert.Equal("codex app-server", workflow.Value.FrontMatter.Codex?["command"]?.ToString());
        Assert.Equal("debug", workflow.Value.FrontMatter.Logging?["level"]?.ToString());
    }

    [Fact]
    public void LoadsCheckedInWorkflows()
    {
        var repoRoot = RepoRoot();
        var rootResult = WorkflowLoader.LoadWorkflowFromPath(Path.Combine(repoRoot, "WORKFLOW.md"));
        Assert.True(rootResult.IsOk, "repo root workflow should parse");
    }

    [Fact]
    public void ReportsMissingWorkflowFile()
    {
        var path = "/definitely/missing/WORKFLOW.md";
        var error = WorkflowLoader.LoadWorkflowFromPath(path);
        Assert.True(error.IsErr, "missing workflow file should fail");

        Assert.IsType<MissingWorkflowFile>(error.Error);
        Assert.Equal(path, ((MissingWorkflowFile)error.Error).Path);
    }

    [Fact]
    public void LeavesOpenHandsLocalServerCommandUnsetWhenOmitted()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo/target", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Null(resolved.Value.Extensions.OpenHands.LocalServer.Command);
    }

    [Fact]
    public void ResolvesDefaultsAndOpenHandsExtension()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n    - In Progress\n  terminal_states:\n    - Done\n    - Closed\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"), ("HOME", "/Users/tester"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Equal(TrackerKind.Linear, resolved.Value.Config.Tracker.Kind);
        Assert.Equal(WorkflowConstants.DEFAULT_LINEAR_ENDPOINT, resolved.Value.Config.Tracker.Endpoint);
        Assert.Equal("linear-token", resolved.Value.Config.Tracker.ApiKey);
        Assert.Equal(WorkflowConstants.DEFAULT_POLL_INTERVAL_MS, resolved.Value.Config.Polling.IntervalMs);
        Assert.Equal(WorkflowConstants.DEFAULT_WORKSPACE_ROOT, resolved.Value.Config.Workspace.Root);
        Assert.Equal(WorkflowConstants.DEFAULT_HOOK_TIMEOUT_MS, resolved.Value.Config.Hooks.TimeoutMs);
        Assert.Equal(WorkflowConstants.DEFAULT_MAX_CONCURRENT_AGENTS, resolved.Value.Config.Agent.MaxConcurrentAgents);
        Assert.Equal(WorkflowConstants.DEFAULT_MAX_TURNS, resolved.Value.Config.Agent.MaxTurns);
        Assert.Equal(WorkflowConstants.DEFAULT_MAX_RETRY_BACKOFF_MS, resolved.Value.Config.Agent.MaxRetryBackoffMs);
        Assert.Equal(WorkflowConstants.DEFAULT_STALL_TIMEOUT_MS, resolved.Value.Config.Agent.StallTimeoutMs);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_BASE_URL, resolved.Value.Extensions.OpenHands.Transport.BaseUrl);
        Assert.Null(resolved.Value.Extensions.OpenHands.LocalServer.Command);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_PERSISTENCE_DIR,
            resolved.Value.Extensions.OpenHands.Conversation.PersistenceDirRelative);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONFIRMATION_POLICY_KIND,
            resolved.Value.Extensions.OpenHands.Conversation.ConfirmationPolicy.Kind);
        Assert.Equal("Agent", resolved.Value.Extensions.OpenHands.Conversation.Agent.Kind);
        var tools = resolved.Value.Extensions.OpenHands.Conversation.Agent.Tools;
        Assert.NotNull(tools);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_AGENT_TOOLS,
            tools!.Select(t => t.Name).ToArray());
        Assert.Null(resolved.Value.Extensions.OpenHands.Conversation.Agent.IncludeDefaultTools);
        var condenser = resolved.Value.Extensions.OpenHands.Conversation.Agent.Condenser;
        Assert.NotNull(condenser);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE, condenser!.MaxSize);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST, condenser.KeepFirst);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_READY_TIMEOUT_MS,
            resolved.Value.Extensions.OpenHands.Websocket.ReadyTimeoutMs);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_RECONNECT_INITIAL_MS,
            resolved.Value.Extensions.OpenHands.Websocket.ReconnectInitialMs);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_RECONNECT_MAX_MS,
            resolved.Value.Extensions.OpenHands.Websocket.ReconnectMaxMs);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_QUERY_PARAM_NAME,
            resolved.Value.Extensions.OpenHands.Websocket.QueryParamName);
    }

    [Fact]
    public void ResolvesExplicitOpenHandsLocalServerCommand()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  local_server:\n    command:\n      - bash\n      - ./scripts/run-openhands.sh\n      - --port\n      - \"9000\"\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "explicit local server commands should resolve");

        Assert.Equal(new List<string> { "bash", "./scripts/run-openhands.sh", "--port", "9000" },
            resolved.Value.Extensions.OpenHands.LocalServer.Command);
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsLocalServerEnabledOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  local_server:\n    enabled: false\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "unsupported local server disablement should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.local_server.enabled", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsLocalServerStartupTimeoutOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  local_server:\n    startup_timeout_ms: 30000\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "unsupported startup timeout overrides should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.local_server.startup_timeout_ms", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsLocalServerReadinessProbePathOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  local_server:\n    readiness_probe_path: /readyz\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "unsupported readiness probe path overrides should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.local_server.readiness_probe_path", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsLocalServerEnvOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  local_server:\n    env:\n      RUNTIME: process\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "unsupported local server env overrides should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.local_server.env", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void ExplicitTrackerApiKeyEnvReferenceMustResolve()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  api_key: ${TRACKER_API_KEY}\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "fallback-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "unset explicit tracker api key env should fail");

        Assert.IsType<MissingEnvironmentVariable>(error.Error);
        Assert.Equal("tracker.api_key", ((MissingEnvironmentVariable)error.Error).Field);
    }

    [Fact]
    public void ResolvesEnvSubstitutionAndPathRules()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n    - In Progress\n  terminal_states:\n    - Done\n" +
            "workspace:\n  root: ${WORKSPACE_ROOT}\nhooks:\n  timeout_ms: 0\n" +
            "agent:\n  max_turns: \"5\"\n  stall_timeout_ms: 0\n  max_concurrent_agents_by_state:\n    In Review: 2\n" +
            "openhands:\n  transport:\n    base_url: ${LLM_BASE_URL}\n  conversation:\n    persistence_dir_relative: .cache/openhands\n" +
            "    agent:\n      llm:\n        model: ${LLM_MODEL}\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(
            ("LINEAR_API_KEY", "linear-token"),
            ("WORKSPACE_ROOT", "/tmp/workspaces"),
            ("LLM_BASE_URL", "http://localhost:8000"),
            ("LLM_MODEL", "gpt-5.4-mini"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo/config", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Equal("/tmp/workspaces", resolved.Value.Config.Workspace.Root);
        Assert.Equal(WorkflowConstants.DEFAULT_HOOK_TIMEOUT_MS, resolved.Value.Config.Hooks.TimeoutMs);
        Assert.Equal(5UL, resolved.Value.Config.Agent.MaxTurns);
        Assert.Null(resolved.Value.Config.Agent.StallTimeoutMs);
        Assert.Equal(2UL, resolved.Value.Config.Agent.MaxConcurrentAgentsByState["in review"]);
        Assert.Equal("http://localhost:8000", resolved.Value.Extensions.OpenHands.Transport.BaseUrl);
        Assert.Equal(".cache/openhands",
            resolved.Value.Extensions.OpenHands.Conversation.PersistenceDirRelative);
        Assert.Equal("gpt-5.4-mini",
            resolved.Value.Extensions.OpenHands.Conversation.Agent.Llm?.Model);
        Assert.Equal("Agent", resolved.Value.Extensions.OpenHands.Conversation.Agent.Kind);
        var tools = resolved.Value.Extensions.OpenHands.Conversation.Agent.Tools;
        Assert.NotNull(tools);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_AGENT_TOOLS,
            tools!.Select(t => t.Name).ToArray());
        Assert.Null(resolved.Value.Extensions.OpenHands.Conversation.Agent.IncludeDefaultTools);
        var condenser = resolved.Value.Extensions.OpenHands.Conversation.Agent.Condenser;
        Assert.NotNull(condenser);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE, condenser!.MaxSize);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST, condenser.KeepFirst);
    }

    [Fact]
    public void ResolvesOpenHandsCondenserConfiguration()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      condenser:\n        enabled: true\n        max_size: 320\n        keep_first: 4\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "workflow should resolve");
        var condenser = resolved.Value.Extensions.OpenHands.Conversation.Agent.Condenser;
        Assert.NotNull(condenser);
        Assert.Equal(320UL, condenser!.MaxSize);
        Assert.Equal(4UL, condenser.KeepFirst);
        Assert.Equal("openai/gpt-5.4",
            resolved.Value.Extensions.OpenHands.Conversation.Agent.Llm?.Model);
    }

    [Fact]
    public void DefaultsOpenHandsCondenserThresholdsWhenEnabledWithoutOverrides()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      condenser:\n        enabled: true\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "workflow should resolve");
        var condenser = resolved.Value.Extensions.OpenHands.Conversation.Agent.Condenser;
        Assert.NotNull(condenser);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_MAX_SIZE, condenser!.MaxSize);
        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONDENSER_KEEP_FIRST, condenser.KeepFirst);
    }

    [Fact]
    public void DisablesOpenHandsCondenserWhenFlagIsFalse()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      condenser:\n        enabled: false\n        max_size: 320\n        keep_first: 4\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Null(resolved.Value.Extensions.OpenHands.Conversation.Agent.Condenser);
    }

    [Fact]
    public void ResolvesConfiguredOpenHandsAgentToolsAndDefaultToolPolicy()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      tools:\n        - name: ReadFileTool\n" +
            "        - name: BrowserToolSet\n          params:\n            start_url: https://example.com\n" +
            "      include_default_tools:\n        - FinishTool\n        - ThinkTool\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "agent tool overrides should resolve");

        var agent = resolved.Value.Extensions.OpenHands.Conversation.Agent;
        var tools = agent.Tools;
        Assert.NotNull(tools);
        Assert.Equal(2, tools!.Count);
        Assert.Equal("ReadFileTool", tools[0].Name);
        Assert.Empty(tools[0].Params);
        Assert.Equal("BrowserToolSet", tools[1].Name);
        Assert.Equal("openai/gpt-5.4", agent.Llm?.Model);
        Assert.Equal("https://example.com", tools[1].Params["start_url"].GetString());
        Assert.Equal(new List<string> { "FinishTool", "ThinkTool" }, agent.IncludeDefaultTools);
    }

    [Fact]
    public void PreservesExplicitEmptyOpenHandsAgentToolsForOptOut()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      tools: []\n      include_default_tools: []\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "explicit empty tool lists should resolve");

        var agent = resolved.Value.Extensions.OpenHands.Conversation.Agent;
        Assert.Empty(agent.Tools);
        Assert.Empty(agent.IncludeDefaultTools);
    }

    [Fact]
    public void RejectsInvalidOpenHandsTransportBaseUrls()
    {
        var invalidBaseUrls = new[]
        {
            "localhost:8000",
            "ws://127.0.0.1:8000",
            "http://[::1]:8000",
            "http://127.0.0.1:8000?session=abc",
            "http://127.0.0.1:8000#fragment",
            "https://user:pass@example.com/runtime",
        };
        foreach (var invalidBaseUrl in invalidBaseUrls)
        {
            var workflow = WorkflowLoader.ParseWorkflow(
                "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
                "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
                $"openhands:\n  transport:\n    base_url: {invalidBaseUrl}\n" +
                "---\n{{{{ issue.identifier }}}}\n");
            Assert.True(workflow.IsOk, "workflow should parse");
            var env = Env(("LINEAR_API_KEY", "linear-token"));

            var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
            Assert.True(error.IsErr, $"invalid OpenHands base URL '{invalidBaseUrl}' should fail during resolution");

            Assert.IsType<InvalidField>(error.Error);
            Assert.Equal("openhands.transport.base_url", ((InvalidField)error.Error).Field);
        }
    }

    [Fact]
    public void RejectsQueryOrFragmentOpenHandsTransportBaseUrl()
    {
        var invalidBaseUrls = new[]
        {
            "http://127.0.0.1:8000?session=abc",
            "http://127.0.0.1:8000#fragment",
        };
        foreach (var invalidBaseUrl in invalidBaseUrls)
        {
            var workflow = WorkflowLoader.ParseWorkflow(
                "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
                "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
                $"openhands:\n  transport:\n    base_url: {invalidBaseUrl}\n" +
                "---\n{{{{ issue.identifier }}}}\n");
            Assert.True(workflow.IsOk, "workflow should parse");
            var env = Env(("LINEAR_API_KEY", "linear-token"));

            var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
            Assert.True(error.IsErr, "query/fragment-bearing OpenHands origins should fail during resolution");

            Assert.IsType<InvalidField>(error.Error);
            Assert.Equal("openhands.transport.base_url", ((InvalidField)error.Error).Field);
        }
    }

    [Fact]
    public void RejectsNonLoopbackHttpOpenHandsTransportBaseUrl()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    base_url: http://agent.example.com:8000\n    session_api_key_env: OPENHANDS_SESSION_API_KEY\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "non-loopback http OpenHands origins should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.transport.base_url", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void ResolvesRemoteHttpsOpenHandsTransportWithPathAndAuth()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    base_url: https://agent.example.com/runtime/api/\n    session_api_key_env: OPENHANDS_SESSION_API_KEY\n" +
            "  websocket:\n    ready_timeout_ms: 45000\n    reconnect_initial_ms: 1500\n    reconnect_max_ms: 45000\n    auth_mode: header\n    query_param_name: openhands_token\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "remote https transport should resolve");

        Assert.Equal("https://agent.example.com/runtime/api/",
            resolved.Value.Extensions.OpenHands.Transport.BaseUrl);
        Assert.Equal("OPENHANDS_SESSION_API_KEY",
            resolved.Value.Extensions.OpenHands.Transport.SessionApiKeyEnv);
        Assert.Equal(45000UL, resolved.Value.Extensions.OpenHands.Websocket.ReadyTimeoutMs);
        Assert.Equal(1500UL, resolved.Value.Extensions.OpenHands.Websocket.ReconnectInitialMs);
        Assert.Equal(45000UL, resolved.Value.Extensions.OpenHands.Websocket.ReconnectMaxMs);
        Assert.Equal("header", resolved.Value.Extensions.OpenHands.Websocket.AuthMode);
        Assert.Equal("openhands_token", resolved.Value.Extensions.OpenHands.Websocket.QueryParamName);
    }

    [Fact]
    public void RejectsRemoteHttpsOpenHandsTransportWithoutAuthEnv()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    base_url: https://agent.example.com/runtime/api/\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "remote https transport should require auth");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.transport.session_api_key_env", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void RejectsRemovedLegacyLinearBridgeConfig()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  mcp:\n    stdio_servers:\n      - name: linear\n        command:\n          - deprecated\n          - removed-linear-bridge\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "removed legacy Linear bridge config should fail with a migration error");

        Assert.IsType<RemovedField>(error.Error);
        Assert.Equal("openhands.mcp", ((RemovedField)error.Error).Field);
    }

    [Fact]
    public void ResolvesOpenHandsConversationReusePolicyOverrideForRuntimeConsumers()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    reuse_policy: fresh_each_run\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "runtime-owned reuse-policy gating should not fail during workflow resolution");

        Assert.Equal("fresh_each_run",
            resolved.Value.Extensions.OpenHands.Conversation.ReusePolicy);
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsAgentOptionOverrides()
    {
        var workflowSources = new[]
        {
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      log_completions: true\n---\n{{ issue.identifier }}\n",
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      custom_mode: verbose\n---\n{{ issue.identifier }}\n",
        };
        foreach (var workflowSource in workflowSources)
        {
            var workflow = WorkflowLoader.ParseWorkflow(workflowSource);
            Assert.True(workflow.IsOk, "workflow should parse");
            var env = Env(("LINEAR_API_KEY", "linear-token"));

            var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
            Assert.True(error.IsErr, "unsupported agent options should fail during resolution");

            Assert.IsType<InvalidField>(error.Error);
            var field = ((InvalidField)error.Error).Field;
            Assert.True(field == "openhands.conversation.agent.log_completions" ||
                        field == "openhands.conversation.agent",
                        $"unexpected field: {field}");
        }
    }

    [Fact]
    public void RejectsOpenHandsMaxIterationsAboveU32Range()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            $"openhands:\n  conversation:\n    max_iterations: {(long)uint.MaxValue + 1}\n" +
            "---\n{{{{ issue.identifier }}}}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "oversized max_iterations should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.conversation.max_iterations", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void DefaultsConfirmationPolicyKindWhenBlockOmitsIt()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    confirmation_policy: {}\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "confirmation policy defaults should resolve");

        Assert.Equal(WorkflowConstants.DEFAULT_OPENHANDS_CONFIRMATION_POLICY_KIND,
            resolved.Value.Extensions.OpenHands.Conversation.ConfirmationPolicy.Kind);
    }

    [Fact]
    public void RejectsConfirmationPolicyOptionsThatCannotReachRuntime()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    confirmation_policy:\n      max_budget_usd: 5\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "unsupported confirmation policy options should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.conversation.confirmation_policy", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void RejectsOpenHandsLlmBlocksWithoutModel()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm: {}\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "llm blocks without model should fail during resolution");

        Assert.IsType<MissingRequiredField>(error.Error);
        Assert.Equal("openhands.conversation.agent.llm.model",
            ((MissingRequiredField)error.Error).Field);
    }

    [Fact]
    public void ResolvesOpenHandsTransportSessionApiKeyEnv()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    session_api_key_env: OPENHANDS_SESSION_API_KEY\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "transport auth env should resolve");

        Assert.Equal("OPENHANDS_SESSION_API_KEY",
            resolved.Value.Extensions.OpenHands.Transport.SessionApiKeyEnv);
    }

    [Fact]
    public void ResolvesOpenHandsWebsocketAuthModeOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    session_api_key_env: OPENHANDS_SESSION_API_KEY\n  websocket:\n    auth_mode: header\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "websocket auth mode should resolve");

        Assert.Equal("header", resolved.Value.Extensions.OpenHands.Websocket.AuthMode);
    }

    [Fact]
    public void ResolvesOpenHandsWebsocketRuntimeOverrides()
    {
        var workflowSources = new[]
        {
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  websocket:\n    ready_timeout_ms: 45000\n---\n{{ issue.identifier }}\n",
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  websocket:\n    reconnect_initial_ms: 1500\n---\n{{ issue.identifier }}\n",
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  websocket:\n    reconnect_max_ms: 45000\n---\n{{ issue.identifier }}\n",
        };
        foreach (var workflowSource in workflowSources)
        {
            var workflow = WorkflowLoader.ParseWorkflow(workflowSource);
            Assert.True(workflow.IsOk, "workflow should parse");
            var env = Env(("LINEAR_API_KEY", "linear-token"));

            var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
            Assert.True(resolved.IsOk, "websocket runtime overrides should resolve when supported");
        }
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsWebsocketEnabledOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  websocket:\n    enabled: false\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "workflow-owned websocket enablement should still fail");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.websocket.enabled", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void ResolvesOpenHandsWebsocketQueryParamOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    session_api_key_env: OPENHANDS_SESSION_API_KEY\n  websocket:\n    query_param_name: openhands_token\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "websocket query-param overrides should resolve");

        Assert.Equal("openhands_token",
            resolved.Value.Extensions.OpenHands.Websocket.QueryParamName);
    }

    [Fact]
    public void RejectsInvalidOpenHandsWebsocketAuthModeOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    session_api_key_env: OPENHANDS_SESSION_API_KEY\n  websocket:\n    auth_mode: browser_magic\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "invalid websocket auth mode should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.websocket.auth_mode", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void ResolvesOpenHandsLlmApiKeyEnvOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: ${LLM_MODEL}\n        api_key_env: OPENHANDS_API_KEY\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"), ("LLM_MODEL", "gpt-5.4"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "llm api-key env overrides should resolve");

        Assert.Equal("OPENHANDS_API_KEY",
            resolved.Value.Extensions.OpenHands.Conversation.Agent.Llm?.ApiKeyEnv);
    }

    [Fact]
    public void RejectsUnsupportedOpenHandsLlmOptionOverrides()
    {
        var workflowSources = new[]
        {
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: gpt-5.4-mini\n        temperature: 0.1\n---\n{{ issue.identifier }}\n",
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: gpt-5.4-mini\n        reasoning_effort: high\n---\n{{ issue.identifier }}\n",
        };
        foreach (var workflowSource in workflowSources)
        {
            var workflow = WorkflowLoader.ParseWorkflow(workflowSource);
            Assert.True(workflow.IsOk, "workflow should parse");
            var env = Env(("LINEAR_API_KEY", "linear-token"));

            var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
            Assert.True(error.IsErr, "unsupported llm options should fail during resolution");

            Assert.IsType<InvalidField>(error.Error);
            Assert.Equal("openhands.conversation.agent.llm", ((InvalidField)error.Error).Field);
        }
    }

    [Fact]
    public void ResolvesOpenHandsLlmBaseUrlEnvOverride()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: ${LLM_MODEL}\n        base_url_env: OPENHANDS_BASE_URL\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"), ("LLM_MODEL", "gpt-5.4"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "llm base-url env overrides should resolve");

        Assert.Equal("OPENHANDS_BASE_URL",
            resolved.Value.Extensions.OpenHands.Conversation.Agent.Llm?.BaseUrlEnv);
    }

    [Fact]
    public void ResolvesFeatureGatedOpenHandsSubscriptionCredentialReference()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: gpt-5.2-codex\n        credential_mode: openai_subscription\n        subscription:\n          vendor: openai\n" +
            "          access_token_env: OPENHANDS_OPENAI_SUBSCRIPTION_ACCESS_TOKEN\n          account_id_env: OPENHANDS_OPENAI_SUBSCRIPTION_ACCOUNT_ID\n" +
            "          auth_directory_env: OPENHANDS_AUTH_DIR\n          auth_method: device_code\n          open_browser: false\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "subscription credential references should resolve");
        var llm = resolved.Value.Extensions.OpenHands.Conversation.Agent.Llm;
        Assert.NotNull(llm);
        var subscription = llm!.Subscription;
        Assert.NotNull(subscription);

        Assert.Equal("openai_subscription", llm.CredentialMode);
        Assert.Equal("OPENHANDS_OPENAI_SUBSCRIPTION_ACCESS_TOKEN", subscription!.AccessTokenEnv);
        Assert.Equal("OPENHANDS_OPENAI_SUBSCRIPTION_ACCOUNT_ID", subscription.AccountIdEnv);
        Assert.Equal("device_code", subscription.AuthMethod);
        Assert.False(subscription.OpenBrowser);
        Assert.False(subscription.ForceLogin);
    }

    [Fact]
    public void GatesOpenHandsSubscriptionCredentialModeByFeature()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: gpt-5.2-codex\n        credential_mode: openai_subscription\n        subscription:\n          vendor: openai\n          access_token_env: OPENHANDS_OPENAI_SUBSCRIPTION_ACCESS_TOKEN\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        // ht: C# port has subscription feature always enabled (no feature gating).
        var result = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(result.IsOk, "subscription mode should resolve in C# port");
    }

    [Fact]
    public void RejectsPersistencePathsThatEscapeTheWorkspace()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  conversation:\n    persistence_dir_relative: ../shared-state\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "parent-directory traversal should be rejected");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.conversation.persistence_dir_relative",
            ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void ResolvesRelativeWorkspacePathsAgainstWorkflowDirectory()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "workspace:\n  root: ./nested/workspaces\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo/config", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Equal("/repo/config/nested/workspaces",
            resolved.Value.Config.Workspace.Root);
    }

    [Fact]
    public void ResolvesBareWorkspaceRootsAgainstWorkflowDirectory()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "workspace:\n  root: workspaces\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo/config", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Equal("/repo/config/workspaces",
            resolved.Value.Config.Workspace.Root);
    }

    [Fact]
    public void ResolvesRelativeWorkspaceRootsAgainstRelativeWorkflowDirectories()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "workspace:\n  root: ./var/workspaces\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));
        var expectedRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
            "examples/target-repo/var/workspaces"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "examples/target-repo", env);
        Assert.True(resolved.IsOk, "workflow should resolve");

        Assert.Equal(expectedRoot, resolved.Value.Config.Workspace.Root);
        Assert.True(Path.IsPathRooted(resolved.Value.Config.Workspace.Root));
    }

    [Fact]
    public void RejectsUnsupportedIpv6OpenHandsTransportBaseUrl()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "openhands:\n  transport:\n    base_url: http://[::1]:8000\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "IPv6 OpenHands origins should fail during resolution");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("openhands.transport.base_url", ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void RendersPromptForFirstRunAndContinuation()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "---\nTicket {{ issue.identifier }}\n{% if attempt %}\nAttempt {{ attempt }}\n{% endif %}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var issue = new TestIssue
        {
            Identifier = "COE-259",
            Title = "Workflow loader",
            State = "In Progress",
            Description = "Implement the workflow crate",
            Labels = ["rust", "workflow"],
        };

        var first = RenderPrompt(workflow.Value, issue, null);
        Assert.True(first.IsOk, "first run render should succeed");
        var continuation = RenderPrompt(workflow.Value, issue, 2);
        Assert.True(continuation.IsOk, "continuation render should succeed");

        Assert.Contains("Ticket COE-259", first.Value);
        Assert.DoesNotContain("Attempt", first.Value);
        Assert.Contains("Attempt 2", continuation.Value);
    }

    [Fact]
    public void RejectsUnknownTemplateVariables()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "---\n{{ issue.missing_field }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var issue = new TestIssue
        {
            Identifier = "COE-259",
            Title = "Workflow loader",
            State = "In Progress",
            Description = null,
            Labels = [],
        };

        var error = RenderPrompt(workflow.Value, issue, null);
        Assert.True(error.IsErr, "missing template variables should fail");

        Assert.IsType<PromptTemplateRender>(error.Error);
    }

    [Fact]
    public void RejectsUnknownTemplateFilters()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n" +
            "---\n{{ issue.title | missing_filter }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var issue = new TestIssue
        {
            Identifier = "COE-259",
            Title = "Workflow loader",
            State = "In Progress",
            Description = null,
            Labels = [],
        };

        var error = RenderPrompt(workflow.Value, issue, null);
        Assert.True(error.IsErr, "unknown filters should fail");

        Assert.IsType<PromptTemplateParse>(error.Error);
    }

    [Fact]
    public void UsesDefaultPromptWhenBodyIsEmpty()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n---\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var issue = new TestIssue
        {
            Identifier = "COE-259",
            Title = "Workflow loader",
            State = "In Progress",
            Description = null,
            Labels = [],
        };

        var rendered = RenderPrompt(workflow.Value, issue, null);
        Assert.True(rendered.IsOk, "default prompt render should succeed");

        Assert.Equal(WorkflowConstants.DEFAULT_PROMPT_TEMPLATE, rendered.Value);
    }

    [Fact]
    public void UsesDefaultPromptWhenBodyIsWhitespaceOnly()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n---\n\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var issue = new TestIssue
        {
            Identifier = "COE-259",
            Title = "Workflow loader",
            State = "In Progress",
            Description = null,
            Labels = [],
        };

        var rendered = RenderPrompt(workflow.Value, issue, null);
        Assert.True(rendered.IsOk, "whitespace-only prompt should use the default template");

        Assert.Equal(WorkflowConstants.DEFAULT_PROMPT_TEMPLATE, rendered.Value);
    }

    [Fact]
    public void PreservesWhitespaceSensitivePromptBody()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n---\n\n    code block\n");
        Assert.True(workflow.IsOk, "workflow should parse");

        Assert.Equal("\n    code block\n", workflow.Value.PromptTemplate);
    }

    [Fact]
    public void ErrorsOnMissingRequiredTrackerConfig()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env();

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "missing project slug should fail");

        Assert.IsType<MissingRequiredField>(error.Error);
        Assert.Equal("tracker.project_slug", ((MissingRequiredField)error.Error).Field);
    }

    [Fact]
    public void MissingTrackerTerminalStatesFail()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "missing terminal states should fail");

        Assert.IsType<MissingRequiredField>(error.Error);
        Assert.Equal("tracker.terminal_states", ((MissingRequiredField)error.Error).Field);
    }

    [Fact]
    public void RejectsInvalidPerStateConcurrencyLimits()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "agent:\n  max_concurrent_agents_by_state:\n    In Review: two\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "malformed state limits should fail");

        Assert.IsType<InvalidInteger>(error.Error);
        Assert.Equal("agent.max_concurrent_agents_by_state",
            ((InvalidInteger)error.Error).Field);
    }

    [Fact]
    public void RejectsNonPositivePerStateConcurrencyLimits()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "agent:\n  max_concurrent_agents_by_state:\n    In Review: 0\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var error = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(error.IsErr, "non-positive state limits should fail");

        Assert.IsType<InvalidField>(error.Error);
        Assert.Equal("agent.max_concurrent_agents_by_state",
            ((InvalidField)error.Error).Field);
    }

    [Fact]
    public void ResolvesSelectedHarnessModelAndEnvironmentOverrides()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "routing:\n  harness: openhands_agent_server\n  model: workflow-model\n  model_profile: workflow-profile\n" +
            "---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(
            ("LINEAR_API_KEY", "linear-token"),
            ("OPENSYMPHONY_HARNESS", "codex_app_server"),
            ("OPENSYMPHONY_MODEL", "env-model"),
            ("OPENSYMPHONY_MODEL_PROFILE", "env-profile"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "routing selection should resolve");

        Assert.Equal("codex_app_server", resolved.Value.Config.Routing.Harness);
        Assert.Equal("env-model", resolved.Value.Config.Routing.Model);
        Assert.Equal("env-profile", resolved.Value.Config.Routing.ModelProfile);
        Assert.True(resolved.Value.Config.Routing.HarnessFromEnv);
        Assert.True(resolved.Value.Config.Routing.ModelFromEnv);
        Assert.True(resolved.Value.Config.Routing.ModelProfileFromEnv);
    }

    [Fact]
    public void SelectedOpenHandsModelOverridesConversationModel()
    {
        var workflow = WorkflowLoader.ParseWorkflow(
            "---\ntracker:\n  kind: linear\n  project_slug: sample-project\n  active_states:\n    - Todo\n  terminal_states:\n    - Done\n" +
            "routing:\n  harness: openhands_agent_server\n  model: selected-openhands-model\n" +
            "openhands:\n  conversation:\n    agent:\n      llm:\n        model: workflow-openhands-model\n---\n{{ issue.identifier }}\n");
        Assert.True(workflow.IsOk, "workflow should parse");
        var env = Env(("LINEAR_API_KEY", "linear-token"));

        var resolved = WorkflowResolver.ResolveWorkflow(workflow.Value, "/repo", env);
        Assert.True(resolved.IsOk, "selected OpenHands model should resolve");

        var llm = resolved.Value.Extensions.OpenHands.Conversation.Agent.Llm;
        Assert.NotNull(llm);
        Assert.Equal("selected-openhands-model", llm!.Model);
    }
}
