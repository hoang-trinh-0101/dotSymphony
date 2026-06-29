using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using OpenSymphony.OpenHands;

namespace OpenSymphony.TestKit;

// ht: port of opensymphony-testkit lib.rs — fake OpenHands agent-server for integration tests.
//   ASP.NET Core TestServer provides HTTP + WebSocket without binding a real port.

public sealed class FakeOpenHandsConfig
{
    public int SearchPageSize { get; init; } = 2;
    public string RunTerminalStatus { get; init; } = "finished";
    public string InitialExecutionStatus { get; init; } = "idle";
}

public sealed class FakeConversationBuilder
{
    private readonly WorkspaceConfig _workspace;
    private readonly string _persistenceDir;
    private readonly uint _maxIterations;
    private readonly bool _stuckDetection;
    private string _executionStatus = "idle";
    private readonly ConfirmationPolicy _confirmationPolicy;
    private readonly AgentConfig _agent;

    public FakeConversationBuilder(ConversationCreateRequest request)
    {
        _workspace = request.Workspace;
        _persistenceDir = request.PersistenceDir;
        _maxIterations = request.MaxIterations;
        _stuckDetection = request.StuckDetection;
        _confirmationPolicy = request.ConfirmationPolicy;
        _agent = request.Agent;
    }

    public static FakeConversationBuilder FromRequest(ConversationCreateRequest request) =>
        new(request);

    public FakeConversationBuilder WithExecutionStatus(string status)
    {
        _executionStatus = status;
        return this;
    }

    public Conversation Build(Guid conversationId) => new(
        conversationId,
        _workspace,
        _persistenceDir,
        _maxIterations,
        _stuckDetection,
        _executionStatus,
        _confirmationPolicy,
        _agent);
}

public sealed class FakeEventStreamBuilder
{
    private readonly DateTimeOffset _baseTimestamp;

    public FakeEventStreamBuilder() : this(DateTimeOffset.UtcNow) { }

    public FakeEventStreamBuilder(DateTimeOffset baseTimestamp)
    {
        _baseTimestamp = baseTimestamp;
    }

    public EventEnvelope CustomAt(string id, long offsetMs, string source, string kind, JsonElement payload) =>
        new(id, _baseTimestamp.AddMilliseconds(offsetMs), source, kind, payload);

    public EventEnvelope StateUpdateAt(string id, long offsetMs, string executionStatus)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            execution_status = executionStatus,
            state_delta = new { execution_status = executionStatus },
        });
        return CustomAt(id, offsetMs, "runtime", "ConversationStateUpdateEvent", payload);
    }

    public EventEnvelope LlmCompletionAt(string id, long offsetMs, string model, ulong tokens)
    {
        var payload = JsonSerializer.SerializeToElement(new { model, tokens });
        return CustomAt(id, offsetMs, "llm", "LLMCompletionLogEvent", payload);
    }

    public EventEnvelope ConversationErrorAt(string id, long offsetMs, string message)
    {
        var payload = JsonSerializer.SerializeToElement(new { message });
        return CustomAt(id, offsetMs, "runtime", "ConversationErrorEvent", payload);
    }
}

public sealed class FakeSearchScript
{
    private readonly List<SearchConversationEventsResponse> _responses = [];

    public FakeSearchScript Response(List<EventEnvelope> events)
    {
        _responses.Add(new SearchConversationEventsResponse { Events = events });
        return this;
    }

    public FakeSearchScript PagedResponse(List<EventEnvelope> events, string? nextPageId)
    {
        _responses.Add(new SearchConversationEventsResponse { Events = events, NextPageId = nextPageId });
        return this;
    }

    public IReadOnlyList<SearchConversationEventsResponse> Responses => _responses;
}

public enum FakeSocketAction
{
    Event,
    Text,
    Ping,
    Close
}

public sealed class FakeSocketScript
{
    private readonly List<(FakeSocketAction Kind, object? Data)> _actions = [];

    public FakeSocketScript Event(EventEnvelope evt)
    {
        _actions.Add((FakeSocketAction.Event, evt));
        return this;
    }

    public FakeSocketScript Text(string payload)
    {
        _actions.Add((FakeSocketAction.Text, payload));
        return this;
    }

    public FakeSocketScript Ping(byte[] payload)
    {
        _actions.Add((FakeSocketAction.Ping, payload));
        return this;
    }

    public FakeSocketScript Close()
    {
        _actions.Add((FakeSocketAction.Close, null));
        return this;
    }

    public IReadOnlyList<(FakeSocketAction Kind, object? Data)> Actions => _actions;
}

