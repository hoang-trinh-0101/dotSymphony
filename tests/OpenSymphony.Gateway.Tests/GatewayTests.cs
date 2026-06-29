using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSymphony.Control;
using OpenSymphony.Domain;
using OpenSymphony.Gateway;
using OpenSymphony.GatewaySchema;
using Xunit;

namespace OpenSymphony.Gateway.Tests;

// ht: representative test port from Rust gateway.rs (3070 lines).
//   Focus: health, snapshot, action dispatch, dashboard endpoints.
//   Real implementations using TestServer, not stubs.

public class GatewayTests : IDisposable
{
    private readonly SnapshotStore _store;
    private readonly TestServer _server;
    private readonly HttpClient _client;

    public GatewayTests()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ControlPlaneDaemonSnapshot(
            now,
            new ControlPlaneDaemonStatus(
                ControlPlaneDaemonState.Ready,
                now,
                "/tmp/opensymphony",
                "ready"),
            new ControlPlaneAgentServerStatus(
                true,
                "http://127.0.0.1:3000",
                2,
                "healthy"),
            ControlPlaneMemoryServerStatus.Default,
            new ControlPlaneMetricsSnapshot(
                1,
                0,
                2048,
                2048,
                512,
                4096,
                120_000),
            new List<ControlPlaneIssueSnapshot>(),
            new List<ControlPlaneRecentEvent>());

        _store = new SnapshotStore(snapshot);

