using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Domain;

// ── Enums ──────────────────────────────────────────────────────────────────

public enum RuntimeLivenessPhase
{
    WaitingOnPriorTurn,
    RunningTurn,
    Quiet,
    Degraded,
    Reconciling,
    Cancelling,
    Stalled,
    Detached,
    Terminal,
}

public enum LivenessState
{
    Active,
    Quiet,
    Degraded,
    Stalled,
    Detached,
    Terminal,
}

public enum StreamHealth
{
    Unknown,
    Attaching,
    HistorySyncing,
    Ready,
    Reconnecting,
    Disconnected,
    Failed,
    Detached,
}

public enum HistorySyncStatus
{
    Idle,
    InProgress,
    Synced,
    Stale,
    Failed,
}

public enum ReconnectStatus
{
    Connected,
    Pending,
    Exhausted,
    Closed,
}

public enum DetachReason
{
    CancelFailed,
    CancelUnsupported,
    Unreachable,
    WorkerShutdown,
}

public enum RuntimeStreamState
{
    Detached,
    Attaching,
    Ready,
    Reconnecting,
    Closed,
    Failed,
}

public enum RetryReason
{
    Continuation,
    Failure,
    Stalled,
    Cancelled,
    Reconciliation,
}

public enum WorkerOutcomeKind
{
    Succeeded,
    Failed,
    TimedOut,
    Stalled,
    Cancelled,
    Detached,
    CancelFailed,
}

public enum ReleaseReason
{
    Completed,
    TrackerInactive,
    TrackerTerminal,
    Cancelled,
    RetryExhausted,
}

// ── Enum extensions ────────────────────────────────────────────────────────

