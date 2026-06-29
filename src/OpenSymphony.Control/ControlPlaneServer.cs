using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using OpenSymphony.Domain;

namespace OpenSymphony.Control;

public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("current_sequence")] ulong CurrentSequence,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("issue_count")] int IssueCount);

public sealed class ControlPlaneServer
{
    private readonly SnapshotStore _store;

    public ControlPlaneServer(SnapshotStore store) => _store = store;

    public WebApplication BuildApp(string? basePath = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var app = builder.Build();

        IEndpointRouteBuilder routes = app;
        if (basePath is not null)
            routes = app.MapGroup(basePath);

        routes.MapGet("/healthz", () =>
        {
            var env = _store.Current();
            return Results.Json(
                new HealthResponse("ok", env.Sequence, env.PublishedAt, env.Snapshot.IssueCount),
                ControlPlaneJson.Options);
        });

        routes.MapGet("/api/v1/snapshot", () =>
            Results.Json(_store.Current(), ControlPlaneJson.Options));

        routes.MapGet("/api/v1/control/events", async ctx => await SseHandler(ctx, _store));
        routes.MapGet("/api/v1/events", async ctx => await SseHandler(ctx, _store));

        return app;
    }

    private static async Task SseHandler(HttpContext ctx, SnapshotStore store)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var subscriber = store.Subscribe();
        var initial = store.Current();
        ulong lastSent = initial.Sequence;

        await WriteSnapshotEvent(ctx.Response, initial);

        var keepaliveCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var readTask = SnapshotCatchUp.NextSnapshotEnvelope(store, subscriber, lastSent);
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
        var payload = ControlPlaneJson.Serialize(envelope);
        var sse = $"event: snapshot\nid: {envelope.Sequence}\ndata: {payload}\n\n";
        await response.WriteAsync(sse);
        await response.Body.FlushAsync();
    }
}
