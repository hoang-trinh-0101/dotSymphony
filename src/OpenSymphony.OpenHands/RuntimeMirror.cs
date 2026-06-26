using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands runtime_mirror.rs — config + mirror skeleton.

public static class RuntimeMirrorConstants
{
    public const string NO_EVENT_CURSOR_MARKER = "runtime://status-change";
    public const string TERMINAL_CURSOR_MARKER = "runtime://terminal";
}

public sealed record MirrorConfig
{
    public DurationMs? IdleTimeoutMs { get; init; } = DurationMs.New(300_000);
    public DurationMs? TotalRuntimeCapMs { get; init; }
    public DurationMs? QuietWindowMs { get; init; } = DurationMs.New(60_000);
}

public sealed class RuntimeMirror
{
    private readonly MirrorConfig _config;
    private readonly StringIdentifier<ConversationId> _conversationId;
    private readonly TimestampMs _startedAt;
    private ConversationStateMirror _stateMirror = new();
    private readonly EventCache _eventCache = new();
    private StallMetadata _stall;
    private StreamHealth _streamHealth = StreamHealth.Unknown;
    private HistorySyncStatus _historySyncStatus = HistorySyncStatus.Idle;
    private ReconnectStatus _reconnectStatus = ReconnectStatus.Pending;
    private string? _lastEventId;
    private string? _lastEventKind;
    private TimestampMs? _lastEventAt;
    private TimestampMs? _lastLogicalEventAt;
    private DetachMetadata? _detachMetadata;
    private bool _cancelPending;

    public RuntimeMirror(StringIdentifier<ConversationId> conversationId, TimestampMs startedAt, MirrorConfig? config = null)
    {
        _conversationId = conversationId;
        _startedAt = startedAt;
        _config = config ?? new MirrorConfig();
        _stall = _config.IdleTimeoutMs is { } idle
            ? StallMetadata.New(startedAt, idle)
            : StallMetadata.New(startedAt, DurationMs.New(300_000));
        if (_config.TotalRuntimeCapMs is { } cap)
            _stall = StallMetadata.WithRuntimeCap(startedAt, _config.IdleTimeoutMs ?? DurationMs.New(300_000), cap);
    }

    public StringIdentifier<ConversationId> ConversationId => _conversationId;
    public TimestampMs StartedAt => _startedAt;
    public ConversationStateMirror StateMirror => _stateMirror;
    public EventCache EventCache => _eventCache;
    public StreamHealth StreamHealth => _streamHealth;
    public HistorySyncStatus HistorySyncStatus => _historySyncStatus;
    public ReconnectStatus ReconnectStatus => _reconnectStatus;
    public string? LastEventId => _lastEventId;
    public string? LastEventKind => _lastEventKind;
    public TimestampMs? LastEventAt => _lastEventAt;
    public DetachMetadata? DetachMetadata => _detachMetadata;

    public void ApplyConversation(Conversation conversation) => _stateMirror.ApplyConversation(conversation);

    public void ReconcileHistory(IEnumerable<EventEnvelope> events)
    {
        var inserted = _eventCache.MergeNewEvents(events);
        foreach (var evt in inserted)
            _stateMirror.ApplyEvent(evt);
        _historySyncStatus = HistorySyncStatus.Synced;
        UpdateLastEventFromCache();
    }

    public void ApplyEvent(EventEnvelope envelope)
    {
        if (!_eventCache.Insert(envelope)) return;
        _stateMirror.ApplyEvent(envelope);
        var eventTs = TimestampMs.New((ulong)envelope.Timestamp.ToUnixTimeMilliseconds());
        _lastEventId = envelope.Id;
        _lastEventKind = envelope.Kind;
        _lastEventAt = eventTs;
        _lastLogicalEventAt = eventTs;
        _stall = _stall.ObserveActivity(eventTs, out _);
    }

    public RuntimeLivenessPhase PhaseAt(TimestampMs now)
    {
        if (_cancelPending) return RuntimeLivenessPhase.Cancelling;
        if (_detachMetadata is not null) return RuntimeLivenessPhase.Detached;
        var terminal = _stateMirror.TerminalStatus();
        if (terminal is not null) return RuntimeLivenessPhase.Terminal;

        if (_streamHealth == StreamHealth.Reconnecting || _historySyncStatus == HistorySyncStatus.InProgress)
            return RuntimeLivenessPhase.Reconciling;

        if (_stall.IsStalledAt(now)) return RuntimeLivenessPhase.Stalled;

        return _streamHealth == StreamHealth.Ready ? RuntimeLivenessPhase.RunningTurn : RuntimeLivenessPhase.Quiet;
    }

    public RuntimeProgressSnapshot Snapshot(RuntimeProgressSnapshot previous)
    {
        var phase = PhaseAt(TimestampMs.New((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var (input, output, cacheRead) = _stateMirror.AccumulatedTokenUsage() ?? (0UL, 0UL, 0UL);
        return new RuntimeProgressSnapshot
        {
            Phase = phase,
            LivenessState = phase.LivenessState(),
            StreamHealth = _streamHealth,
            HistorySyncStatus = _historySyncStatus,
            ReconnectStatus = _reconnectStatus,
            LastEventCursor = _lastEventId,
            LastEventKind = _lastEventKind,
            LastEventAt = _lastEventAt,
            InputTokens = input,
            OutputTokens = output,
            CacheReadTokens = cacheRead,
            ExecutionStatus = _stateMirror.ExecutionStatus,
            DetachMetadata = _detachMetadata,
        };
    }

    public void MarkStreamReady()
    {
        _streamHealth = StreamHealth.Ready;
        _reconnectStatus = ReconnectStatus.Connected;
    }

    public void MarkStreamReconnecting()
    {
        _streamHealth = StreamHealth.Reconnecting;
        _reconnectStatus = ReconnectStatus.Pending;
    }

    public void MarkStreamDisconnected()
    {
        _streamHealth = StreamHealth.Disconnected;
        _reconnectStatus = ReconnectStatus.Closed;
    }

    public void ApplyCancelling() => _cancelPending = true;

    public void ApplyTerminal(TimestampMs at)
    {
        _cancelPending = false;
        var status = _stateMirror.ExecutionStatus;
        var reason = status switch
        {
            "error" => DetachReason.Unreachable,
            "stuck" => DetachReason.Unreachable,
            _ => DetachReason.WorkerShutdown,
        };
        _detachMetadata = new DetachMetadata(reason, at, status, $"terminal: {status ?? "unknown"}");
    }

    private void UpdateLastEventFromCache()
    {
        if (_eventCache.Items.Count > 0)
        {
            var last = _eventCache.Items[^1];
            _lastEventId = last.Id;
            _lastEventKind = last.Kind;
            _lastEventAt = TimestampMs.New((ulong)last.Timestamp.ToUnixTimeMilliseconds());
        }
    }
}