public static class RuntimeLivenessPhaseExtensions
{
    public static LivenessState LivenessState(this RuntimeLivenessPhase phase) => phase switch
    {
        RuntimeLivenessPhase.WaitingOnPriorTurn or RuntimeLivenessPhase.RunningTurn => Domain.LivenessState.Active,
        RuntimeLivenessPhase.Quiet => Domain.LivenessState.Quiet,
        RuntimeLivenessPhase.Degraded => Domain.LivenessState.Degraded,
        RuntimeLivenessPhase.Reconciling or RuntimeLivenessPhase.Cancelling or RuntimeLivenessPhase.Stalled => Domain.LivenessState.Stalled,
        RuntimeLivenessPhase.Detached => Domain.LivenessState.Detached,
        RuntimeLivenessPhase.Terminal => Domain.LivenessState.Terminal,
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    public static string ToSnakeCaseString(this RuntimeLivenessPhase phase) => phase switch
    {
        RuntimeLivenessPhase.WaitingOnPriorTurn => "waiting_on_prior_turn",
        RuntimeLivenessPhase.RunningTurn => "running_turn",
        RuntimeLivenessPhase.Quiet => "quiet",
        RuntimeLivenessPhase.Degraded => "degraded",
        RuntimeLivenessPhase.Reconciling => "reconciling",
        RuntimeLivenessPhase.Cancelling => "cancelling",
        RuntimeLivenessPhase.Stalled => "stalled",
        RuntimeLivenessPhase.Detached => "detached",
        RuntimeLivenessPhase.Terminal => "terminal",
        _ => phase.ToString(),
    };
}

public static class LivenessStateExtensions
{
    public static string ToSnakeCaseString(this LivenessState state) => state switch
    {
        Domain.LivenessState.Active => "active",
        Domain.LivenessState.Quiet => "quiet",
        Domain.LivenessState.Degraded => "degraded",
        Domain.LivenessState.Stalled => "stalled",
        Domain.LivenessState.Detached => "detached",
        Domain.LivenessState.Terminal => "terminal",
        _ => state.ToString(),
    };
}

public static class StreamHealthExtensions
{
    public static string ToSnakeCaseString(this StreamHealth health) => health switch
    {
        StreamHealth.Unknown => "unknown",
        StreamHealth.Attaching => "attaching",
        StreamHealth.HistorySyncing => "history_syncing",
        StreamHealth.Ready => "ready",
        StreamHealth.Reconnecting => "reconnecting",
        StreamHealth.Disconnected => "disconnected",
        StreamHealth.Failed => "failed",
        StreamHealth.Detached => "detached",
        _ => health.ToString(),
    };
}

public static class HistorySyncStatusExtensions
{
    public static string ToSnakeCaseString(this HistorySyncStatus status) => status switch
    {
        HistorySyncStatus.Idle => "idle",
        HistorySyncStatus.InProgress => "in_progress",
        HistorySyncStatus.Synced => "synced",
        HistorySyncStatus.Stale => "stale",
        HistorySyncStatus.Failed => "failed",
        _ => status.ToString(),
    };
}

public static class ReconnectStatusExtensions
{
    public static string ToSnakeCaseString(this ReconnectStatus status) => status switch
    {
        ReconnectStatus.Connected => "connected",
        ReconnectStatus.Pending => "pending",
        ReconnectStatus.Exhausted => "exhausted",
        ReconnectStatus.Closed => "closed",
        _ => status.ToString(),
    };
}

public static class ReleaseReasonExtensions
{
    public static bool PreservesReactivationState(this ReleaseReason reason) =>
        reason == Domain.ReleaseReason.TrackerInactive;
}

// ── RetryAttempt + error ───────────────────────────────────────────────────

public enum RetryCalculationError
{
    ZeroAttempt,
    AttemptOverflow,
}

[JsonConverter(typeof(RetryAttemptConverter))]
public readonly struct RetryAttempt : IEquatable<RetryAttempt>
{
    public uint Value { get; }

    internal RetryAttempt(uint value) => Value = value;

    public static RetryAttempt First() => new(1);

    public static Result<RetryAttempt, RetryCalculationError> New(uint value) =>
        value == 0
            ? Result<RetryAttempt, RetryCalculationError>.Err(RetryCalculationError.ZeroAttempt)
            : Result<RetryAttempt, RetryCalculationError>.Ok(new RetryAttempt(value));

    public uint Get() => Value;

    public static Result<RetryAttempt, RetryCalculationError> After(RetryAttempt? previous) =>
        previous is { } prev
            ? prev.CheckedNext() is { } next
                ? Result<RetryAttempt, RetryCalculationError>.Ok(next)
                : Result<RetryAttempt, RetryCalculationError>.Err(RetryCalculationError.AttemptOverflow)
            : Result<RetryAttempt, RetryCalculationError>.Ok(First());

    public RetryAttempt? CheckedNext()
    {
        try
        {
            var next = checked(Value + 1);
            return new RetryAttempt(next);
        }
        catch (OverflowException) { return null; }
    }

    public bool Equals(RetryAttempt other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is RetryAttempt other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(RetryAttempt left, RetryAttempt right) => left.Equals(right);
    public static bool operator !=(RetryAttempt left, RetryAttempt right) => !left.Equals(right);
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

// ── Records / structs ──────────────────────────────────────────────────────

public sealed record DetachMetadata(
    DetachReason Reason,
    TimestampMs DetachedAt,
    string? LastExecutionStatus,
    string Summary);

public sealed record WorkspaceRecord(
    string Path,
    WorkspaceKey WorkspaceKey,
    bool CreatedNow,
    TimestampMs? CreatedAt,
    TimestampMs? UpdatedAt,
    TimestampMs? LastSeenTrackerRefreshAt);

public readonly struct RetryPolicy : IEquatable<RetryPolicy>
{
    public DurationMs ContinuationDelayMs { get; }
    public DurationMs FailureBaseDelayMs { get; }
    public DurationMs MaxBackoffMs { get; }

    [JsonConstructor]
    public RetryPolicy(DurationMs continuationDelayMs, DurationMs failureBaseDelayMs, DurationMs maxBackoffMs)
    {
        ContinuationDelayMs = continuationDelayMs;
        FailureBaseDelayMs = failureBaseDelayMs;
        MaxBackoffMs = maxBackoffMs;
    }

    public static RetryPolicy Default => new(
        DurationMs.New(1_000),
        DurationMs.New(10_000),
        DurationMs.New(300_000));

    public DurationMs FailureDelay(RetryAttempt attempt)
    {
        var raw = attempt.Get();
        var exponent = (int)Math.Min(raw == 0 ? 0 : raw - 1, 63);
        ulong multiplier;
        try { multiplier = checked(1UL << exponent); }
        catch (OverflowException) { multiplier = ulong.MaxValue; }
        var uncapped = FailureBaseDelayMs.SaturatingMul(multiplier);
        var max = MaxBackoffMs.AsU64();
        return DurationMs.New(Math.Min(uncapped.AsU64(), max));
    }

    public bool Equals(RetryPolicy other) =>
        ContinuationDelayMs == other.ContinuationDelayMs &&
        FailureBaseDelayMs == other.FailureBaseDelayMs &&
        MaxBackoffMs == other.MaxBackoffMs;
    public override bool Equals(object? obj) => obj is RetryPolicy other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ContinuationDelayMs, FailureBaseDelayMs, MaxBackoffMs);
    public static bool operator ==(RetryPolicy left, RetryPolicy right) => left.Equals(right);
    public static bool operator !=(RetryPolicy left, RetryPolicy right) => !left.Equals(right);
}

public sealed record RetryEntry(
    StringIdentifier<IssueId> IssueId,
    StringIdentifier<IssueIdentifier> Identifier,
    RetryAttempt Attempt,
    uint NormalRetryCount,
    TimestampMs ScheduledAt,
    TimestampMs DueAt,
    RetryReason Reason,
    string? Error)
{
    public static Result<RetryEntry, RetryCalculationError> Continuation(
        NormalizedIssue issue, RetryAttempt? previousAttempt, uint normalRetryCount,
        TimestampMs scheduledAt, RetryPolicy policy)
    {
        var attemptResult = RetryAttempt.After(previousAttempt);
        if (attemptResult.IsErr) return Result<RetryEntry, RetryCalculationError>.Err(attemptResult.Error);
        var attempt = attemptResult.Value;
        return Result<RetryEntry, RetryCalculationError>.Ok(new RetryEntry(
            issue.Id, issue.Identifier, attempt,
            normalRetryCount + 1,
            scheduledAt, scheduledAt.SaturatingAdd(policy.ContinuationDelayMs),
            RetryReason.Continuation, null));
    }

    public static Result<RetryEntry, RetryCalculationError> Failure(
        NormalizedIssue issue, RetryAttempt? previousAttempt, uint normalRetryCount,
        TimestampMs scheduledAt, RetryReason reason, string? error, RetryPolicy policy)
    {
        var attemptResult = RetryAttempt.After(previousAttempt);
        if (attemptResult.IsErr) return Result<RetryEntry, RetryCalculationError>.Err(attemptResult.Error);
        var attempt = attemptResult.Value;
        return Result<RetryEntry, RetryCalculationError>.Ok(new RetryEntry(
            issue.Id, issue.Identifier, attempt,
            normalRetryCount,
            scheduledAt, scheduledAt.SaturatingAdd(policy.FailureDelay(attempt)),
            reason, error));
    }
}

public sealed class RunAttempt
{
    public StringIdentifier<WorkerId> WorkerId { get; }
    public StringIdentifier<IssueId> IssueId { get; }
    public StringIdentifier<IssueIdentifier> IssueIdentifier { get; }
    public string WorkspacePath { get; }
    public TimestampMs ClaimedAt { get; }
    public TimestampMs? StartedAt { get; private set; }
    public RetryAttempt? Attempt { get; }
    public uint NormalRetryCount { get; private set; }
    public uint TurnCount { get; private set; }
    public uint MaxTurns { get; }

    public RunAttempt(
        StringIdentifier<WorkerId> workerId,
        StringIdentifier<IssueId> issueId,
        StringIdentifier<IssueIdentifier> issueIdentifier,
        string workspacePath,
        TimestampMs claimedAt,
        TimestampMs? startedAt,
        RetryAttempt? attempt,
        uint normalRetryCount,
        uint turnCount,
        uint maxTurns)
    {
        WorkerId = workerId;
        IssueId = issueId;
        IssueIdentifier = issueIdentifier;
        WorkspacePath = workspacePath;
        ClaimedAt = claimedAt;
        StartedAt = startedAt;
        Attempt = attempt;
        NormalRetryCount = normalRetryCount;
        TurnCount = turnCount;
        MaxTurns = maxTurns;
    }

    public static RunAttempt New(
        StringIdentifier<WorkerId> workerId,
        StringIdentifier<IssueId> issueId,
        StringIdentifier<IssueIdentifier> issueIdentifier,
        string workspacePath,
        TimestampMs claimedAt,
        RetryAttempt? attempt,
        uint maxTurns) => new(
            workerId, issueId, issueIdentifier, workspacePath, claimedAt,
            null, attempt, 0, 0, maxTurns);

    public RunAttempt WithNormalRetryCount(uint normalRetryCount) =>
        new(WorkerId, IssueId, IssueIdentifier, WorkspacePath, ClaimedAt,
            StartedAt, Attempt, normalRetryCount, TurnCount, MaxTurns);

    public RunAttempt MarkStarted(TimestampMs startedAt) =>
        new(WorkerId, IssueId, IssueIdentifier, WorkspacePath, ClaimedAt,
            startedAt, Attempt, NormalRetryCount, TurnCount, MaxTurns);

    public void RecordTurnStarted()
    {
        // ht: Rust saturating_add(1) — uint can't overflow in practice but match semantics.
        TurnCount = TurnCount == uint.MaxValue ? uint.MaxValue : TurnCount + 1;
    }

    public override bool Equals(object? obj) => obj is RunAttempt other &&
        WorkerId == other.WorkerId && IssueId == other.IssueId &&
        IssueIdentifier == other.IssueIdentifier && WorkspacePath == other.WorkspacePath &&
        ClaimedAt == other.ClaimedAt && Nullable.Equals(StartedAt, other.StartedAt) &&
        Nullable.Equals(Attempt, other.Attempt) && NormalRetryCount == other.NormalRetryCount &&
        TurnCount == other.TurnCount && MaxTurns == other.MaxTurns;
    public override int GetHashCode() => HashCode.Combine(WorkerId, IssueId, ClaimedAt, TurnCount);
}
public readonly struct StallMetadata : IEquatable<StallMetadata>
{
    public TimestampMs StartedAt { get; }
    public TimestampMs LastActivityAt { get; }
    public DurationMs IdleTimeoutMs { get; }
    public DurationMs? TotalRuntimeCapMs { get; }
    public TimestampMs StalledAt { get; }

    internal StallMetadata(TimestampMs startedAt, TimestampMs lastActivityAt,
        DurationMs idleTimeoutMs, DurationMs? totalRuntimeCapMs, TimestampMs stalledAt)
    {
        StartedAt = startedAt;
        LastActivityAt = lastActivityAt;
        IdleTimeoutMs = idleTimeoutMs;
        TotalRuntimeCapMs = totalRuntimeCapMs;
        StalledAt = stalledAt;
    }

    public static StallMetadata New(TimestampMs startedAt, DurationMs idleTimeoutMs) =>
        WithRuntimeCap(startedAt, idleTimeoutMs, null);

    public static StallMetadata WithRuntimeCap(
        TimestampMs startedAt, DurationMs idleTimeoutMs, DurationMs? totalRuntimeCapMs)
    {
        var idleDeadline = startedAt.SaturatingAdd(idleTimeoutMs);
        var stalledAt = totalRuntimeCapMs is { } cap
            ? TimestampMsMin(idleDeadline, startedAt.SaturatingAdd(cap))
            : idleDeadline;
        return new StallMetadata(startedAt, startedAt, idleTimeoutMs, totalRuntimeCapMs, stalledAt);
    }

    // ht: Returns a new StallMetadata with updated activity. Rust &mut self → C# returns updated struct.
    public StallMetadata ObserveActivity(TimestampMs activityAt, out bool advanced)
    {
        if (activityAt < LastActivityAt)
        {
            advanced = false;
            return this;
        }

        var idleDeadline = activityAt.SaturatingAdd(IdleTimeoutMs);
        var newStalledAt = TotalRuntimeCapMs is { } cap
            ? TimestampMsMin(idleDeadline, StartedAt.SaturatingAdd(cap))
            : idleDeadline;
        advanced = newStalledAt > StalledAt;
        return new StallMetadata(StartedAt, activityAt, IdleTimeoutMs, TotalRuntimeCapMs, newStalledAt);
    }

    public bool IsStalledAt(TimestampMs now) => now >= StalledAt;

    static TimestampMs TimestampMsMin(TimestampMs a, TimestampMs b) => a <= b ? a : b;

    public bool Equals(StallMetadata other) =>
        StartedAt == other.StartedAt && LastActivityAt == other.LastActivityAt &&
        IdleTimeoutMs == other.IdleTimeoutMs && TotalRuntimeCapMs == other.TotalRuntimeCapMs &&
        StalledAt == other.StalledAt;
    public override bool Equals(object? obj) => obj is StallMetadata other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(StartedAt, LastActivityAt, IdleTimeoutMs, TotalRuntimeCapMs, StalledAt);
    public static bool operator ==(StallMetadata left, StallMetadata right) => left.Equals(right);
    public static bool operator !=(StallMetadata left, StallMetadata right) => !left.Equals(right);
}

public sealed class ConversationActivityEvent
{
    public string EventId { get; set; }
    public TimestampMs HappenedAt { get; set; }
    public string Kind { get; set; }
    public string Summary { get; set; }
    public JsonElement? Payload { get; set; }
    public ulong Sequence { get; set; }

    public ConversationActivityEvent(string eventId, TimestampMs happenedAt, string kind, string summary, JsonElement? payload, ulong sequence)
    {
        EventId = eventId;
        HappenedAt = happenedAt;
        Kind = kind;
        Summary = summary;
        Payload = payload;
        Sequence = sequence;
    }

    public override bool Equals(object? obj) => obj is ConversationActivityEvent other &&
        EventId == other.EventId && HappenedAt == other.HappenedAt && Kind == other.Kind &&
        Summary == other.Summary && Sequence == other.Sequence &&
        PayloadEquals(Payload, other.Payload);
    public override int GetHashCode() => HashCode.Combine(EventId, HappenedAt, Kind, Summary, Sequence);

    static bool PayloadEquals(JsonElement? a, JsonElement? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return JsonElement.DeepEquals(a.Value, b.Value);
    }
}

public sealed class ConversationMetadata
{
    public StringIdentifier<ConversationId> ConversationId { get; set; }
    public string? ServerBaseUrl { get; set; }
    public string? TransportTarget { get; set; }
    public string? HttpAuthMode { get; set; }
    public string? WebsocketAuthMode { get; set; }
    public string? WebsocketQueryParamName { get; set; }
    public bool FreshConversation { get; set; }
    public string? RuntimeContractVersion { get; set; }
    public RuntimeStreamState StreamState { get; set; }
    public string? LastEventId { get; set; }
    public string? LastEventKind { get; set; }
    public TimestampMs? LastEventAt { get; set; }
    public string? LastEventSummary { get; set; }
    public List<ConversationActivityEvent> RecentActivity { get; set; } = new();
    public ulong InputTokens { get; set; }
    public ulong OutputTokens { get; set; }
    public ulong CacheReadTokens { get; set; }
    public ulong TotalTokens { get; set; }
    public ulong RuntimeSeconds { get; set; }
    public ulong NextActivitySequence { get; set; }

    public ConversationMetadata(StringIdentifier<ConversationId> conversationId)
    {
        ConversationId = conversationId;
    }

    const int MaxActivityEvents = 50;
    const string CodexAgentDeltaKind = "codex.item/agentMessage/delta";
    const string CodexPrefix = "Codex assistant: ";

    public void ObserveEvent(
        TimestampMs eventAt, string? eventId, string? eventKind, string? summary, JsonElement? payload)
    {
        if (LastEventAt is { } lastEventAt && eventAt < lastEventAt)
            return;

        LastEventAt = eventAt;
        LastEventId = eventId;
        LastEventKind = eventKind;
        LastEventSummary = summary;

        if (eventId is not null && eventKind is not null && summary is not null)
        {
            if (RecentActivity.Count > 0 && ShouldCoalesceCodexAgentDelta(RecentActivity[^1], eventId, eventKind))
            {
                var last = RecentActivity[^1];
                last.HappenedAt = eventAt;
                last.Summary = CoalescedCodexAgentSummary(last.Summary, summary);
                last.Payload = payload;
                LastEventSummary = last.Summary;
                return;
            }

            var sequence = NextActivitySequence;
            NextActivitySequence++;
            RecentActivity.Add(new ConversationActivityEvent(eventId, eventAt, eventKind, summary, payload, sequence));
            while (RecentActivity.Count > MaxActivityEvents)
                RecentActivity.RemoveAt(0);
        }
    }

    static bool ShouldCoalesceCodexAgentDelta(ConversationActivityEvent last, string eventId, string eventKind) =>
        eventKind == CodexAgentDeltaKind && last.Kind == eventKind && last.EventId == eventId;

    public static string CoalescedCodexAgentSummary(string existing, string next)
    {
        var existingText = existing.StartsWith(CodexPrefix) ? existing[CodexPrefix.Length..] : existing;
        var nextText = (next.StartsWith(CodexPrefix) ? next[CodexPrefix.Length..] : next).Trim();
        if (nextText.Length == 0) return existing;
        if (existingText.Trim().Length == 0) return $"{CodexPrefix}{nextText}";
        var separator = CodexDeltaNeedsSpace(existingText, nextText) ? " " : "";
        return $"{CodexPrefix}{existingText.TrimEnd()}{separator}{nextText}";
    }

    static bool CodexDeltaNeedsSpace(string existing, string next)
    {
        char? previous = null;
        for (var i = existing.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(existing[i]))
            {
                previous = existing[i];
                break;
            }
        }
        if (previous is null) return false;
        if (next.Length == 0) return false;
        var first = next[0];
        var firstPunct = first is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '\'' or '\u2019';
        var previousOpen = previous is '(' or '[' or '{' or '/' or '$';
        return !firstPunct && !previousOpen;
    }

    public void AddTokens(ulong input, ulong output)
    {
        InputTokens += input;
        OutputTokens += output;
        TotalTokens += input + output;
    }

    public void SetTokenUsage(ulong input, ulong output, ulong cacheRead, ulong total)
    {
        InputTokens = input;
        OutputTokens = output;
        CacheReadTokens = cacheRead;
        TotalTokens = total;
    }

    public ulong EffectiveTotalTokens() => TotalTokens > 0 ? TotalTokens : InputTokens + OutputTokens;

    public void AddRuntimeSeconds(ulong seconds) => RuntimeSeconds += seconds;
}

public sealed record WorkerOutcomeRecord(
    StringIdentifier<WorkerId> WorkerId,
    RetryAttempt? Attempt,
    WorkerOutcomeKind Outcome,
    TimestampMs StartedAt,
    TimestampMs FinishedAt,
    uint TurnCount,
    string? Summary,
    string? Error)
{
    public static WorkerOutcomeRecord FromRun(
        RunAttempt run, WorkerOutcomeKind outcome, TimestampMs finishedAt, string? summary, string? error)
        => new(
            run.WorkerId, run.Attempt, outcome,
            run.StartedAt ?? run.ClaimedAt,
            finishedAt, run.TurnCount, summary, error);
}

public sealed class RuntimeProgressSnapshot
{
    public RuntimeLivenessPhase Phase { get; set; }
    public LivenessState LivenessState { get; set; }
    public ulong EventCount { get; set; }
    public ulong EventDelta { get; set; }
    public ulong InputTokens { get; set; }
    public ulong InputTokenDelta { get; set; }
    public ulong OutputTokens { get; set; }
    public ulong OutputTokenDelta { get; set; }
    public ulong CacheReadTokens { get; set; }
    public ulong CacheReadTokenDelta { get; set; }
    public string? ExecutionStatus { get; set; }
    public StreamHealth StreamHealth { get; set; }
    public HistorySyncStatus HistorySyncStatus { get; set; }
    public ReconnectStatus ReconnectStatus { get; set; }
    public TimestampMs? LastActivityAt { get; set; }
    public TimestampMs? StallDeadlineAt { get; set; }
    public string? LastEventCursor { get; set; }
    public string? LastEventKind { get; set; }
    public TimestampMs? LastEventAt { get; set; }
    public DetachMetadata? DetachMetadata { get; set; }