public sealed class FakeServerError : Exception
{
    public FakeServerError(string message) : base(message) { }
}

public sealed class FakeOpenHandsServer : IDisposable
{
    private readonly FakeOpenHandsConfig _config;
    private readonly WebApplication _app;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<Guid, FakeConversationState> _conversations = new();
    private readonly object _lock = new();
    private readonly Dictionary<Guid, int> _conversationGetNotFound = new();
    private readonly DateTimeOffset _baseTimestamp = DateTimeOffset.UtcNow;
    private long _nextEventOffsetMs;
    private int _nextEventNumber = 1;

    public FakeOpenHandsServer(FakeOpenHandsConfig? config = null)
    {
        _config = config ?? new FakeOpenHandsConfig();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/openapi.json", OpenApi);
        app.MapPost("/api/conversations", CreateConversation);
        app.MapGet("/api/conversations/{id:guid}", GetConversation);
        app.MapDelete("/api/conversations/{id:guid}", DeleteConversation);
        app.MapPost("/api/conversations/{id:guid}/events", SendMessage);
        app.MapPost("/api/conversations/{id:guid}/run", RunConversation);
        app.MapGet("/api/conversations/{id:guid}/events/search", SearchEvents);
        app.MapGet("/sockets/events/{id:guid}", EventsSocket);
        _app = app;
        _app.StartAsync().GetAwaiter().GetResult();
        _baseUrl = _app.Urls.FirstOrDefault() ?? throw new InvalidOperationException("no server URL");
        _httpClient = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public string BaseUrl => _baseUrl;

    public HttpClient CreateClient() => _httpClient;

    public ClientWebSocket CreateWebSocketClient() => new();

    public void Dispose()
    {
        _httpClient.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }

    public Task EmitStateUpdate(Guid conversationId, string executionStatus)
    {
        var evt = MakeStateEvent(executionStatus);
        return InsertEvent(conversationId, evt);
    }

    public Task InsertEvent(Guid conversationId, EventEnvelope evt)
    {
        lock (_lock)
        {
            var state = GetConversationState(conversationId);
            AddEvent(state, evt);
            return Task.CompletedTask;
        }
    }

    public Task<int> EventCount(Guid conversationId)
    {
        lock (_lock)
        {
            var state = GetConversationState(conversationId);
            return Task.FromResult(state.Events.Count);
        }
    }

    public Task FailNextConversationGets(Guid conversationId, int count)
    {
        lock (_lock)
        {
            if (!_conversations.ContainsKey(conversationId))
                throw new FakeServerError($"conversation not found: {conversationId}");
            if (count == 0)
                _conversationGetNotFound.Remove(conversationId);
            else
                _conversationGetNotFound[conversationId] = count;
            return Task.CompletedTask;
        }
    }

    public Task SetExecutionStatusOnNextMessageWithoutEvent(Guid conversationId, string executionStatus)
    {
        lock (_lock)
        {
            var state = GetConversationState(conversationId);
            state.NextMessageExecutionStatus = executionStatus;
            return Task.CompletedTask;
        }
    }

    public Task ScriptSearchResponses(Guid conversationId, FakeSearchScript script)
    {
        lock (_lock)
        {
            var state = GetConversationState(conversationId);
            foreach (var response in script.Responses)
                state.ScriptedSearchResponses.Enqueue(response);
            return Task.CompletedTask;
        }
    }

    public Task ScriptSocketConnections(Guid conversationId, List<FakeSocketScript> scripts)
    {
        lock (_lock)
        {
            var state = GetConversationState(conversationId);
            foreach (var script in scripts)
                state.SocketScripts.Enqueue(script);
            return Task.CompletedTask;
        }
    }

    private async Task OpenApi(HttpContext context)
    {
        var doc = new
        {
            openapi = "3.1.0",
            info = new { title = "Fake OpenHands agent-server", version = "0.1.0" }
        };
        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(doc, OpenHandsJsonOptions.Default);
    }

    private async Task CreateConversation(HttpContext context)
    {
        var request = await context.Request.ReadFromJsonAsync<ConversationCreateRequest>(OpenHandsJsonOptions.Default);
        if (request is null)
        {
            context.Response.StatusCode = 400;
            return;
        }

        Conversation summary;
        lock (_lock)
        {
            if (_conversations.TryGetValue(request.ConversationId, out var existing))
            {
                summary = existing.Summary;
            }
            else
            {
                summary = FakeConversationBuilder.FromRequest(request)
                    .WithExecutionStatus(_config.InitialExecutionStatus)
                    .Build(request.ConversationId);
                var state = new FakeConversationState { Summary = summary };
                AddEvent(state, MakeStateEvent(_config.InitialExecutionStatus));
                _conversations[request.ConversationId] = state;
            }
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(summary, OpenHandsJsonOptions.Default);
    }

    private async Task GetConversation(HttpContext context)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        FakeConversationState? state;
        bool notFound;
        lock (_lock)
        {
            notFound = _conversationGetNotFound.TryGetValue(id, out var remaining) && remaining > 0;
            if (notFound)
            {
                if (remaining == 1)
                    _conversationGetNotFound.Remove(id);
                else
                    _conversationGetNotFound[id] = remaining - 1;
            }
            _conversations.TryGetValue(id, out state);
        }

        if (notFound || state is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(state.Summary, OpenHandsJsonOptions.Default);
    }

    private async Task DeleteConversation(HttpContext context)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        lock (_lock)
        {
            if (!_conversations.Remove(id))
            {
                context.Response.StatusCode = 404;
                return;
            }
            _conversationGetNotFound.Remove(id);
        }
        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(new { success = true }, OpenHandsJsonOptions.Default);
    }

    private async Task SendMessage(HttpContext context)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        var request = await context.Request.ReadFromJsonAsync<SendMessageRequest>(OpenHandsJsonOptions.Default);
        if (request is null)
        {
            context.Response.StatusCode = 400;
            return;
        }

        lock (_lock)
        {
            if (!_conversations.TryGetValue(id, out var state))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var evt = new EventEnvelope(
                NextEventId(),
                NextEventTimestamp(),
                "user",
                "MessageEvent",
                JsonSerializer.SerializeToElement(request, OpenHandsJsonOptions.Default));
            AddEvent(state, evt);

            if (state.NextMessageExecutionStatus is { } status)
            {
                state.Summary = state.Summary with { ExecutionStatus = status };
                state.NextMessageExecutionStatus = null;
            }
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(new AcceptedResponse(), OpenHandsJsonOptions.Default);
    }

    private async Task RunConversation(HttpContext context)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);

