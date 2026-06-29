using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands RuntimeEventStream (client.rs).
//   WebSocket wrapper with readiness barrier, reconnect/replay, dedup, and
//   timestamp-ordered event delivery. Single async caller only.

public sealed class RuntimeEventStream
{
    private const string ConversationStateUpdateEventKind = "ConversationStateUpdateEvent";
    private const string UnreadyEventId = "runtime-stream-unready";

    private readonly OpenHandsClient _client;
    private readonly Guid _conversationId;
    private readonly RuntimeStreamConfig _config;
    private readonly EventCache _eventCache = new();
    private readonly ConversationStateMirror _stateMirror = new();
    private readonly List<EventEnvelope> _pendingEvents = [];
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> _webSocketFactory;

    private WebSocket? _socket;
    private Conversation _conversation;
    private EventEnvelope _readyEvent;
    private bool _pendingDeliveryNeedsDrain;
    private bool _reconnectPending;
    private bool _closed;

    internal RuntimeEventStream(OpenHandsClient client, Guid conversationId, RuntimeStreamConfig config, Conversation conversation, Func<Uri, CancellationToken, Task<WebSocket>>? webSocketFactory = null)
    {
        _client = client;
        _conversationId = conversationId;
        _config = config;
        _conversation = conversation;
        _webSocketFactory = webSocketFactory ?? DefaultWebSocketFactory;
        _readyEvent = EventEnvelope.StateUpdate(UnreadyEventId, "idle");
        _stateMirror.ApplyConversation(conversation);
    }

