using System.Text;
using System.Text.Json;
using OpenSymphony.Cli.OrchestratorRun;
using OpenSymphony.Domain;
using OpenSymphony.Linear;
using OpenSymphony.OpenHands;
using OpenSymphony.TestKit;
using OpenSymphony.Workspace;

namespace OpenSymphony.Cli.Tests;

/// <summary>
/// End-to-end integration tests for RunOrchestrator using fake services.
/// </summary>
public class RunIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public RunIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"opensymphony-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public async Task RunOrchestrator_WithFakeServices_CompletesOneIssue()
    {
        // Arrange: fake OpenHands server
        using var fakeServer = new FakeOpenHandsServer(new FakeOpenHandsConfig
        {
            RunTerminalStatus = "finished",
            InitialExecutionStatus = "idle"
        });

        var issueId = "issue-123";
        var issueIdentifier = "TEST-1";
        var issueTitle = "Fake integration issue";
        var issueState = "Todo";

        WriteConfigAndWorkflow(fakeServer.BaseUrl, issueState);
        var configPath = Path.Combine(_tempDir, "config.yaml");

        var tracker = new FakeLinearClient(issueId, issueIdentifier, issueTitle, issueState);
        var snapshots = new List<ControlPlaneDaemonSnapshot>();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act: run the orchestrator against fakes
        await RunOrchestrator.RunOrchestratorAsync(
            configPath,
            dryRun: false,
            ct: cts.Token,
            trackerFactory: _ => tracker,
            transportFactory: (_, _) =>
            {
                var transport = new TransportConfig(fakeServer.BaseUrl);
                var client = new OpenHandsClient(
                    transport,
                    fakeServer.CreateClient(),
                    async (uri, ct) =>
                    {
                        var socket = fakeServer.CreateWebSocketClient();
                        await socket.ConnectAsync(uri, ct);
                        return socket;
                    });
                return (client, null);
            },
            snapshotPublished: snapshot =>
            {
                lock (snapshots) { snapshots.Add(snapshot); }
            });

        // Assert: workspace directory created
        var workspacePath = Path.Combine(_tempDir, "workspaces", issueIdentifier);
        Assert.True(Directory.Exists(workspacePath), $"workspace directory should exist: {workspacePath}");

        // Diagnostic: print final snapshot
        ControlPlaneDaemonSnapshot? diagnosticSnapshot;
        lock (snapshots) { diagnosticSnapshot = snapshots.LastOrDefault(); }
        if (diagnosticSnapshot is not null)
        {
            Console.WriteLine($"Final snapshot: {diagnosticSnapshot.Issues.Count} issues");
            foreach (var i in diagnosticSnapshot.Issues)
            {
                Console.WriteLine($"  {i.Identifier}: runtime={i.RuntimeState}, outcome={i.LastOutcome}, path={i.WorkspacePathSuffix}");
            }
        }
        var runDir = Path.Combine(workspacePath, ".opensymphony", "runs");
        Console.WriteLine($"Run dir exists: {Directory.Exists(runDir)}");
        if (Directory.Exists(runDir))
        {
            foreach (var f in Directory.GetFiles(runDir))
                Console.WriteLine($"  run file: {f}");
        }
        var openhandsDir = Path.Combine(workspacePath, ".opensymphony", "openhands");
        Console.WriteLine($"Openhands dir exists: {Directory.Exists(openhandsDir)}");
        if (Directory.Exists(openhandsDir))
        {
            foreach (var f in Directory.GetFiles(openhandsDir))
            {
                Console.WriteLine($"  openhands file: {f}");
                if (f.EndsWith("last-conversation-state.json"))
                {
                    Console.WriteLine("--- last-conversation-state.json ---");
                    Console.WriteLine(await File.ReadAllTextAsync(f));
                    Console.WriteLine("---");
                }
            }
        }
        var requestPath = Path.Combine(openhandsDir, "create-conversation-request.json");
        Console.WriteLine($"Create request exists: {File.Exists(requestPath)}");

        // Assert: conversation manifest written
        var manifestPath = Path.Combine(workspacePath, ".opensymphony", "conversation.json");
        Assert.True(File.Exists(manifestPath), "conversation manifest should be written");
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
        Assert.Equal(issueIdentifier, manifest.GetProperty("identifier").GetString());
        Assert.True(manifest.GetProperty("conversation_id").GetGuid() != Guid.Empty);

        // Assert: run manifest written
        var runManifestPath = Path.Combine(workspacePath, ".opensymphony", "run.json");
        Assert.True(File.Exists(runManifestPath), "run manifest should be written");
        var runManifestJson = await File.ReadAllTextAsync(runManifestPath);
        var runManifest = JsonSerializer.Deserialize<JsonElement>(runManifestJson);
        Assert.Equal("succeeded", runManifest.GetProperty("status").GetString());

        // Assert: snapshot published with at least one issue
        ControlPlaneDaemonSnapshot? final;
        lock (snapshots) { final = snapshots.LastOrDefault(); }
        Assert.NotNull(final);
        Assert.Single(final.Issues);
        var issue = final.Issues[0];
        Assert.Equal(issueIdentifier, issue.Identifier);
        Assert.Equal(issueTitle, issue.Title);
        Assert.Equal(issueState, issue.TrackerState);
        Assert.Equal(ControlPlaneIssueRuntimeState.Completed, issue.RuntimeState);
        Assert.Equal(ControlPlaneWorkerOutcome.Completed, issue.LastOutcome);
    }

    [Fact]
    public void IssueSessionPromptKind_DeserializesFromManifest()
    {
        var json = "{\"last_prompt_kind\":\"full\"}";
        var manifest = JsonSerializer.Deserialize<OpenSymphony.OpenHands.IssueConversationManifest>(json, OpenSymphony.OpenHands.OpenHandsJsonOptions.Default);
        Assert.NotNull(manifest);
        Assert.Equal(OpenSymphony.OpenHands.IssueSessionPromptKind.Full, manifest.LastPromptKind);
    }

    [Fact]
    public async Task RunOrchestrator_DryRun_LoadsConfigAndExits()
    {
        // Arrange
        WriteConfigAndWorkflow("http://127.0.0.1:8000", "Todo");
        var configPath = Path.Combine(_tempDir, "config.yaml");

        var output = new StringBuilder();
        var originalOut = Console.Out;
        Console.SetOut(new StringWriter(output));
        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await RunOrchestrator.RunOrchestratorAsync(configPath, dryRun: true, ct: cts.Token);

            var text = output.ToString();
            Assert.Contains("Dry run", text);
            Assert.Contains("config loaded", text);

            // No workspace should be created in dry-run mode
            var workspacePath = Path.Combine(_tempDir, "workspaces");
            Assert.False(Directory.Exists(workspacePath), "dry run should not create workspaces");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private void WriteConfigAndWorkflow(string openHandsBaseUrl, string activeState)
    {
        var config = $@"
target_repo: {_tempDir.Replace("\\", "/")}
control_plane:
  bind: ""127.0.0.1:0""
memory:
  auto_capture: false
".Trim();
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), config);

        var workflow = $@"
---
tracker:
  kind: linear
  api_key: fake-key
  project_slug: fake-project
  active_states:
    - {activeState}
  terminal_states:
    - Done
polling:
  interval_ms: 500
workspace:
  root: ./workspaces
agent:
  max_concurrent_agents: 1
  max_turns: 1
  max_retry_backoff_ms: 1000
  stall_timeout_ms: 5000
openhands:
  transport:
    base_url: {openHandsBaseUrl}
  websocket:
    ready_timeout_ms: 5000
routing:
  harness: openhands_agent_server
---
Work on issue {{{{issue.identifier}}}}: {{{{issue.title}}}}.
".Trim();
        File.WriteAllText(Path.Combine(_tempDir, "WORKFLOW.md"), workflow);
    }
}
