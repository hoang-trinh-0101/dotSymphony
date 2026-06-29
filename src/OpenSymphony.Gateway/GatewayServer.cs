using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using OpenSymphony.Control;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Gateway;

// ht: minimal ASP.NET Core gateway server with real handlers.
//   Focus: core snapshot, action dispatch, SSE streaming, and capabilities.

public sealed record GatewayHealthzResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("current_sequence")] ulong CurrentSequence,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("issue_count")] string IssueCount
);

public sealed class GatewayServer
{
    private readonly SnapshotStore _store;
    private readonly InMemoryEventJournal _journal;
    private readonly StreamBroker _broker;
    private readonly ActionHandler _actionHandler;
    private readonly ILinearMutationClient? _linearMutations;
    private readonly CodexReadinessCache _codexReadinessCache;
    private readonly string? _webAssetsDir;
    private WebApplication? _app;

    public GatewayServer(
        SnapshotStore store,
        InMemoryEventJournal? journal = null,
        StreamBroker? broker = null,
        ILinearMutationClient? linearMutations = null,
        string? webAssetsDir = null)
    {
        _store = store;
        _journal = journal ?? new InMemoryEventJournal(10_000, 256);
        _broker = broker ?? new StreamBroker(_journal);
        _actionHandler = new ActionHandler(_journal);
        _linearMutations = linearMutations;
        _codexReadinessCache = new CodexReadinessCache();
        _webAssetsDir = webAssetsDir;
    }

    public WebApplication BuildApp(string? basePath = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var app = builder.Build();

        IEndpointRouteBuilder routes = app;
        if (basePath is not null)
            routes = app.MapGroup(basePath);

        RegisterRoutes(routes);

        return app;
    }

