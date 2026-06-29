using System.Text.Json;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.GatewaySchema.Tests;

// ht: minimal port of gateway schema roundtrip tests.

public class GatewaySchemaTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void SchemaVersion_Roundtrips()
    {
        var version = SchemaVersion.V1();
        var json = JsonSerializer.Serialize(version, _options);
        var back = JsonSerializer.Deserialize<SchemaVersion>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(1, back.Major);
        Assert.Equal(0, back.Minor);
        Assert.Equal(0, back.Patch);
        Assert.Equal("1.0.0", version.AsStr());
    }

    [Fact]
    public void GatewaySchemaVersion_Constant_Matches()
    {
        Assert.Equal("1.0.0", GatewaySchemaConstants.Version);
    }

    [Fact]
    public void StreamCursor_Roundtrips()
    {
        var cursor = StreamCursor.New(42, "events").WithTimestampAnchor(1700000000);
        var json = JsonSerializer.Serialize(cursor, _options);
        var back = JsonSerializer.Deserialize<StreamCursor>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(42u, back.Sequence);
        Assert.Equal("events", back.Partition);
        Assert.Equal(1700000000ul, back.TimestampAnchor);
    }

    [Fact]
    public void PageCursor_Roundtrips()
    {
        var cursor = PageCursor.First(50);
        var json = JsonSerializer.Serialize(cursor, _options);
        var back = JsonSerializer.Deserialize<PageCursor>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(string.Empty, back.PageToken);
        Assert.Equal(50u, back.PageSize);
    }

    [Fact]
    public void EntityRef_Roundtrips()
    {
        var entityRef = new EntityRef(EntityKind.Issue, "issue-1", "COE-390");
        var json = JsonSerializer.Serialize(entityRef, _options);
        var back = JsonSerializer.Deserialize<EntityRef>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(EntityKind.Issue, back.Kind);
        Assert.Equal("issue-1", back.Id);
        Assert.Equal("COE-390", back.Identifier);
    }

    [Fact]
    public void GatewayEnvelope_Roundtrips_WithRawPayload()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"content\":\"hello\"}");
        var envelope = GatewayEnvelope.New(
            StreamCursor.New(7, "terminal:run-1"),
            EntityRefFactory.Terminal("term-1"),
            "terminal_frame",
            payload
        );
        var json = JsonSerializer.Serialize(envelope, _options);
        var back = JsonSerializer.Deserialize<GatewayEnvelope>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(7u, back.Cursor.Sequence);
        Assert.Equal(EntityKind.TerminalSession, back.EntityRef.Kind);
        Assert.Equal("term-1", back.EntityRef.Id);
        Assert.Equal("terminal_frame", back.EventKind);
        Assert.True(back.Payload.HasValue);
        Assert.True(back.RawPayload.HasValue);
    }

    [Fact]
    public void GatewayEnvelope_Roundtrips_FromRawPayload()
    {
        var rawPayload = JsonSerializer.Deserialize<JsonElement>("{\"unknown_field\":42}");
        var envelope = GatewayEnvelope.FromRawPayload(
            StreamCursor.New(8, "unknown:run-2"),
            EntityRefFactory.Run("run-2"),
            "future_event",
            rawPayload
        );
        var json = JsonSerializer.Serialize(envelope, _options);
        var back = JsonSerializer.Deserialize<GatewayEnvelope>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(8u, back.Cursor.Sequence);
        Assert.Equal(EntityKind.Run, back.EntityRef.Kind);
        Assert.Equal("future_event", back.EventKind);
        Assert.False(back.Payload.HasValue);
        Assert.True(back.RawPayload.HasValue);
    }

    [Fact]
    public void DashboardSnapshot_Roundtrips()
    {
        var snapshot = new DashboardSnapshot(
            SchemaVersion.V1(),
            DateTimeOffset.UtcNow,
            1,
            GatewayHealth.Healthy,
            new GatewayMetrics(2, 0, 1024, 512, 256, 0),
            [new ProjectSummary("proj-1", "OpenSymphony", 3, 12, 2, 5, 0)],
            [new SnapshotEventSummary(DateTimeOffset.UtcNow, "COE-390", SnapshotEventKind.WorkerStarted, "Run started")]
        );
        var json = JsonSerializer.Serialize(snapshot, _options);
        var back = JsonSerializer.Deserialize<DashboardSnapshot>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(1ul, back.Sequence);
        Assert.Equal(GatewayHealth.Healthy, back.Health);
        Assert.Single(back.Projects);
        Assert.Equal("proj-1", back.Projects[0].ProjectId);
    }

    [Fact]
    public void TaskGraphNode_Roundtrips()
    {
        var node = new TaskGraphNode(
            SchemaVersion.V1(),
            "node-1",
            TaskGraphNodeKind.Issue,
            "COE-390",
            "Gateway Schemas",
            "In Progress",
            TaskGraphStateCategory.InProgress,
            1,
            "milestone-1",
            ["sub-1"],
            [],
            "https://linear.app/trilogy-ai-coe/issue/COE-390",
            "leonardogonzalez/coe-390",
            ["foundation", "contracts"],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            300,
            null
        );
        var json = JsonSerializer.Serialize(node, _options);
        var back = JsonSerializer.Deserialize<TaskGraphNode>(json, _options);
        Assert.NotNull(back);
        Assert.Equal("node-1", back.NodeId);
        Assert.Equal(TaskGraphNodeKind.Issue, back.Kind);
        Assert.Equal(TaskGraphStateCategory.InProgress, back.StateCategory);
        Assert.Single(back.Children);
    }

    [Fact]
    public void ModelSettings_Roundtrips_And_RedactsSecretMaterial()
    {
        var settings = ModelSettingsResponse.LocalDefault(false);
        var json = JsonSerializer.Serialize(settings, _options);
        var back = JsonSerializer.Deserialize<ModelSettingsResponse>(json, _options);

        Assert.NotNull(back);
        Assert.Equal(SchemaVersion.V1(), back.SchemaVersion);
        Assert.Contains(back.Profiles, p =>
            p.Id == "openhands-env-api-key" &&
            p.CredentialReference.Reference == "LLM_API_KEY" &&
            p.Model.Reference == "LLM_MODEL" &&
            p.BaseUrl!.Reference == "LLM_BASE_URL" &&
            p.CompatibleHarnesses.SequenceEqual(["openhands_agent_server"]) &&
            p.Status == CredentialStatusKind.LoggedOut
        );
        Assert.Contains(back.Profiles, p =>
            p.StorageMode == CredentialStorageMode.HostedBroker &&
            p.CredentialReference.Redacted &&
            p.Status == CredentialStatusKind.Unsupported
        );
        Assert.DoesNotContain("sk-live-secret", json);
        Assert.DoesNotContain("refresh_token", json);
    }

    [Fact]
    public void CodexLocalReadiness_MapsSupportedCommandOutput()
    {
        var readiness = new CodexLocalReadiness(
            "codex",
            "codex-cli 0.138.0",
            CredentialStatusKind.Installed,
            CredentialStatusKind.Installed,
            CredentialStatusKind.Installed,
            CredentialStatusKind.Installed,
            "codex_cli_supported_commands",
            "Codex CLI is ready",
            "codex login --device-auth",
            "codex status",
            "codex logout"
        );
        var settings = ModelSettingsResponse.LocalWithCodexReadiness(false, readiness);

        Assert.Equal("codex-cli 0.138.0", settings.CodexLocalReadiness.Version);
        Assert.Equal(CredentialStatusKind.Installed, settings.CodexLocalReadiness.CliStatus);
        Assert.Equal(CredentialStatusKind.Installed, settings.CodexLocalReadiness.AppServerStatus);
        Assert.Equal(CredentialStatusKind.Installed, settings.CodexLocalReadiness.LoginStatus);
        Assert.Equal(CredentialStatusKind.Installed, settings.CodexLocalReadiness.SubscriptionStatus);
        Assert.Contains("ready", settings.CodexLocalReadiness.Detail);
    }

    [Fact]
    public void RunDetail_Roundtrips()
    {
        var run = new RunDetail
        {
            SchemaVersion = SchemaVersion.V1(),
            RunId = "run-1",
            IssueId = "issue-1",
            IssueIdentifier = "COE-390",
            WorkerId = "worker-1",
            Status = RunStatus.Running,
            LifecycleState = RunLifecycleState.Running,
            ClaimedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            TurnCount = 3,
            MaxTurns = 8,
            InputTokens = 1024,
            OutputTokens = 512,
            CacheReadTokens = 256,
            RuntimeSeconds = 120,
            ConversationId = "conv-1",
            WorkspacePath = "/tmp/workspaces/COE-390",
            WorkspaceId = "COE-390",
            HarnessType = "openhands",
            Summary = "Processing run",
            AllowedActions = [],
            SafeActions = new SafeActions()
        };
        var json = JsonSerializer.Serialize(run, _options);
        var back = JsonSerializer.Deserialize<RunDetail>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(RunStatus.Running, back.Status);
        Assert.Equal("COE-390", back.IssueIdentifier);
        Assert.Equal(3u, back.TurnCount);
    }

    [Fact]
    public void TerminalFrame_Roundtrips()
    {
        var frame = new TerminalFrame
        {
            SchemaVersion = SchemaVersion.V1(),
            FrameSequence = 1,
            StreamId = "stream-1",
            RunId = "run-1",
            TerminalSessionId = "term-1",
            FrameKind = TerminalFrameKind.Stdout,
            Encoding = TerminalEncoding.Utf8,
            Content = "hello world\n",
            Timestamp = DateTimeOffset.UtcNow,
            Association = new TerminalLogAssociation("run-1", "workspace-1")
        };
        var json = JsonSerializer.Serialize(frame, _options);
        var back = JsonSerializer.Deserialize<TerminalFrame>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(1u, back.FrameSequence);
        Assert.Equal(TerminalFrameKind.Stdout, back.FrameKind);
        Assert.Equal(TerminalEncoding.Utf8, back.Encoding);
        Assert.Equal("hello world\n", back.Content);
    }

    [Fact]
    public void ApprovalRequest_Roundtrips()
    {
        var proposedAction = JsonSerializer.Deserialize<JsonElement>("{\"path\":\"src/main.rs\",\"content\":\"fn main() {}\"}");
        var req = new ApprovalRequest(
            SchemaVersion.V1(),
            "apr-1",
            "run-1",
            "issue-1",
            ApprovalKind.ToolUse,
            "Approve file write",
            "Agent wants to write to src/main.rs",
            proposedAction,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            ApprovalStatus.Pending,
            "corr-1"
        );
        var json = JsonSerializer.Serialize(req, _options);
        var back = JsonSerializer.Deserialize<ApprovalRequest>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(ApprovalKind.ToolUse, back.Kind);
        Assert.Equal(ApprovalStatus.Pending, back.Status);
        Assert.Equal("corr-1", back.CorrelationId);
    }

    [Fact]
    public void ActionReceipt_Roundtrips()
    {
        var receipt = ActionReceipt.Accepted("act-1", "corr-1", ActionKind.Retry);
        var json = JsonSerializer.Serialize(receipt, _options);
        var back = JsonSerializer.Deserialize<ActionReceipt>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(ActionStatus.Accepted, back.Status);
        Assert.Equal("corr-1", back.CorrelationId);
        Assert.Equal("act-1", back.ActionId);
    }

    [Fact]
    public void PlanningArtifact_Roundtrips()
    {
        var artifact = new PlanningArtifact(
            SchemaVersion.V1(),
            "art-1",
            "sess-1",
            PlanningArtifactKind.MilestoneDraft,
            "M1: Gateway Contract",
            "# Milestone\n\nDraft gateway schemas.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "planning-agent",
            false,
            false
        );
        var json = JsonSerializer.Serialize(artifact, _options);
        var back = JsonSerializer.Deserialize<PlanningArtifact>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(PlanningArtifactKind.MilestoneDraft, back.Kind);
        Assert.False(back.Approved);
    }

    [Fact]
    public void PlanningSessionSummary_Roundtrips()
    {
        var sess = new PlanningSessionSummary(
            SchemaVersion.V1(),
            "sess-1",
            "proj-1",
            "Q3 Planning",
            PlanningSessionStatus.Draft,
            null,
            0,
            3,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );
        var json = JsonSerializer.Serialize(sess, _options);
        var back = JsonSerializer.Deserialize<PlanningSessionSummary>(json, _options);
        Assert.NotNull(back);
        Assert.Equal(PlanningSessionStatus.Draft, back.Status);
    }

    [Fact]
    public void HarnessCapability_OpenHandsAgentServer_Roundtrips()
    {
        var capability = HarnessKind.OpenHandsAgentServer.Capability();
        var json = JsonSerializer.Serialize(capability, _options);
        var back = JsonSerializer.Deserialize<HarnessCapability>(json, _options);
        Assert.NotNull(back);
        Assert.Equal("openhands_agent_server", back.Kind);
        Assert.True(back.Available);
        Assert.True(back.Actions.StartRun);
        Assert.True(back.Actions.SendUserMessage);
        Assert.True(back.Actions.Retry);
        Assert.True(back.Actions.Cancel);
        Assert.False(back.Actions.Pause);
        Assert.False(back.Actions.Resume);
    }

    [Fact]
    public void HarnessKind_Parse_ReturnsCorrectKind()
    {
        Assert.Equal(HarnessKind.OpenHandsAgentServer, HarnessKindExtensions.Parse("openhands_agent_server"));
        Assert.Equal(HarnessKind.CodexAppServer, HarnessKindExtensions.Parse("codex_app_server"));
        Assert.Equal(HarnessKind.RustNative, HarnessKindExtensions.Parse("rust_native"));
        Assert.Null(HarnessKindExtensions.Parse("unknown"));
    }

    [Fact]
    public void HarnessKind_AsStr_ReturnsCorrectString()
    {
        Assert.Equal("openhands_agent_server", HarnessKind.OpenHandsAgentServer.AsStr());
        Assert.Equal("codex_app_server", HarnessKind.CodexAppServer.AsStr());
        Assert.Equal("rust_native", HarnessKind.RustNative.AsStr());
    }
}