        // ht: Simple TestServer with direct endpoint configuration
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(_store);
            })
            .Configure(app =>
            {
                var gateway = new GatewayServer(_store);
                var webApp = gateway.BuildApp();
                
                // ht: Copy all endpoints from webApp to app
                var routes = (IEndpointRouteBuilder)webApp;
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    foreach (var dataSource in routes.DataSources)
                    {
                        endpoints.DataSources.Add(dataSource);
                    }
                });
            }));
        
        _client = _server.CreateClient();
    }

    [Fact]
    public async Task Healthz_ReturnsOkStatus()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var healthz = JsonSerializer.Deserialize<GatewayHealthzResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(healthz);
        Assert.Equal("ok", healthz.Status);
        Assert.Equal(1UL, healthz.CurrentSequence);
    }

    [Fact]
    public async Task ControlSnapshot_ReturnsCurrentSnapshot()
    {
        var response = await _client.GetAsync("/api/v1/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(envelope);
        Assert.Equal(1UL, envelope.Sequence);
        Assert.Equal(ControlPlaneDaemonState.Ready, envelope.Snapshot.Daemon.State);
    }

    [Fact]
    public async Task DashboardSnapshot_ReturnsDashboardView()
    {
        var response = await _client.GetAsync("/api/v1/dashboard/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var dashboard = JsonSerializer.Deserialize<DashboardSnapshot>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(dashboard);
        Assert.Equal(GatewayHealth.Healthy, dashboard.Health);
        Assert.Single(dashboard.Projects);
        Assert.Equal("OpenSymphony", dashboard.Projects[0].Name);
    }

    [Fact]
    public async Task Capabilities_ReturnsCapabilityList()
    {
        var response = await _client.GetAsync("/api/v1/capabilities");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonSerializer.Deserialize<GatewayCapabilities>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(capabilities);
        Assert.Equal(3, capabilities.Transports.Count);
        Assert.Equal(3, capabilities.Harnesses.Count);
    }

    [Fact]
    public async Task ModelSettings_ReturnsSettings()
    {
        var response = await _client.GetAsync("/api/v1/model-settings");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var settings = JsonSerializer.Deserialize<ModelSettingsResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(settings);
        Assert.NotNull(settings.Profiles);
        Assert.True(settings.Profiles.Count > 0);
    }

    [Fact]
    public async Task ActionDispatch_WithValidRetry_Accepts()
    {
        // Add a completed issue to snapshot
        var now = DateTimeOffset.UtcNow;
        var issue = new ControlPlaneIssueSnapshot(
            "COE-255",
            "Test Issue",
            "Done",
            ControlPlaneIssueRuntimeState.Completed,
            ControlPlaneWorkerOutcome.Completed,
            now,
            "c0e255",
            "COE-255",
            0,
            now - TimeSpan.FromMinutes(10),
            now - TimeSpan.FromMinutes(9),
            now,
            3,
            8,
            0UL,
            false,
            new List<string>(),
            null,
            null,
            null,
            null,
            null,
            new List<ControlPlaneConversationEvent>(),
            new List<ControlPlaneFileChange>(),
            100UL,
            200UL,
            50UL,
            350UL,
            false,
            false,
            false);

        var updatedSnapshot = _store.Current().Snapshot with
        {
            Issues = new List<ControlPlaneIssueSnapshot> { issue }
        };
        _store.Publish(updatedSnapshot);

        var action = new ActionDispatch(
            SchemaVersion.V1(),
            "test-correlation",
            ActionKind.Retry,
            new ActionTarget(EntityKind.Issue, "COE-255"),
            null,
            null);

        var json = JsonSerializer.Serialize(action, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/actions/dispatch", content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var receipt = JsonSerializer.Deserialize<ActionReceipt>(responseContent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(receipt);
        Assert.Equal(ActionStatus.Accepted, receipt.Status);
    }

    [Fact]
    public async Task ActionDispatch_WithUnknownIssue_Rejects()
    {
        var action = new ActionDispatch(
            SchemaVersion.V1(),
            "test-correlation",
            ActionKind.Retry,
            new ActionTarget(EntityKind.Issue, "UNKNOWN-123"),
            null,
            null);

        var json = JsonSerializer.Serialize(action, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/actions/dispatch", content);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var receipt = JsonSerializer.Deserialize<ActionReceipt>(responseContent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(receipt);
        Assert.Equal(ActionStatus.Rejected, receipt.Status);
        Assert.Contains("not found", receipt.Reason ?? "");
    }

    [Fact]
    public async Task Projects_ReturnsProjectList()
    {
        var response = await _client.GetAsync("/api/v1/projects");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var projects = JsonSerializer.Deserialize<ProjectList>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(projects);
        Assert.NotNull(projects.Projects);
    }

    [Fact]
    public async Task ControlEvents_ReturnsEventStream()
    {
        var response = await _client.GetAsync("/api/v1/control/events");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Events_ReturnsEventStream()
    {
        var response = await _client.GetAsync("/api/v1/events");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ModelCredentialStatus_ReturnsCredentialStatuses()
    {
        var response = await _client.GetAsync("/api/v1/model-settings/credential-status");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var statuses = JsonSerializer.Deserialize<CredentialStatusResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(statuses);
        Assert.NotNull(statuses.Statuses);
    }

    [Fact]
    public async Task ProjectDetail_UnknownProject_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/projects/unknown-project");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RunDetail_UnknownRun_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/runs/unknown-run-id");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RunEvents_UnknownRun_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/runs/unknown-run-id/events");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_ReflectsIssueCount()
    {
        var now = DateTimeOffset.UtcNow;
        var issue1 = new ControlPlaneIssueSnapshot(
            "COE-261",
            "Issue 1",
            "Done",
            ControlPlaneIssueRuntimeState.Completed,
            ControlPlaneWorkerOutcome.Completed,
            now,
            "c0e261",
            "COE-261",
            0,
            now - TimeSpan.FromMinutes(10),
            now - TimeSpan.FromMinutes(9),
            now,
            3,
            8,
            0UL,
            false,
            new List<string>(),
            null,
            null,
            null,
            null,
            null,
            new List<ControlPlaneConversationEvent>(),
            new List<ControlPlaneFileChange>(),
            100UL,
            200UL,
            50UL,
            350UL,
            false,
            false,
            false);

        var issue2 = new ControlPlaneIssueSnapshot(
            "COE-262",
            "Issue 2",
            "Done",
            ControlPlaneIssueRuntimeState.Completed,
            ControlPlaneWorkerOutcome.Completed,
            now,
            "c0e262",
            "COE-262",
            0,
            now - TimeSpan.FromMinutes(10),
            now - TimeSpan.FromMinutes(9),
            now,
            3,
            8,
            0UL,
            false,
            new List<string>(),
            null,
            null,
            null,
            null,
            null,
            new List<ControlPlaneConversationEvent>(),
            new List<ControlPlaneFileChange>(),
            100UL,
            200UL,
            50UL,
            350UL,
            false,
            false,
            false);

        var updatedSnapshot = _store.Current().Snapshot with
        {
            Issues = new List<ControlPlaneIssueSnapshot> { issue1, issue2 }
        };
        _store.Publish(updatedSnapshot);

        var response = await _client.GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var healthz = JsonSerializer.Deserialize<GatewayHealthzResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(healthz);
        Assert.Equal("2", healthz.IssueCount);
    }

    [Fact]
    public async Task Healthz_SequenceIncrements()
    {
        // Publish a new snapshot
        var updatedSnapshot = _store.Current().Snapshot with
        {
            GeneratedAt = DateTimeOffset.UtcNow
        };
        _store.Publish(updatedSnapshot);

        var response = await _client.GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var healthz = JsonSerializer.Deserialize<GatewayHealthzResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(healthz);
        Assert.Equal(2UL, healthz.CurrentSequence);
    }

    [Fact]
    public async Task DashboardSnapshot_WithNoIssues_ShowsHealthy()
    {
        var response = await _client.GetAsync("/api/v1/dashboard/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var dashboard = JsonSerializer.Deserialize<DashboardSnapshot>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(dashboard);
        Assert.Equal(GatewayHealth.Healthy, dashboard.Health);
    }

    [Fact]
    public async Task Capabilities_TransportCount()
    {
        var response = await _client.GetAsync("/api/v1/capabilities");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonSerializer.Deserialize<GatewayCapabilities>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Transports.Count > 0);
    }

    [Fact]
    public async Task Capabilities_HarnessCount()
    {
        var response = await _client.GetAsync("/api/v1/capabilities");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonSerializer.Deserialize<GatewayCapabilities>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Harnesses.Count > 0);
    }

    [Fact]
    public async Task ModelSettings_ProfilesNotEmpty()
    {
        var response = await _client.GetAsync("/api/v1/model-settings");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var settings = JsonSerializer.Deserialize<ModelSettingsResponse>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(settings);
        Assert.NotNull(settings.Profiles);
        Assert.True(settings.Profiles.Count > 0);
    }

    [Fact]
    public async Task ControlSnapshot_Sequence()
    {
        var response = await _client.GetAsync("/api/v1/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(envelope);
        Assert.True(envelope.Sequence > 0);
    }

    [Fact]
    public async Task ControlSnapshot_DaemonState()
    {
        var response = await _client.GetAsync("/api/v1/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(envelope);
        Assert.Equal(ControlPlaneDaemonState.Ready, envelope.Snapshot.Daemon.State);
    }

    [Fact]
    public async Task DashboardSnapshot_ProjectsNotEmpty()
    {
        var response = await _client.GetAsync("/api/v1/dashboard/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var dashboard = JsonSerializer.Deserialize<DashboardSnapshot>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard.Projects);
        Assert.True(dashboard.Projects.Count > 0);
    }

    [Fact]
    public async Task SnapshotEnvelope_PublishedAt()
    {
        var response = await _client.GetAsync("/api/v1/snapshot");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.NotNull(envelope);
        Assert.True(envelope.PublishedAt > DateTimeOffset.MinValue);
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
    }
}