    public RuntimeProgressSnapshot() { }

    public static RuntimeProgressSnapshot Initial(RuntimeLivenessPhase phase) => new()
    {
        Phase = phase,
        LivenessState = phase.LivenessState(),
        EventCount = 0, EventDelta = 0,
        InputTokens = 0, InputTokenDelta = 0,
        OutputTokens = 0, OutputTokenDelta = 0,
        CacheReadTokens = 0, CacheReadTokenDelta = 0,
        ExecutionStatus = null,
        StreamHealth = Domain.StreamHealth.Unknown,
        HistorySyncStatus = Domain.HistorySyncStatus.Idle,
        ReconnectStatus = Domain.ReconnectStatus.Connected,
        LastActivityAt = null, StallDeadlineAt = null,
        LastEventCursor = null, LastEventKind = null, LastEventAt = null,
        DetachMetadata = null,
    };

    public RuntimeProgressSnapshotBuilder UpdateWith(RuntimeLivenessPhase phase) => new(this, phase);

    public override bool Equals(object? obj)
    {
        if (obj is not RuntimeProgressSnapshot other) return false;
        return Phase == other.Phase && LivenessState == other.LivenessState &&
            EventCount == other.EventCount && EventDelta == other.EventDelta &&
            InputTokens == other.InputTokens && InputTokenDelta == other.InputTokenDelta &&
            OutputTokens == other.OutputTokens && OutputTokenDelta == other.OutputTokenDelta &&
            CacheReadTokens == other.CacheReadTokens && CacheReadTokenDelta == other.CacheReadTokenDelta &&
            ExecutionStatus == other.ExecutionStatus && StreamHealth == other.StreamHealth &&
            HistorySyncStatus == other.HistorySyncStatus && ReconnectStatus == other.ReconnectStatus &&
            Nullable.Equals(LastActivityAt, other.LastActivityAt) &&
            Nullable.Equals(StallDeadlineAt, other.StallDeadlineAt) &&
            LastEventCursor == other.LastEventCursor && LastEventKind == other.LastEventKind &&
            Nullable.Equals(LastEventAt, other.LastEventAt) &&
            DetachMetadataEquals(DetachMetadata, other.DetachMetadata);
    }
    public override int GetHashCode() => HashCode.Combine(Phase, EventCount, InputTokens, OutputTokens);
    static bool DetachMetadataEquals(DetachMetadata? a, DetachMetadata? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a == b;
    }
}

public sealed class RuntimeProgressSnapshotBuilder
{
    readonly RuntimeProgressSnapshot _previous;
    RuntimeLivenessPhase _phase;
    ulong _eventCount;
    ulong _inputTokens;
    ulong _outputTokens;
    ulong _cacheReadTokens;
    string? _executionStatus;
    StreamHealth _streamHealth;
    HistorySyncStatus _historySyncStatus;
    ReconnectStatus _reconnectStatus;
    TimestampMs? _lastActivityAt;
    TimestampMs? _stallDeadlineAt;
    string? _lastEventCursor;
    string? _lastEventKind;
    TimestampMs? _lastEventAt;
    DetachMetadata? _detachMetadata;

