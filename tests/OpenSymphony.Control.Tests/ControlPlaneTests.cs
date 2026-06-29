using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using OpenSymphony.Control;
using OpenSymphony.Domain;

namespace OpenSymphony.Control.Tests;

public static class Fixture
{
    public static readonly DateTimeOffset BaseTime =
        new(2026, 3, 21, 20, 0, 0, TimeSpan.Zero);

    public static ControlPlaneDaemonSnapshot Snapshot(ulong step)
    {
        var now = BaseTime.AddSeconds(step);
        return new ControlPlaneDaemonSnapshot(
            GeneratedAt: now,
            Daemon: new ControlPlaneDaemonStatus(
                ControlPlaneDaemonState.Ready, now, "/tmp/opensymphony", "ready"),
            AgentServer: new ControlPlaneAgentServerStatus(
                true, "http://127.0.0.1:3000", 2, "healthy"),
            MemoryServer: ControlPlaneMemoryServerStatus.Default,
            Metrics: new ControlPlaneMetricsSnapshot(
                RunningIssues: 1, RetryQueueDepth: 0,
                InputTokens: 2048, OutputTokens: 2048, CacheReadTokens: 512,
                TotalTokens: 4096 + step, TotalCostMicros: 120_000),
            Issues:
            [
                new ControlPlaneIssueSnapshot(
                    Identifier: "COE-255",
                    Title: "Observability and FrankenTUI",
                    TrackerState: "In Progress",
                    RuntimeState: ControlPlaneIssueRuntimeState.Running,
                    LastOutcome: ControlPlaneWorkerOutcome.Running,
                    LastEventAt: now,
                    ConversationIdSuffix: "c0e255",
                    WorkspacePathSuffix: "COE-255",
                    RetryCount: 0,
                    ClaimedAt: null, StartedAt: null, FinishedAt: null,
                    TurnCount: 0, MaxTurns: 0, RuntimeSeconds: 0,
                    Blocked: false, BlockedBy: [],
                    ServerBaseUrl: "http://127.0.0.1:3000",
                    TransportTarget: "loopback",
                    HttpAuthMode: "none",
                    WebsocketAuthMode: "none",
                    WebsocketQueryParamName: null,
                    RecentEvents: [], ModifiedFiles: [],
                    InputTokens: 1024, OutputTokens: 512,
                    CacheReadTokens: 256, TotalTokens: 0,
                    Detached: false, CancelAcknowledged: false, CancelFailed: false),
            ],
            RecentEvents:
            [
                new ControlPlaneRecentEvent(
                    now, "COE-255",
                    ControlPlaneRecentEventKind.SnapshotPublished,
                    $"published step {step}"),
            ]);
    }
}

public class SnapshotStoreTests
{
    [Fact]
    public async Task LaggedReceiversResumeFromLatestSnapshotWithoutRegressing()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        var subscriber = store.Subscribe();
        var lastSent = store.Current().Sequence; // 1

        for (ulong step = 1; step <= 80; step++)
            store.Publish(Fixture.Snapshot(step));

        var latest = await SnapshotCatchUp.NextSnapshotEnvelope(store, subscriber, lastSent);
        Assert.NotNull(latest);
        Assert.Equal(81UL, latest!.Sequence);

        var expected = store.Publish(Fixture.Snapshot(81));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var resumed = await SnapshotCatchUp.NextSnapshotEnvelope(store, subscriber, latest.Sequence);
        Assert.NotNull(resumed);
        Assert.Equal(expected.Sequence, resumed!.Sequence);
        Assert.Equal("published step 81", resumed.Snapshot.RecentEvents[0].Summary);
    }

    [Fact]
    public void LaggedCatchUpReturnsBeforeBacklogDrains()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        var subscriber = store.Subscribe();
        var lastSent = store.Current().Sequence; // 1

        for (ulong step = 1; step <= 80; step++)
            store.Publish(Fixture.Snapshot(step));

        var latest = SnapshotCatchUp.CatchUpLaggedReceiver(store, lastSent);
        Assert.NotNull(latest);
        Assert.Equal(81UL, latest!.Sequence);

        // Channel should still have buffered items (backlog not drained)
        Assert.True(subscriber.Channel.Reader.TryRead(out var buffered));
        Assert.True(buffered.Sequence < latest.Sequence);
    }

    [Fact]
    public void PublishIncrementsSequence()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        Assert.Equal(1UL, store.Current().Sequence);

        var env1 = store.Publish(Fixture.Snapshot(1));
        Assert.Equal(2UL, env1.Sequence);
        Assert.Equal(2UL, store.Current().Sequence);

        var env2 = store.Publish(Fixture.Snapshot(2));
        Assert.Equal(3UL, env2.Sequence);
    }

    [Fact]
    public void SubscribeReceivesPublishedSnapshots()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        var subscriber = store.Subscribe();

        store.Publish(Fixture.Snapshot(1));
        Assert.True(subscriber.Channel.Reader.TryRead(out var env));
        Assert.Equal(2UL, env.Sequence);
    }
}