        lock (_lock)
        {
            if (!_conversations.TryGetValue(id, out var state))
            {
                context.Response.StatusCode = 404;
                return;
            }
            if (IsRunning(state.Summary.ExecutionStatus))
            {
                context.Response.StatusCode = 409;
                return;
            }

            var terminalStatus = _config.RunTerminalStatus;
            AddEvent(state, MakeStateEvent("running"));
            AddEvent(state, MakeCompletionEvent());
            AddEvent(state, MakeStateEvent(terminalStatus));
            state.Summary = state.Summary with { ExecutionStatus = terminalStatus };
        }

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(new AcceptedResponse(), OpenHandsJsonOptions.Default);
    }

    private async Task SearchEvents(HttpContext context)
    {
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        var pageId = context.Request.Query["page_id"].FirstOrDefault() ?? "0";
        var limit = int.TryParse(context.Request.Query["limit"].FirstOrDefault(), out var l) ? Math.Max(1, l) : _config.SearchPageSize;
        var sortOrder = context.Request.Query["sort_order"].FirstOrDefault();

        List<EventEnvelope> events = new();
        SearchConversationEventsResponse? scripted;
        lock (_lock)
        {
            if (!_conversations.TryGetValue(id, out var state))
            {
                context.Response.StatusCode = 404;
                return;
            }
            if (state.ScriptedSearchResponses.Count > 0)
            {
                scripted = state.ScriptedSearchResponses.Dequeue();
            }
            else
            {
                scripted = null;
                events = new List<EventEnvelope>(state.Events);
            }
        }

        if (scripted is not null)
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsJsonAsync(scripted, OpenHandsJsonOptions.Default);
            return;
        }

        if (sortOrder?.Equals("TIMESTAMP_DESC", StringComparison.OrdinalIgnoreCase) == true)
            events.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        var offset = int.TryParse(pageId, out var o) ? Math.Max(0, o) : 0;
        var page = events.Skip(offset).Take(limit).ToList();
        var nextPageId = offset + page.Count < events.Count ? (offset + page.Count).ToString() : null;

        context.Response.StatusCode = 200;
        await context.Response.WriteAsJsonAsync(
            new SearchConversationEventsResponse { Events = page, NextPageId = nextPageId },
            OpenHandsJsonOptions.Default);
    }

    private async Task EventsSocket(HttpContext context)
    {
        Console.WriteLine($"[FakeOpenHands] WebSocket request received: {context.Request.Path}");
        var id = Guid.Parse(context.Request.RouteValues["id"]!.ToString()!);
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        FakeConversationState? state;
        lock (_lock)
        {
            if (!_conversations.TryGetValue(id, out state))
            {
                context.Response.StatusCode = 404;
                return;
            }
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        Console.WriteLine("[FakeOpenHands] WebSocket accepted");

        // Send the latest state event as the readiness barrier.
        var readyEvent = LatestStateEvent(state);
        Console.WriteLine($"[FakeOpenHands] Sending ready event: {readyEvent.Kind}");
        if (await SendEventAsync(socket, readyEvent) is false) return;

        // If a scripted connection is queued, play it; otherwise forward live events.
        FakeSocketScript? script;
        lock (_lock) { script = state.SocketScripts.Count > 0 ? state.SocketScripts.Dequeue() : null; }

        if (script is not null)
        {
            foreach (var (kind, data) in script.Actions)
            {
                switch (kind)
                {
                    case FakeSocketAction.Event when data is EventEnvelope evt:
                        if (await SendEventAsync(socket, evt) is false) return;
                        break;
                    case FakeSocketAction.Text when data is string text:
                        if (await SendTextAsync(socket, text) is false) return;
                        break;
                    case FakeSocketAction.Ping when data is byte[] payload:
                        if (await SendPingAsync(socket, payload) is false) return;
                        break;
                    case FakeSocketAction.Close:
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "scripted close", CancellationToken.None);
                        return;
                }
            }
            return;
        }

        var reader = state.EventChannel.Reader;
        while (true)
        {
            var evt = await reader.ReadAsync();
            Console.WriteLine($"[FakeOpenHands] Forwarding event {evt.Kind} ({evt.Id}) over WebSocket");
            if (await SendEventAsync(socket, evt) is false) return;
        }
    }

    private sealed class FakeConversationState
    {
        public Conversation Summary { get; set; } = null!;
        public List<EventEnvelope> Events { get; } = new();
        public Channel<EventEnvelope> EventChannel { get; } = Channel.CreateUnbounded<EventEnvelope>();
        public Queue<SearchConversationEventsResponse> ScriptedSearchResponses { get; } = new();
        public Queue<FakeSocketScript> SocketScripts { get; } = new();
        public string? NextMessageExecutionStatus { get; set; }
    }

    private FakeConversationState GetConversationState(Guid id)
    {
        if (!_conversations.TryGetValue(id, out var state))
            throw new FakeServerError($"conversation not found: {id}");
        return state;
    }

    private void AddEvent(FakeConversationState state, EventEnvelope evt)
    {
        state.Events.Add(evt);
        state.EventChannel.Writer.TryWrite(evt);
        Console.WriteLine($"[FakeOpenHands] Added event {evt.Kind} ({evt.Id}), channel count now {state.EventChannel.Reader.Count}");
    }

    private EventEnvelope LatestStateEvent(FakeConversationState state)
    {
        var latest = state.Events.LastOrDefault(e => e.Kind == "ConversationStateUpdateEvent")
            ?? state.Events.LastOrDefault()
            ?? MakeStateEvent(_config.InitialExecutionStatus);
        return latest;
    }

    private EventEnvelope MakeStateEvent(string executionStatus)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            execution_status = executionStatus,
            state_delta = new { execution_status = executionStatus },
        });
        return new EventEnvelope(NextEventId(), NextEventTimestamp(), "runtime", "ConversationStateUpdateEvent", payload);
    }

    private EventEnvelope MakeCompletionEvent()
    {
        var payload = JsonSerializer.SerializeToElement(new { model = "fake-model", tokens = 42 });
        return new EventEnvelope(NextEventId(), NextEventTimestamp(), "llm", "LLMCompletionLogEvent", payload);
    }

    private string NextEventId()
    {
        var n = Interlocked.Increment(ref _nextEventNumber);
        return $"evt-{n}";
    }

    private static bool IsRunning(string status) =>
        status.Equals("running", StringComparison.OrdinalIgnoreCase)
        || status.Equals("queued", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> SendEventAsync(WebSocket socket, EventEnvelope evt)
    {
        var json = evt.Serialize();
        return await SendTextAsync(socket, json);
    }

    private static async Task<bool> SendTextAsync(WebSocket socket, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);
            return true;
        }
        catch (WebSocketException)
        {
            return false;
        }
    }

    private static async Task<bool> SendPingAsync(WebSocket socket, byte[] payload)
    {
        try
        {
            await socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None);
            return true;
        }
        catch (WebSocketException)
        {
            return false;
        }
    }

    private DateTimeOffset NextEventTimestamp()
    {
        var offset = Interlocked.Increment(ref _nextEventOffsetMs);
        return _baseTimestamp.AddMilliseconds(offset);
    }
}