    internal RuntimeProgressSnapshotBuilder(RuntimeProgressSnapshot previous, RuntimeLivenessPhase phase)
    {
        _previous = previous;
        _phase = phase;
        _eventCount = previous.EventCount;
        _inputTokens = previous.InputTokens;
        _outputTokens = previous.OutputTokens;
        _cacheReadTokens = previous.CacheReadTokens;
        _executionStatus = previous.ExecutionStatus;
        _streamHealth = previous.StreamHealth;
        _historySyncStatus = previous.HistorySyncStatus;
        _reconnectStatus = previous.ReconnectStatus;
        _lastActivityAt = previous.LastActivityAt;
        _stallDeadlineAt = previous.StallDeadlineAt;
        _lastEventCursor = previous.LastEventCursor;
        _lastEventKind = previous.LastEventKind;
        _lastEventAt = previous.LastEventAt;
        _detachMetadata = previous.DetachMetadata;
    }

    public RuntimeProgressSnapshotBuilder WithEventCount(ulong count) { _eventCount = count; return this; }
    public RuntimeProgressSnapshotBuilder WithInputTokens(ulong count) { _inputTokens = count; return this; }
    public RuntimeProgressSnapshotBuilder WithOutputTokens(ulong count) { _outputTokens = count; return this; }
    public RuntimeProgressSnapshotBuilder WithCacheReadTokens(ulong count) { _cacheReadTokens = count; return this; }
    public RuntimeProgressSnapshotBuilder WithExecutionStatus(string? status) { _executionStatus = status; return this; }
    public RuntimeProgressSnapshotBuilder WithStreamHealth(StreamHealth health) { _streamHealth = health; return this; }
    public RuntimeProgressSnapshotBuilder WithHistorySyncStatus(HistorySyncStatus status) { _historySyncStatus = status; return this; }
    public RuntimeProgressSnapshotBuilder WithReconnectStatus(ReconnectStatus status) { _reconnectStatus = status; return this; }
    public RuntimeProgressSnapshotBuilder WithLastActivityAt(TimestampMs? ts) { _lastActivityAt = ts; return this; }
    public RuntimeProgressSnapshotBuilder WithStallDeadlineAt(TimestampMs? ts) { _stallDeadlineAt = ts; return this; }
    public RuntimeProgressSnapshotBuilder WithLastEventCursor(string? cursor) { _lastEventCursor = cursor; return this; }
    public RuntimeProgressSnapshotBuilder WithLastEventKind(string? kind) { _lastEventKind = kind; return this; }
    public RuntimeProgressSnapshotBuilder WithLastEventAt(TimestampMs? ts) { _lastEventAt = ts; return this; }
    public RuntimeProgressSnapshotBuilder WithDetachMetadata(DetachMetadata? metadata) { _detachMetadata = metadata; return this; }