public class ControlPlaneClientUrlTests
{
    [Fact]
    public void PreservesPathPrefixesWithoutTrailingSlashes()
    {
        var client = new ControlPlaneClient(new Uri("http://proxy/opensymphony"));
        var snapshotUrl = client.JoinPath("api/v1/snapshot");
        var eventsUrl = client.JoinPath("api/v1/control/events");

        Assert.Equal("http://proxy/opensymphony/api/v1/snapshot", snapshotUrl.ToString());
        Assert.Equal("http://proxy/opensymphony/api/v1/control/events", eventsUrl.ToString());
    }

    [Fact]
    public void HandlesRootBaseUrlWithTrailingSlash()
    {
        var client = new ControlPlaneClient(new Uri("http://proxy/"));
        var snapshotUrl = client.JoinPath("api/v1/snapshot");
        Assert.Equal("http://proxy/api/v1/snapshot", snapshotUrl.ToString());
    }
}

public class ControlPlaneServerIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private Uri? _baseUrl;

    public async Task InitializeAsync()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        var server = new ControlPlaneServer(store);
        _app = server.BuildApp();
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        var addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        _baseUrl = new Uri(addresses.Addresses.First());
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task ServesSnapshot()
    {
        var client = new ControlPlaneClient(_baseUrl!);
        var current = await client.FetchSnapshotAsync();
        Assert.Equal(1UL, current.Sequence);
        Assert.Equal("COE-255", current.Snapshot.Issues[0].Identifier);
    }

    [Fact]
    public async Task StreamsUpdates()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        var server = new ControlPlaneServer(store);
        await using var app = server.BuildApp();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        var baseUrl = new Uri(addresses.Addresses.First());

        var client = new ControlPlaneClient(baseUrl);
        var current = await client.FetchSnapshotAsync();
        Assert.Equal(1UL, current.Sequence);

        using var stream = await client.StreamUpdatesAsync();
        var initial = await stream.NextAsync();
        Assert.NotNull(initial);
        Assert.Equal(1UL, initial!.Sequence);

        var expected = store.Publish(Fixture.Snapshot(1));

        var streamed = await stream.NextAsync();
        Assert.NotNull(streamed);
        Assert.Equal(expected.Sequence, streamed!.Sequence);
        Assert.Equal("published step 1", streamed.Snapshot.RecentEvents[0].Summary);
    }
}

public class ControlPlaneServerPrefixedIntegrationTests
{
    [Fact]
    public async Task ClientHandlesPathPrefixedBaseUrlWithoutTrailingSlash()
    {
        var store = new SnapshotStore(Fixture.Snapshot(0));
        var server = new ControlPlaneServer(store);
        await using var app = server.BuildApp("/opensymphony");
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        var baseAddress = addresses.Addresses.First().TrimEnd('/');
        var baseUrl = new Uri($"{baseAddress}/opensymphony");

        var client = new ControlPlaneClient(baseUrl);
        var current = await client.FetchSnapshotAsync();
        Assert.Equal(1UL, current.Sequence);
        Assert.Equal("COE-255", current.Snapshot.Issues[0].Identifier);

        using var stream = await client.StreamUpdatesAsync();
        var initial = await stream.NextAsync();
        Assert.NotNull(initial);
        Assert.Equal(1UL, initial!.Sequence);
    }
}