    public async Task StartAsync(IPEndPoint bind, CancellationToken ct = default)
    {
        if (_app is not null)
            throw new InvalidOperationException("Gateway server is already started");

        _app = BuildApp();
        _app.Urls.Clear();
        _app.Urls.Add($"http://{bind}");
        await _app.StartAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_app is null) return;
        await _app.StopAsync(ct);
        await _app.DisposeAsync();
        _app = null;
    }

    public void RegisterRoutes(IEndpointRouteBuilder routes)
    {
        // Core health and snapshot endpoints
        routes.MapGet("/healthz", (Delegate)Healthz);
        routes.MapGet("/api/v1/snapshot", (Delegate)ControlSnapshot);
        routes.MapGet("/api/v1/control/events", (Delegate)ControlEvents);
        routes.MapGet("/api/v1/capabilities", (Delegate)Capabilities);
        routes.MapGet("/api/v1/model-settings", (Delegate)ModelSettings);
        routes.MapGet("/api/v1/model-settings/credential-status", (Delegate)ModelCredentialStatuses);
        routes.MapGet("/api/v1/dashboard/snapshot", (Delegate)DashboardSnapshot);
        routes.MapGet("/api/v1/events", (Delegate)Events);

        // Action dispatch
        routes.MapPost("/api/v1/actions/dispatch", (Delegate)DispatchAction);

        // Run detail endpoints (minimal implementation)
        routes.MapGet("/api/v1/runs/{run_id}", (Delegate)GetRunDetail);
        routes.MapGet("/api/v1/runs/{run_id}/events", (Delegate)GetRunEvents);

        // Project endpoints
        routes.MapGet("/api/v1/projects", (Delegate)ListProjects);
        routes.MapGet("/api/v1/projects/{project_id}", (Delegate)GetProject);

        // WebSocket event stream
        routes.MapGet("/api/v1/streams/events", (Delegate)EventStreamWs);

        // Task graph mutation endpoints
        var mutationState = new TaskGraphMutationState(_journal, _linearMutations);
        var mutationRoutes = routes.MapGroup("/api/v1/taskgraph");
        mutationRoutes.MapPost("/milestone", (Delegate)CreateOrUpdateMilestone);
        mutationRoutes.MapPost("/issue", (Delegate)CreateOrUpdateIssue);
        mutationRoutes.MapPost("/sub-issue", (Delegate)CreateOrUpdateSubIssue);
        mutationRoutes.MapPost("/relation", (Delegate)CreateRelation);
        mutationRoutes.MapPost("/evidence", (Delegate)CreateEvidence);

        // Web assets (if configured)
        if (_webAssetsDir is not null)
        {
            routes.MapGet("/app", (Delegate)WebAssetHandler);
            routes.MapGet("/app/{*path}", (Delegate)WebAssetHandler);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    // =============================================================================
    // Handlers
    // =============================================================================

    private async Task<IResult> Healthz(HttpContext ctx)
    {
        var envelope = _store.Current();
        var response = new GatewayHealthzResponse(
            "ok",
            envelope.Sequence,
            envelope.PublishedAt,
            envelope.Snapshot.IssueCount.ToString()
        );
        return Results.Json(response, JsonOptions);
    }

    private async Task<IResult> ControlSnapshot(HttpContext ctx)
    {
        var envelope = _store.Current();
        return Results.Json(envelope, JsonOptions);
    }

    private async Task<IResult> DashboardSnapshot(HttpContext ctx)
    {
        var envelope = _store.Current();
        var snapshot = ControlPlaneToDashboardSnapshot(envelope);
        return Results.Json(snapshot, JsonOptions);
    }

    private async Task<IResult> DispatchAction(HttpContext ctx)
    {
        var action = await JsonSerializer.DeserializeAsync<ActionDispatch>(
            ctx.Request.Body,
            JsonOptions);

        if (action is null)
            return Results.BadRequest();

        var envelope = _store.Current();
        var receipt = await _actionHandler.Dispatch(action, envelope);

        var statusCode = receipt.Status == ActionStatus.Accepted
            ? StatusCodes.Status200OK
            : DispatchRejectionStatus(receipt);

        return Results.Json(receipt, JsonOptions, contentType: null, statusCode);
    }

    private static int DispatchRejectionStatus(ActionReceipt receipt)
    {
        if (receipt.Reason is null) return StatusCodes.Status400BadRequest;

        var lower = receipt.Reason.ToLowerInvariant();
        if (lower.Contains("permission denied")) return StatusCodes.Status403Forbidden;
        if (lower.Contains("duplicate idempotency key")) return StatusCodes.Status409Conflict;
        if (lower.Contains("not found")) return StatusCodes.Status404NotFound;
        if (lower.Contains("already active") ||
            lower.Contains("unsafe in state") ||
            lower.Contains("only valid on")) return StatusCodes.Status422UnprocessableEntity;

        return StatusCodes.Status400BadRequest;
    }

    private async Task ControlEvents(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var subscriber = _store.Subscribe();
        var initial = _store.Current();
        ulong lastSent = initial.Sequence;

        await WriteSnapshotEvent(ctx.Response, initial);

        var keepaliveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var readTask = SnapshotCatchUp.NextSnapshotEnvelope(_store, subscriber, lastSent);
            var keepaliveTask = Task.Delay(Timeout.InfiniteTimeSpan, keepaliveCts.Token);

            var winner = await Task.WhenAny(readTask, keepaliveTask);
            if (winner == keepaliveTask)
            {
                await ctx.Response.WriteAsync(": keepalive\n\n");
                await ctx.Response.Body.FlushAsync();
                keepaliveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                continue;
            }

            keepaliveCts.Cancel();

            var envelope = await readTask;
            if (envelope is null) break;

            lastSent = envelope.Sequence;
            await WriteSnapshotEvent(ctx.Response, envelope);
        }
    }

    private static async Task WriteSnapshotEvent(HttpResponse response, SnapshotEnvelope envelope)
    {
        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        var sse = $"event: snapshot\nid: {envelope.Sequence}\ndata: {payload}\n\n";
        await response.WriteAsync(sse);
        await response.Body.FlushAsync();
    }

    private async Task<IResult> Capabilities(HttpContext ctx)
    {
        var capabilities = BuildCapabilities();
        return Results.Json(capabilities, JsonOptions);
    }

    private async Task<IResult> ModelSettings(HttpContext ctx)
    {
        var llmApiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        var codexReadiness = await _codexReadinessCache.Readiness("codex");
        var response = ModelSettingsForApiKeyAndCodexReadiness(llmApiKey, codexReadiness);
        return Results.Json(response, JsonOptions);
    }

    private async Task<IResult> ModelCredentialStatuses(HttpContext ctx)
    {
        var llmApiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        var codexReadiness = await _codexReadinessCache.Readiness("codex");
        var modelSettings = ModelSettingsForApiKeyAndCodexReadiness(llmApiKey, codexReadiness);
        var response = CredentialStatusResponse.FromModelSettings(modelSettings);
        return Results.Json(response, JsonOptions);
    }

    private async Task Events(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var subscriber = _journal.Subscribe();
        var allEvents = await _journal.AllEvents();
        ulong lastSent = allEvents.LastOrDefault()?.Sequence ?? 0;

        // Send backlog
        foreach (var evt in allEvents)
        {
            await WriteJournalEvent(ctx.Response, evt);
            lastSent = evt.Sequence;
        }

        // Send live events
        var keepaliveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var readTask = subscriber.Channel.Reader.ReadAsync(ctx.RequestAborted).AsTask();
            var keepaliveTask = Task.Delay(Timeout.InfiniteTimeSpan, keepaliveCts.Token);

            var winner = await Task.WhenAny(readTask, keepaliveTask);
            if (winner == keepaliveTask)
            {
                await ctx.Response.WriteAsync(": keepalive\n\n");
                await ctx.Response.Body.FlushAsync();
                keepaliveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                continue;
            }

            keepaliveCts.Cancel();

            if (await readTask is { } evt)
            {
                await WriteJournalEvent(ctx.Response, evt);
            }
        }
    }

    private static async Task WriteJournalEvent(HttpResponse response, EventRecord evt)
    {
        var payload = JsonSerializer.Serialize(evt, JsonOptions);
        var sse = $"event: event\nid: {evt.Sequence}\ndata: {payload}\n\n";
        await response.WriteAsync(sse);
        await response.Body.FlushAsync();
    }

    // =============================================================================
    // Stub handlers (to be implemented as needed by tests)
    // =============================================================================

    private static async Task<IResult> GetRunDetail(string run_id) =>
        Results.NotFound(new { error = "run detail not yet implemented" });

    private static async Task<IResult> GetRunEvents(string run_id) =>
        Results.NotFound(new { error = "run events not yet implemented" });

    private static async Task<IResult> ListProjects() =>
        Results.Json(new ProjectList(SchemaVersion.V1(), new List<ProjectSummary>()));

    private static async Task<IResult> GetProject(string project_id) =>
        Results.NotFound(new { error = "project not found" });

    private static async Task EventStreamWs(HttpContext ctx) =>
        Results.StatusCode(StatusCodes.Status501NotImplemented);

    private static async Task<IResult> WebAssetHandler(string? path) =>
        Results.StatusCode(StatusCodes.Status501NotImplemented);

    // =============================================================================
    // Task graph mutation handlers
    // =============================================================================

    private async Task<IResult> CreateOrUpdateMilestone(HttpContext ctx)
    {
        if (_linearMutations is null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var request = await JsonSerializer.DeserializeAsync<TaskGraphMilestoneRequest>(
            ctx.Request.Body, JsonOptions);

        if (request is null)
            return Results.BadRequest();

        var result = await _linearMutations.CreateOrUpdateProjectMilestone(
            request, request.CorrelationId);

        return result.Match(
            response => Results.Json(response, JsonOptions),
            error => Results.BadRequest(new { error = error.AsReason() })
        );
    }

    private async Task<IResult> CreateOrUpdateIssue(HttpContext ctx)
    {
        if (_linearMutations is null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var request = await JsonSerializer.DeserializeAsync<TaskGraphIssueRequest>(
            ctx.Request.Body, JsonOptions);

        if (request is null)
            return Results.BadRequest();

        var result = await _linearMutations.CreateOrUpdateIssue(
            request, request.CorrelationId);

        return result.Match(
            response => Results.Json(response, JsonOptions),
            error => Results.BadRequest(new { error = error.AsReason() })
        );
    }

    private async Task<IResult> CreateOrUpdateSubIssue(HttpContext ctx)
    {
        if (_linearMutations is null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var request = await JsonSerializer.DeserializeAsync<TaskGraphSubIssueRequest>(
            ctx.Request.Body, JsonOptions);

        if (request is null)
            return Results.BadRequest();

        var result = await _linearMutations.CreateOrUpdateSubIssue(
            request, request.CorrelationId);

        return result.Match(
            response => Results.Json(response, JsonOptions),
            error => Results.BadRequest(new { error = error.AsReason() })
        );
    }

    private async Task<IResult> CreateRelation(HttpContext ctx)
    {
        if (_linearMutations is null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var request = await JsonSerializer.DeserializeAsync<TaskGraphRelationRequest>(
            ctx.Request.Body, JsonOptions);

        if (request is null)
            return Results.BadRequest();

        var result = await _linearMutations.CreateIssueRelation(
            request, request.CorrelationId);

        return result.Match(
            response => Results.Json(response, JsonOptions),
            error => Results.BadRequest(new { error = error.AsReason() })
        );
    }

    private async Task<IResult> CreateEvidence(HttpContext ctx)
    {
        if (_linearMutations is null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var request = await JsonSerializer.DeserializeAsync<TaskGraphEvidenceRequest>(
            ctx.Request.Body, JsonOptions);

        if (request is null)
            return Results.BadRequest();

        var result = await _linearMutations.CreateEvidenceComment(
            request, request.CorrelationId);

        return result.Match(
            response => Results.Json(response, JsonOptions),
            error => Results.BadRequest(new { error = error.AsReason() })
        );
    }

    // =============================================================================
    // Helper Functions
    // =============================================================================

    public static DashboardSnapshot ControlPlaneToDashboardSnapshot(SnapshotEnvelope envelope)
    {
        var snapshot = envelope.Snapshot;
        var health = DaemonStateToGatewayHealth(snapshot.Daemon.State);
        var metrics = new GatewayMetrics(
            snapshot.Metrics.RunningIssues,
            snapshot.Metrics.RetryQueueDepth,
            snapshot.Metrics.InputTokens,
            snapshot.Metrics.OutputTokens,
            snapshot.Metrics.CacheReadTokens,
            snapshot.Metrics.TotalCostMicros
        );

        var projects = new List<ProjectSummary>
            {
                new(
                    "default",
                    "OpenSymphony",
                    0,
                    (uint)snapshot.Issues.Count,
                    (uint)snapshot.Issues.Count(i => i.RuntimeState == ControlPlaneIssueRuntimeState.Running),
                    (uint)snapshot.Issues.Count(i => i.LastOutcome == ControlPlaneWorkerOutcome.Completed),
                    (uint)snapshot.Issues.Count(i => i.LastOutcome == ControlPlaneWorkerOutcome.Failed)
                )
            };

        var recentEvents = snapshot.RecentEvents.Select(e => new SnapshotEventSummary(
            e.HappenedAt,
            e.IssueIdentifier,
            RecentEventKindToSnapshotEventKind(e.Kind),
            e.Summary
        )).ToList();

        return new DashboardSnapshot(
            SchemaVersion.V1(),
            snapshot.GeneratedAt,
            envelope.Sequence,
            health,
            metrics,
            projects,
            recentEvents
        );
    }

    private static GatewayHealth DaemonStateToGatewayHealth(ControlPlaneDaemonState state) => state switch
    {
        ControlPlaneDaemonState.Ready => GatewayHealth.Healthy,
        ControlPlaneDaemonState.Degraded => GatewayHealth.Degraded,
        ControlPlaneDaemonState.Starting => GatewayHealth.Starting,
        ControlPlaneDaemonState.Stopped => GatewayHealth.Failed,
        _ => GatewayHealth.Failed
    };

    private static SnapshotEventKind RecentEventKindToSnapshotEventKind(ControlPlaneRecentEventKind kind) => kind switch
    {
        ControlPlaneRecentEventKind.WorkerStarted => SnapshotEventKind.WorkerStarted,
        ControlPlaneRecentEventKind.WorkspacePrepared => SnapshotEventKind.WorkspacePrepared,
        ControlPlaneRecentEventKind.StreamAttached => SnapshotEventKind.StreamAttached,
        ControlPlaneRecentEventKind.SnapshotPublished => SnapshotEventKind.SnapshotPublished,
        ControlPlaneRecentEventKind.WorkerCompleted => SnapshotEventKind.WorkerCompleted,
        ControlPlaneRecentEventKind.RetryScheduled => SnapshotEventKind.RetryScheduled,
        ControlPlaneRecentEventKind.ClientAttached => SnapshotEventKind.ClientAttached,
        ControlPlaneRecentEventKind.ClientDetached => SnapshotEventKind.ClientDetached,
        ControlPlaneRecentEventKind.Warning => SnapshotEventKind.Warning,
        _ => SnapshotEventKind.Warning
    };

    private static GatewayCapabilities BuildCapabilities()
    {
        return new GatewayCapabilities(
            SchemaVersion.V1(),
            "1.0.0", // ht: placeholder version
            new List<string> { "1.0.0" },
            new List<TransportCapability>
            {
                new("sse", new List<string> { "snapshot" }, new List<string> { "utf-8", "base64" }, false),
                new("websocket", new List<string> { "json", "binary" }, new List<string> { "utf-8", "base64" }, true),
                new("http", new List<string> { "rest" }, new List<string> { "utf-8" }, false)
            },
            new List<HarnessCapability>
            {
                HarnessKindCapabilityExtensions.OpenHandsAgentServer(),
                HarnessKindCapabilityExtensions.CodexAppServerLocal(),
                HarnessKindCapabilityExtensions.RustNativeFuture()
            },
            new List<FeatureCapability>
            {
                new("task_graph", true, false, null),
                new("action_dispatch", true, false, null),
                new("action_receipts", true, false, null),
                new("run_detail", true, false, null),
                new("event_journal", true, false, null),
                new("terminal_stream", false, false, null),
                new("planning", true, false, null),
                new("approval", false, false, null),
                new("rehydrate", true, false, null),
                new("linear_sync", true, false, null),
                new("openhands_harness", true, false, null),
                new("codex_harness", true, false, null),
                new("model_settings", true, false, null),
                new("hosted_mode", false, true, null)
            },
            new List<AuthMode> { AuthMode.None, AuthMode.ApiKey },
            1000,
            500
        );
    }

    private static ModelSettingsResponse ModelSettingsForApiKeyAndCodexReadiness(
        string? llmApiKey,
        GatewaySchema.CodexLocalReadiness codexReadiness)
    {
        var hasApiKey = !string.IsNullOrWhiteSpace(llmApiKey);
        return ModelSettingsResponse.LocalWithCodexReadiness(hasApiKey, codexReadiness);
    }
}