    public RuntimeProgressSnapshot Build()
    {
        static ulong SaturatingSub(ulong a, ulong b) => a >= b ? a - b : 0;
        return new RuntimeProgressSnapshot
        {
            EventDelta = SaturatingSub(_eventCount, _previous.EventCount),
            InputTokenDelta = SaturatingSub(_inputTokens, _previous.InputTokens),
            OutputTokenDelta = SaturatingSub(_outputTokens, _previous.OutputTokens),
            CacheReadTokenDelta = SaturatingSub(_cacheReadTokens, _previous.CacheReadTokens),
            LivenessState = _phase.LivenessState(),
            Phase = _phase,
            EventCount = _eventCount,
            InputTokens = _inputTokens,
            OutputTokens = _outputTokens,
            CacheReadTokens = _cacheReadTokens,
            ExecutionStatus = _executionStatus,
            StreamHealth = _streamHealth,
            HistorySyncStatus = _historySyncStatus,
            ReconnectStatus = _reconnectStatus,
            LastActivityAt = _lastActivityAt,
            StallDeadlineAt = _stallDeadlineAt,
            LastEventCursor = _lastEventCursor,
            LastEventKind = _lastEventKind,
            LastEventAt = _lastEventAt,
            DetachMetadata = _detachMetadata,
        };
    }
}