    private static async Task<WebSocket> DefaultWebSocketFactory(Uri uri, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, ct);
        return socket;
    }

    public Conversation Conversation => _conversation;
    public EventEnvelope ReadyEvent => _readyEvent;
    public EventCache EventCache => _eventCache;
    public ConversationStateMirror StateMirror => _stateMirror;

    internal async Task AttachAsync(CancellationToken ct = default)
    {
        await RefreshConversationAsync(ct);
        await ConnectReadyAndReconcileAsync(ct);
    }

    internal async Task AttachWithRecentEventsAsync(int limit, CancellationToken ct = default)
    {
        await RefreshConversationAsync(ct);
        await ConnectReadyAndReconcileRecentAsync(limit, ct);
    }

    public async Task<Result<int, OpenHandsError>> ReconcileEventsAsync(CancellationToken ct = default)
    {
        var reconciled = await _client.SearchAllEventsAsync(_conversationId, ct);
        if (reconciled.IsErr) return Result<int, OpenHandsError>.Err(reconciled.Error);
        return Result<int, OpenHandsError>.Ok(PushNewEvents(reconciled.Value.Items, true));
    }

    public async Task<Result<int, OpenHandsError>> ReconcileRecentEventsAsync(int limit, CancellationToken ct = default)
    {
        var reconciled = await _client.SearchRecentEventsAsync(_conversationId, limit, ct);
        if (reconciled.IsErr) return Result<int, OpenHandsError>.Err(reconciled.Error);
        return Result<int, OpenHandsError>.Ok(PushNewEvents(reconciled.Value.Items, true));
    }

    public async Task<Result<EventEnvelope?, OpenHandsError>> NextEventAsync(CancellationToken ct = default)
    {
        while (true)
        {
            if (TryTakePendingEvent() is { } pending)
                return Result<EventEnvelope?, OpenHandsError>.Ok(pending);

            if (_socket is null && _pendingEvents.Count == 0 && !_reconnectPending)
                return Result<EventEnvelope?, OpenHandsError>.Ok(null);

            var polled = await PollNextEventOnceAsync(ct);
            if (polled.IsErr) return Result<EventEnvelope?, OpenHandsError>.Err(polled.Error);
            if (polled.Value is { } evt)
                return Result<EventEnvelope?, OpenHandsError>.Ok(evt);
        }
    }

    public async Task CloseAsync()
    {
        _closed = true;
        ClearReadyEvent();
        _pendingDeliveryNeedsDrain = false;
        _reconnectPending = false;
        _pendingEvents.Clear();
        var socket = _socket;
        _socket = null;
        if (socket is null) return;
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "runtime stream closed", CancellationToken.None);
        }
        catch { /* best effort */ }
        socket.Dispose();
    }

    private async Task RefreshConversationAsync(CancellationToken ct)
    {
        var refreshed = await _client.GetConversationAsync(_conversationId, ct);
        if (refreshed.IsErr) throw refreshed.Error;
        _conversation = refreshed.Value;
        RebuildStateMirror();
    }

    private async Task ConnectReadyAndReconcileAsync(CancellationToken ct)
    {
        var socket = await ConnectWebSocketAsync(_config.ReplayExistingEventsOnAttach, ct);
        var readyEvent = await WaitForReadinessOnStreamAsync(socket, _config.ReadinessTimeout, ct);
        _readyEvent = readyEvent;
        _socket = socket;

        var reconciled = await _client.SearchAllEventsAsync(_conversationId, ct);
        if (reconciled.IsErr) throw reconciled.Error;
        PushNewEvents(reconciled.Value.Items, true);
        RebuildStateMirror();
    }

    private async Task ConnectReadyAndReconcileRecentAsync(int limit, CancellationToken ct)
    {
        var socket = await ConnectWebSocketAsync(_config.ReplayExistingEventsOnAttach, ct);
        var readyEvent = await WaitForReadinessOnStreamAsync(socket, _config.ReadinessTimeout, ct);
        _readyEvent = readyEvent;
        _socket = socket;

        var reconciled = await _client.SearchRecentEventsAsync(_conversationId, limit, ct);
        if (reconciled.IsErr) throw reconciled.Error;
        PushNewEvents(reconciled.Value.Items, true);
        RebuildStateMirror();
    }

    private async Task<WebSocket> ConnectWebSocketAsync(bool resendAll, CancellationToken ct)
    {
        var uri = BuildWebSocketUri(resendAll);
        if (uri.IsErr) throw uri.Error;
        var socket = await _webSocketFactory(uri.Value, ct);
        return socket;
    }

    private Result<Uri, OpenHandsError> BuildWebSocketUri(bool resendAll)
    {
        var parsed = _client.Transport.ParsedBaseUrl();
        if (parsed.IsErr) return Result<Uri, OpenHandsError>.Err(parsed.Error);
        var baseUri = parsed.Value;
        var scheme = baseUri.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            _ => throw OpenHandsError.InvalidConfiguration(
                $"unsupported base URL scheme `{baseUri.Scheme}`"),
        };

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var path = string.IsNullOrEmpty(basePath)
            ? $"/sockets/events/{_conversationId}"
            : $"{basePath}/sockets/events/{_conversationId}";

        var builder = new UriBuilder(baseUri) { Scheme = scheme, Path = path };
        var query = System.Web.HttpUtility.ParseQueryString(builder.Query ?? "");
        if (_client.Transport.Auth.Websocket is WebSocketAuth.QueryParam qp)
            query[qp.Key.Name] = qp.Key.Value;
        if (resendAll)
            query["resend_all"] = "true";
        builder.Query = query.ToString();
        return Result<Uri, OpenHandsError>.Ok(builder.Uri);
    }

    private async Task<EventEnvelope> WaitForReadinessOnStreamAsync(WebSocket socket, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var message = await ReadNextSocketMessageAsync(socket, cts.Token);
                if (message is null) throw OpenHandsError.WebSocketClosed();
                var evt = ParseTextEvent(message);
                if (evt.IsErr) continue;
                if (evt.Value.Kind == ConversationStateUpdateEventKind)
                    return evt.Value;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw OpenHandsError.ReadinessTimeout(timeout);
        }
    }

    private async Task<Result<EventEnvelope?, OpenHandsError>> PollNextEventOnceAsync(CancellationToken ct)
    {
        await AbsorbBufferedSocketEventsAsync(ct);
        if (await DeferPendingEventDeliveryOnceAsync(ct))
            return Result<EventEnvelope?, OpenHandsError>.Ok(null);

        if (TryTakePendingEvent() is { } evt)
            return Result<EventEnvelope?, OpenHandsError>.Ok(evt);

        if (_reconnectPending)
        {
            _reconnectPending = false;
            await ReconnectAsync(ct);
            if (await DeferPendingEventDeliveryOnceAsync(ct))
                return Result<EventEnvelope?, OpenHandsError>.Ok(null);
            if (TryTakePendingEvent() is { } pending)
                return Result<EventEnvelope?, OpenHandsError>.Ok(pending);
        }

        if (_socket is null)
            return Result<EventEnvelope?, OpenHandsError>.Ok(null);

        var read = await ReadNextSocketEventAsync(_socket, ct);
        switch (read)
        {
            case StreamRead.Event eventRead:
                {
                    var drained = new List<EventEnvelope> { eventRead.Value };
                    var reconnectSignal = await DrainBufferedSocketEventsAsync(drained, ct);
                    PushNewEvents(drained, true);
                    await HandleReconnectSignalAsync(reconnectSignal, ct);
                    return Result<EventEnvelope?, OpenHandsError>.Ok(null);
                }
            case StreamRead.Closed:
                await HandleReconnectSignalAsync(new StreamRead.Closed(), ct);
                if (await DeferPendingEventDeliveryOnceAsync(ct))
                    return Result<EventEnvelope?, OpenHandsError>.Ok(null);
                return Result<EventEnvelope?, OpenHandsError>.Ok(TryTakePendingEvent());
            case StreamRead.Transport transport:
                await HandleReconnectSignalAsync(transport, ct);
                if (await DeferPendingEventDeliveryOnceAsync(ct))
                    return Result<EventEnvelope?, OpenHandsError>.Ok(null);
                return Result<EventEnvelope?, OpenHandsError>.Ok(TryTakePendingEvent());
            default:
                return Result<EventEnvelope?, OpenHandsError>.Ok(null);
        }
    }

    private EventEnvelope? TryTakePendingEvent()
    {
        if (_pendingEvents.Count == 0) return null;
        var evt = _pendingEvents[0];
        _pendingEvents.RemoveAt(0);
        return evt;
    }

    private async Task AbsorbBufferedSocketEventsAsync(CancellationToken ct)
    {
        if (_socket is null) return;
        var drained = new List<EventEnvelope>();
        var reconnectSignal = await DrainBufferedSocketEventsAsync(drained, ct);
        PushNewEvents(drained, true);
        await HandleReconnectSignalAsync(reconnectSignal, ct);
    }

    private async Task<bool> DeferPendingEventDeliveryOnceAsync(CancellationToken ct)
    {
        if (!_pendingDeliveryNeedsDrain || _pendingEvents.Count == 0)
            return false;
        _pendingDeliveryNeedsDrain = false;
        await Task.Yield();
        await AbsorbBufferedSocketEventsAsync(ct);
        return true;
    }

    private async Task<StreamRead?> DrainBufferedSocketEventsAsync(List<EventEnvelope> drained, CancellationToken ct)
    {
        while (_socket is not null)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(10);
            try
            {
                var next = await ReadNextSocketEventAsync(_socket, cts.Token);
                if (next is StreamRead.Event evt)
                {
                    drained.Add(evt.Value);
                    continue;
                }
                return next;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }
        return null;
    }

    private async Task HandleReconnectSignalAsync(StreamRead? signal, CancellationToken ct)
    {
        if (signal is not (StreamRead.Closed or StreamRead.Transport)) return;
        var socket = _socket;
        _socket = null;
        socket?.Dispose();
        if (_closed) return;
        if (_pendingEvents.Count == 0)
            await ReconnectAsync(ct);
        else
            _reconnectPending = true;
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        ClearReadyEvent();
        var attempts = 0;
        var delay = _config.ReconnectInitialBackoff;
        while (true)
        {
            attempts++;
            if (attempts > 1)
            {
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _config.ReconnectMaxBackoff.Ticks));
            }

            try
            {
                await RefreshConversationAsync(ct);
                await ConnectReadyAndReconcileAsync(ct);
                return;
            }
            catch (OpenHandsError error)
            {
                if (attempts >= _config.MaxReconnectAttempts)
                    throw OpenHandsError.ReconnectExhausted(attempts, error.Message);
            }
        }
    }

    private async Task<StreamRead> ReadNextSocketEventAsync(WebSocket socket, CancellationToken ct)
    {
        while (true)
        {
            var message = await ReadNextSocketMessageAsync(socket, ct);
            if (message is null)
                return new StreamRead.Closed();

            var parsed = ParseTextEvent(message);
            if (parsed.IsOk)
                return new StreamRead.Event(parsed.Value);
        }
    }

    private async Task<string?> ReadNextSocketMessageAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (result.CloseStatus is { } status && socket.State != WebSocketState.Closed)
                    await socket.CloseAsync(status, result.CloseStatusDescription, CancellationToken.None);
                return null;
            }
            if (result.MessageType == WebSocketMessageType.Text)
            {
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                    return sb.ToString();
            }
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                    return sb.ToString();
            }
        }
        return null;
    }

    private static Result<EventEnvelope, OpenHandsError> ParseTextEvent(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return Result<EventEnvelope, OpenHandsError>.Ok(EventEnvelope.Deserialize(doc.RootElement));
        }
        catch (Exception ex)
        {
            return Result<EventEnvelope, OpenHandsError>.Err(
                OpenHandsError.MalformedWebSocketEvent(ex.Message, payload[..Math.Min(payload.Length, 160)]));
        }
    }

    private int PushNewEvents(IEnumerable<EventEnvelope> events, bool queueNew)
    {
        var inserted = _eventCache.MergeNewEvents(events);
        if (inserted.Count == 0) return 0;

        _pendingDeliveryNeedsDrain = true;
        if (queueNew)
            QueuePendingEvents(inserted);
        if (inserted.Any(e => e.Kind == ConversationStateUpdateEventKind))
            RebuildStateMirror();
        return inserted.Count;
    }

    private void QueuePendingEvents(List<EventEnvelope> inserted)
    {
        foreach (var evt in inserted)
        {
            var pos = _pendingEvents.FindIndex(p => ComparePendingEvents(p, evt) > 0);
            if (pos < 0) pos = _pendingEvents.Count;
            _pendingEvents.Insert(pos, evt);
        }
    }

    private void RebuildStateMirror()
    {
        _stateMirror.RebuildFrom(_conversation, _eventCache.Items);
        ApplyTerminalConversationFallback();
        ApplyReadyEventToStateMirror();
    }

    private void ClearReadyEvent()
    {
        _readyEvent = EventEnvelope.StateUpdate(UnreadyEventId, "idle");
    }

    private void ApplyReadyEventToStateMirror()
    {
        if (_readyEvent.Id == UnreadyEventId || _readyEvent.Kind != ConversationStateUpdateEventKind)
            return;

        var payload = KnownEvent.DecodeStateUpdate(_readyEvent);
        if (payload is null) return;

        var cacheHasSameOrNewer = _eventCache.Items.Any(e =>
            ComparePendingEvents(e, _readyEvent) >= 0 && e.Kind == ConversationStateUpdateEventKind);
        if (cacheHasSameOrNewer) return;

        var readyIsTerminal = payload.ExecutionStatus is "finished" or "error" or "stuck";
        var readyRestarts = payload.ExecutionStatus is "queued" or "running";
        if (_stateMirror.TerminalStatus() is not null && !readyIsTerminal && !readyRestarts)
            return;

        _stateMirror.ApplyEvent(_readyEvent);
    }

    private void ApplyTerminalConversationFallback()
    {
        var latestCachedStatus = _eventCache.Items.LastOrDefault(e => e.Kind == ConversationStateUpdateEventKind);
        var cachedStatus = latestCachedStatus is null ? null : KnownEvent.DecodeStateUpdate(latestCachedStatus)?.ExecutionStatus;
        var cachedRestarts = cachedStatus is "queued" or "running";
        if (_conversation.ExecutionStatus is "finished" or "error" or "stuck"
            && !cachedRestarts
            && _stateMirror.TerminalStatus() is null)
        {
            _stateMirror.ApplyConversationExecutionStatus(_conversation);
        }
    }

    private static int ComparePendingEvents(EventEnvelope left, EventEnvelope right)
    {
        var c = left.Timestamp.CompareTo(right.Timestamp);
        return c != 0 ? c : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }

    private abstract record StreamRead
    {
        public sealed record Event(EventEnvelope Value) : StreamRead;
        public sealed record Closed : StreamRead;
        public sealed record Transport(OpenHandsError Error) : StreamRead;
    }
}
