using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Domain.Tests;

public class RuntimeTests
{
    static TimestampMs ts(ulong v) => TimestampMs.New(v);
    static NormalizedIssue sampleIssue() => new(
        StringIdentifier<IssueId>.New("ISS-1").Value,
        StringIdentifier<IssueIdentifier>.New("ISS-1").Value,
        "Title", null, null,
        new IssueState(null, "Open", IssueStateCategory.Active),
        null, null, new(), null, new(), new(), null, null);

    static StringIdentifier<WorkerId> wid() => StringIdentifier<WorkerId>.New("w-1").Value;
    static StringIdentifier<IssueId> iid() => StringIdentifier<IssueId>.New("ISS-1").Value;
    static StringIdentifier<IssueIdentifier> iident() => StringIdentifier<IssueIdentifier>.New("ISS-1").Value;

    // ── Enum snake_case Theory ──────────────────────────────────────────────

    [Theory]
    [InlineData(RuntimeLivenessPhase.WaitingOnPriorTurn, "waiting_on_prior_turn")]
    [InlineData(RuntimeLivenessPhase.RunningTurn, "running_turn")]
    [InlineData(RuntimeLivenessPhase.Quiet, "quiet")]
    [InlineData(RuntimeLivenessPhase.Degraded, "degraded")]
    [InlineData(RuntimeLivenessPhase.Reconciling, "reconciling")]
    [InlineData(RuntimeLivenessPhase.Cancelling, "cancelling")]
    [InlineData(RuntimeLivenessPhase.Stalled, "stalled")]
    [InlineData(RuntimeLivenessPhase.Detached, "detached")]
    [InlineData(RuntimeLivenessPhase.Terminal, "terminal")]
    public void RuntimeLivenessPhase_SnakeCaseJson(RuntimeLivenessPhase phase, string expected)
    {
        var json = JsonSerializer.Serialize(phase, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
        var deserialized = JsonSerializer.Deserialize<RuntimeLivenessPhase>(json, DomainJsonOptions.Default);
        Assert.Equal(phase, deserialized);
    }

    [Theory]
    [InlineData(LivenessState.Active, "active")]
    [InlineData(LivenessState.Quiet, "quiet")]
    [InlineData(LivenessState.Degraded, "degraded")]
    [InlineData(LivenessState.Stalled, "stalled")]
    [InlineData(LivenessState.Detached, "detached")]
    [InlineData(LivenessState.Terminal, "terminal")]
    public void LivenessState_SnakeCaseJson(LivenessState state, string expected)
    {
        var json = JsonSerializer.Serialize(state, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(StreamHealth.Unknown, "unknown")]
    [InlineData(StreamHealth.Attaching, "attaching")]
    [InlineData(StreamHealth.HistorySyncing, "history_syncing")]
    [InlineData(StreamHealth.Ready, "ready")]
    [InlineData(StreamHealth.Reconnecting, "reconnecting")]
    [InlineData(StreamHealth.Disconnected, "disconnected")]
    [InlineData(StreamHealth.Failed, "failed")]
    [InlineData(StreamHealth.Detached, "detached")]
    public void StreamHealth_SnakeCaseJson(StreamHealth health, string expected)
    {
        var json = JsonSerializer.Serialize(health, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(HistorySyncStatus.Idle, "idle")]
    [InlineData(HistorySyncStatus.InProgress, "in_progress")]
    [InlineData(HistorySyncStatus.Synced, "synced")]
    [InlineData(HistorySyncStatus.Stale, "stale")]
    [InlineData(HistorySyncStatus.Failed, "failed")]
    public void HistorySyncStatus_SnakeCaseJson(HistorySyncStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(ReconnectStatus.Connected, "connected")]
    [InlineData(ReconnectStatus.Pending, "pending")]
    [InlineData(ReconnectStatus.Exhausted, "exhausted")]
    [InlineData(ReconnectStatus.Closed, "closed")]
    public void ReconnectStatus_SnakeCaseJson(ReconnectStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(DetachReason.CancelFailed, "cancel_failed")]
    [InlineData(DetachReason.CancelUnsupported, "cancel_unsupported")]
    [InlineData(DetachReason.Unreachable, "unreachable")]
    [InlineData(DetachReason.WorkerShutdown, "worker_shutdown")]
    public void DetachReason_SnakeCaseJson(DetachReason reason, string expected)
    {
        var json = JsonSerializer.Serialize(reason, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(RuntimeStreamState.Detached, "detached")]
    [InlineData(RuntimeStreamState.Attaching, "attaching")]
    [InlineData(RuntimeStreamState.Ready, "ready")]
    [InlineData(RuntimeStreamState.Reconnecting, "reconnecting")]
    [InlineData(RuntimeStreamState.Closed, "closed")]
    [InlineData(RuntimeStreamState.Failed, "failed")]
    public void RuntimeStreamState_SnakeCaseJson(RuntimeStreamState state, string expected)
    {
        var json = JsonSerializer.Serialize(state, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(RetryReason.Continuation, "continuation")]
    [InlineData(RetryReason.Failure, "failure")]
    [InlineData(RetryReason.Stalled, "stalled")]
    [InlineData(RetryReason.Cancelled, "cancelled")]
    [InlineData(RetryReason.Reconciliation, "reconciliation")]
    public void RetryReason_SnakeCaseJson(RetryReason reason, string expected)
    {
        var json = JsonSerializer.Serialize(reason, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(WorkerOutcomeKind.Succeeded, "succeeded")]
    [InlineData(WorkerOutcomeKind.Failed, "failed")]
    [InlineData(WorkerOutcomeKind.TimedOut, "timed_out")]
    [InlineData(WorkerOutcomeKind.Stalled, "stalled")]
    [InlineData(WorkerOutcomeKind.Cancelled, "cancelled")]
    [InlineData(WorkerOutcomeKind.Detached, "detached")]
    [InlineData(WorkerOutcomeKind.CancelFailed, "cancel_failed")]
    public void WorkerOutcomeKind_SnakeCaseJson(WorkerOutcomeKind kind, string expected)
    {
        var json = JsonSerializer.Serialize(kind, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(ReleaseReason.Completed, "completed")]
    [InlineData(ReleaseReason.TrackerInactive, "tracker_inactive")]
    [InlineData(ReleaseReason.TrackerTerminal, "tracker_terminal")]
    [InlineData(ReleaseReason.Cancelled, "cancelled")]
    [InlineData(ReleaseReason.RetryExhausted, "retry_exhausted")]
    public void ReleaseReason_SnakeCaseJson(ReleaseReason reason, string expected)
    {
        var json = JsonSerializer.Serialize(reason, DomainJsonOptions.Default);
        Assert.Equal($"\"{expected}\"", json);
    }

    // ── RetryAttempt ────────────────────────────────────────────────────────

    [Fact]
    public void RetryAttempt_First_IsOne()
    {
        Assert.Equal(1u, RetryAttempt.First().Get());
    }

    [Fact]
    public void RetryAttempt_New_Zero_ReturnsZeroAttempt()
    {
        var r = RetryAttempt.New(0);
        Assert.True(r.IsErr);
        Assert.Equal(RetryCalculationError.ZeroAttempt, r.Error);
    }

    [Fact]
    public void RetryAttempt_New_Five_GetIsFive()
    {
        var r = RetryAttempt.New(5);
        Assert.True(r.IsOk);
        Assert.Equal(5u, r.Value.Get());
    }

    [Fact]
    public void RetryAttempt_After_None_ReturnsFirst()
    {
        var r = RetryAttempt.After(null);
        Assert.True(r.IsOk);
        Assert.Equal(1u, r.Value.Get());
    }

    [Fact]
    public void RetryAttempt_After_Some_Three_ReturnsFour()
    {
        var r = RetryAttempt.After(RetryAttempt.New(3).Value);
        Assert.True(r.IsOk);
        Assert.Equal(4u, r.Value.Get());
    }

    [Fact]
    public void RetryAttempt_After_Some_UintMax_ReturnsAttemptOverflow()
    {
        var r = RetryAttempt.After(RetryAttempt.New(uint.MaxValue).Value);
        Assert.True(r.IsErr);
        Assert.Equal(RetryCalculationError.AttemptOverflow, r.Error);
    }

    [Fact]
    public void RetryAttempt_CheckedNext_AtMax_ReturnsNull()
    {
        var max = RetryAttempt.New(uint.MaxValue).Value;
        Assert.Null(max.CheckedNext());
    }

    [Fact]
    public void RetryAttempt_Json_TransparentBareNumber()
    {
        var json = JsonSerializer.Serialize(RetryAttempt.New(5).Value, DomainJsonOptions.Default);
        Assert.Equal("5", json);
        var deserialized = JsonSerializer.Deserialize<RetryAttempt>("5", DomainJsonOptions.Default);
        Assert.Equal(5u, deserialized.Get());
    }

    [Fact]
    public void RetryAttempt_Json_RejectZero()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RetryAttempt>("0", DomainJsonOptions.Default));
    }

    [Fact]
    public void RetryAttempt_ToString_IsBareNumber()
    {
        Assert.Equal("5", RetryAttempt.New(5).Value.ToString());
    }

    // ── RetryPolicy math ────────────────────────────────────────────────────

    [Fact]
    public void RetryPolicy_Default_Values()
    {
        var d = RetryPolicy.Default;
        Assert.Equal(1000UL, d.ContinuationDelayMs.AsU64());
        Assert.Equal(10000UL, d.FailureBaseDelayMs.AsU64());
        Assert.Equal(300000UL, d.MaxBackoffMs.AsU64());
    }

    [Fact]
    public void RetryPolicy_FailureDelay_Attempt1_Is10000()
    {
        Assert.Equal(DurationMs.New(10000),
            RetryPolicy.Default.FailureDelay(RetryAttempt.New(1).Value));
    }

    [Fact]
    public void RetryPolicy_FailureDelay_Attempt2_Is20000()
    {
        Assert.Equal(DurationMs.New(20000),
            RetryPolicy.Default.FailureDelay(RetryAttempt.New(2).Value));
    }

    [Fact]
    public void RetryPolicy_FailureDelay_Attempt5_Is160000()
    {
        Assert.Equal(DurationMs.New(160000),
            RetryPolicy.Default.FailureDelay(RetryAttempt.New(5).Value));
    }

    [Fact]
    public void RetryPolicy_FailureDelay_Attempt10_CappedAt300000()
    {
        Assert.Equal(DurationMs.New(300000),
            RetryPolicy.Default.FailureDelay(RetryAttempt.New(10).Value));
    }

    [Fact]
    public void RetryPolicy_Capped_FailureDelay_Attempt5_Is25000()
    {
        var capped = new RetryPolicy(
            DurationMs.New(1_000), DurationMs.New(10_000), DurationMs.New(25_000));
        Assert.Equal(DurationMs.New(25_000),
            capped.FailureDelay(RetryAttempt.New(5).Value));
    }

    // ── RetryEntry factories ────────────────────────────────────────────────

    [Fact]
    public void RetryEntry_Continuation_MatchesRustTest()
    {
        var issue = sampleIssue();
        var continuation = RetryEntry.Continuation(issue, null, 0, ts(100), RetryPolicy.Default).Value;
        Assert.Equal(1u, continuation.Attempt.Get());
        Assert.Equal(1u, continuation.NormalRetryCount);
        Assert.Equal(ts(1100), continuation.DueAt);
        Assert.Equal(RetryReason.Continuation, continuation.Reason);
        Assert.Null(continuation.Error);
    }

    [Fact]
    public void RetryEntry_Failure_MatchesRustTest()
    {
        var issue = sampleIssue();
        var failure = RetryEntry.Failure(
            issue, null, 1, ts(100), RetryReason.Failure, "first failure", RetryPolicy.Default).Value;
        Assert.Equal(1u, failure.Attempt.Get());
        Assert.Equal(ts(10100), failure.DueAt);
        Assert.Equal(RetryReason.Failure, failure.Reason);
        Assert.Equal("first failure", failure.Error);
    }

    // Combined mirror of Rust lib.rs retry_delay_math_matches_continuation_and_failure_rules
    [Fact]
    public void RetryDelayMathMatchesContinuationAndFailureRules()
    {
        var issue = sampleIssue();
        var policy = RetryPolicy.Default;

        // 1) continuation from None, count=0, ts(100) → attempt=1, normal_retry_count=1, due_at=ts(1100)
        var continuation = RetryEntry.Continuation(issue, null, 0, ts(100), policy).Value;
        Assert.Equal(1u, continuation.Attempt.Get());
        Assert.Equal(1u, continuation.NormalRetryCount);
        Assert.Equal(ts(1_100), continuation.DueAt);

        // 2) failure from None, count=1, ts(100), Failure reason → attempt=1, due_at=ts(10100)
        var firstFailure = RetryEntry.Failure(
            issue, null, 1, ts(100), RetryReason.Failure, "first failure", policy).Value;
        Assert.Equal(1u, firstFailure.Attempt.Get());
        Assert.Equal(ts(10_100), firstFailure.DueAt);

        // 3) capped_policy (max=25000), attempt=5 → failure_delay == 25000
        var cappedPolicy = new RetryPolicy(
            DurationMs.New(1_000), DurationMs.New(10_000), DurationMs.New(25_000));
        var fifthAttempt = RetryAttempt.New(5).Value;
        Assert.Equal(DurationMs.New(25_000), cappedPolicy.FailureDelay(fifthAttempt));
    }

    // ── StallMetadata ───────────────────────────────────────────────────────

    [Fact]
    public void StallMetadata_New_StalledAtIsStartedPlusIdle()
    {
        var m = StallMetadata.New(ts(100), DurationMs.New(300));
        Assert.Equal(ts(400), m.StalledAt);
        Assert.Equal(ts(100), m.LastActivityAt);
    }

    [Fact]
    public void StallMetadata_WithRuntimeCap_StalledAtIsMin()
    {
        var m = StallMetadata.WithRuntimeCap(ts(100), DurationMs.New(300), DurationMs.New(200));
        Assert.Equal(ts(300), m.StalledAt);
    }

    [Fact]
    public void StallMetadata_ObserveActivity_AdvancesDeadline()
    {
        var m = StallMetadata.New(ts(100), DurationMs.New(300));
        var updated = m.ObserveActivity(ts(150), out var advanced);
        Assert.True(advanced);
        Assert.Equal(ts(150), updated.LastActivityAt);
        Assert.Equal(ts(450), updated.StalledAt);
    }

    [Fact]
    public void StallMetadata_ObserveActivity_OutOfOrder_ReturnsFalse()
    {
        var m = StallMetadata.New(ts(100), DurationMs.New(300));
        var updated = m.ObserveActivity(ts(50), out var advanced);
        Assert.False(advanced);
        Assert.Equal(ts(100), updated.LastActivityAt);
        Assert.Equal(ts(400), updated.StalledAt);
    }

    [Fact]
    public void StallMetadata_IsStalledAt_Boundary()
    {
        var m = StallMetadata.New(ts(100), DurationMs.New(300));
        Assert.False(m.IsStalledAt(ts(399)));
        Assert.True(m.IsStalledAt(ts(400)));
    }

    [Fact]
    public void StallMetadata_Json_AliasStallTimeoutMs()
    {
        var json = "{\"started_at\":100,\"last_activity_at\":100,\"stall_timeout_ms\":300,\"stalled_at\":400}";
        var m = JsonSerializer.Deserialize<StallMetadata>(json, DomainJsonOptions.Default);
        Assert.Equal(DurationMs.New(300), m.IdleTimeoutMs);
        Assert.Equal(ts(100), m.StartedAt);
    }

    [Fact]
    public void StallMetadata_Json_OmitsTotalRuntimeCapWhenNull()
    {
        var m = StallMetadata.New(ts(100), DurationMs.New(300));
        var json = JsonSerializer.Serialize(m, DomainJsonOptions.Default);
        Assert.DoesNotContain("total_runtime_cap_ms", json);
        Assert.Contains("idle_timeout_ms", json);
    }

    [Fact]
    public void StallMetadata_Json_WritesTotalRuntimeCapWhenSome()
    {
        var m = StallMetadata.WithRuntimeCap(ts(100), DurationMs.New(300), DurationMs.New(200));
        var json = JsonSerializer.Serialize(m, DomainJsonOptions.Default);
        Assert.Contains("total_runtime_cap_ms", json);
    }

    [Fact]
    public void StallMetadata_Json_RoundTrip()
    {
        var m = StallMetadata.WithRuntimeCap(ts(100), DurationMs.New(300), DurationMs.New(200));
        var json = JsonSerializer.Serialize(m, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<StallMetadata>(json, DomainJsonOptions.Default);
        Assert.Equal(m, deserialized);
    }

    // ── ConversationMetadata ────────────────────────────────────────────────

    static ConversationMetadata sampleConversation() => new(
        StringIdentifier<ConversationId>.New("conv_1").Value)
    {
        FreshConversation = true,
        StreamState = RuntimeStreamState.Ready,
    };

    [Fact]
    public void ConversationMetadata_CodexAgentDeltasCoalesceIntoOneRow()
    {
        var conv = sampleConversation();
        string[] summaries = { "Codex assistant: poll", "Codex assistant: is", "Codex assistant: still" };
        for (var i = 0; i < summaries.Length; i++)
        {
            conv.ObserveEvent(
                ts(1000 + (ulong)i),
                "item-1",
                "codex.item/agentMessage/delta",
                summaries[i],
                null);
        }
        Assert.Single(conv.RecentActivity);
        Assert.Equal("Codex assistant: poll is still", conv.RecentActivity[0].Summary);
        Assert.Equal("Codex assistant: poll is still", conv.LastEventSummary);

        conv.ObserveEvent(ts(2000), "item-1", "codex.item/completed", "Codex item completed", null);
        Assert.Equal(2, conv.RecentActivity.Count);
    }

    [Fact]
    public void ConversationMetadata_ObserveEvent_OutOfOrderIgnored()
    {
        var conv = sampleConversation();
        conv.ObserveEvent(ts(60), "e1", "kind", "s1", null);
        conv.ObserveEvent(ts(55), "e2", "kind", "s2", null);
        Assert.Equal(ts(60), conv.LastEventAt);
        Assert.Equal("e1", conv.LastEventId);
    }

    [Fact]
    public void ConversationMetadata_AddTokens()
    {
        var conv = sampleConversation();
        conv.AddTokens(100, 50);
        Assert.Equal(100UL, conv.InputTokens);
        Assert.Equal(50UL, conv.OutputTokens);
        Assert.Equal(150UL, conv.TotalTokens);
    }

    [Fact]
    public void ConversationMetadata_SetTokenUsage()
    {
        var conv = sampleConversation();
        conv.SetTokenUsage(10, 20, 5, 35);
        Assert.Equal(10UL, conv.InputTokens);
        Assert.Equal(20UL, conv.OutputTokens);
        Assert.Equal(5UL, conv.CacheReadTokens);
        Assert.Equal(35UL, conv.TotalTokens);
    }

    [Fact]
    public void ConversationMetadata_EffectiveTotalTokens_TotalPositive()
    {
        var conv = sampleConversation();
        conv.SetTokenUsage(10, 20, 0, 35);
        Assert.Equal(35UL, conv.EffectiveTotalTokens());
    }

    [Fact]
    public void ConversationMetadata_EffectiveTotalTokens_TotalZero_FallsBackToInputPlusOutput()
    {
        var conv = sampleConversation();
        conv.AddTokens(100, 50);
        conv.TotalTokens = 0;
        Assert.Equal(150UL, conv.EffectiveTotalTokens());
    }

    [Fact]
    public void ConversationMetadata_Json_OmitsRecentActivityWhenEmpty()
    {
        var conv = sampleConversation();
        var json = JsonSerializer.Serialize(conv, DomainJsonOptions.Default);
        Assert.DoesNotContain("recent_activity", json);
    }

    [Fact]
    public void ConversationMetadata_Json_OmitsTransportTargetWhenNull()
    {
        var conv = sampleConversation();
        var json = JsonSerializer.Serialize(conv, DomainJsonOptions.Default);
        Assert.DoesNotContain("transport_target", json);
        Assert.DoesNotContain("http_auth_mode", json);
        Assert.DoesNotContain("websocket_auth_mode", json);
        Assert.DoesNotContain("websocket_query_param_name", json);
    }

    [Fact]
    public void ConversationMetadata_Json_RoundTrip()
    {
        var conv = sampleConversation();
        conv.ObserveEvent(ts(1000), "e1", "kind", "summary", null);
        conv.AddTokens(100, 50);
        conv.TransportTarget = "ws://localhost";
        var json = JsonSerializer.Serialize(conv, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<ConversationMetadata>(json, DomainJsonOptions.Default);
        Assert.Equal(conv.ConversationId, deserialized.ConversationId);
        Assert.Equal("ws://localhost", deserialized.TransportTarget);
        Assert.Single(deserialized.RecentActivity);
        Assert.Equal(100UL, deserialized.InputTokens);
    }

    // ── RuntimeProgressSnapshot ─────────────────────────────────────────────

    [Fact]
    public void RuntimeProgressSnapshot_Initial_CountersZero()
    {
        var snap = RuntimeProgressSnapshot.Initial(RuntimeLivenessPhase.RunningTurn);
        Assert.Equal(0UL, snap.EventCount);
        Assert.Equal(0UL, snap.InputTokens);
        Assert.Equal(StreamHealth.Unknown, snap.StreamHealth);
        Assert.Equal(HistorySyncStatus.Idle, snap.HistorySyncStatus);
        Assert.Equal(ReconnectStatus.Connected, snap.ReconnectStatus);
        Assert.Equal(LivenessState.Active, snap.LivenessState);
    }

    [Fact]
    public void RuntimeProgressSnapshot_UpdateWith_ComputesDelta()
    {
        var snap = RuntimeProgressSnapshot.Initial(RuntimeLivenessPhase.RunningTurn);
        snap.EventCount = 5;
        var updated = snap.UpdateWith(RuntimeLivenessPhase.Quiet)
            .WithEventCount(15)
            .Build();
        Assert.Equal(15UL, updated.EventCount);
        Assert.Equal(10UL, updated.EventDelta);
        Assert.Equal(LivenessState.Quiet, updated.LivenessState);
    }

    [Fact]
    public void RuntimeProgressSnapshot_Json_OmitsDetachMetadataWhenNull()
    {
        var snap = RuntimeProgressSnapshot.Initial(RuntimeLivenessPhase.RunningTurn);
        var json = JsonSerializer.Serialize(snap, DomainJsonOptions.Default);
        Assert.DoesNotContain("detach_metadata", json);
    }

    [Fact]
    public void RuntimeProgressSnapshot_Json_WritesDetachMetadataWhenSome()
    {
        var snap = RuntimeProgressSnapshot.Initial(RuntimeLivenessPhase.Detached);
        snap.DetachMetadata = new DetachMetadata(DetachReason.Unreachable, ts(100), null, "lost");
        var json = JsonSerializer.Serialize(snap, DomainJsonOptions.Default);
        Assert.Contains("detach_metadata", json);
    }

    [Fact]
    public void RuntimeProgressSnapshot_Json_RoundTrip()
    {
        var snap = RuntimeProgressSnapshot.Initial(RuntimeLivenessPhase.RunningTurn);
        snap.EventCount = 10;
        snap.InputTokens = 500;
        snap.StreamHealth = StreamHealth.Ready;
        snap.LastActivityAt = ts(999);
        var json = JsonSerializer.Serialize(snap, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<RuntimeProgressSnapshot>(json, DomainJsonOptions.Default);
        Assert.Equal(snap, deserialized);
    }

    // ── RunAttempt ──────────────────────────────────────────────────────────

    [Fact]
    public void RunAttempt_New_StartedAtNull_TurnCountZero()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), null, 10);
        Assert.Null(run.StartedAt);
        Assert.Equal(0u, run.TurnCount);
        Assert.Equal(0u, run.NormalRetryCount);
        Assert.Equal(10u, run.MaxTurns);
    }

    [Fact]
    public void RunAttempt_MarkStarted_SetsStartedAt()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), null, 10);
        var started = run.MarkStarted(ts(50));
        Assert.Equal(ts(50), started.StartedAt);
    }

    [Fact]
    public void RunAttempt_RecordTurnStarted_Increments()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), null, 10);
        run.RecordTurnStarted();
        run.RecordTurnStarted();
        Assert.Equal(2u, run.TurnCount);
    }

    [Fact]
    public void RunAttempt_WithNormalRetryCount_SetsCount()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), null, 10);
        var updated = run.WithNormalRetryCount(3);
        Assert.Equal(3u, updated.NormalRetryCount);
    }

    [Fact]
    public void RunAttempt_Json_RoundTrip()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), RetryAttempt.New(2).Value, 10);
        run.RecordTurnStarted();
        var json = JsonSerializer.Serialize(run, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<RunAttempt>(json, DomainJsonOptions.Default);
        Assert.Equal(run, deserialized);
    }

    // ── WorkerOutcomeRecord ─────────────────────────────────────────────────

    [Fact]
    public void WorkerOutcomeRecord_FromRun_UsesStartedAtWhenAvailable()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), null, 10)
            .MarkStarted(ts(50));
        run.RecordTurnStarted();
        run.RecordTurnStarted();
        var outcome = WorkerOutcomeRecord.FromRun(run, WorkerOutcomeKind.Succeeded, ts(100), "ok", null);
        Assert.Equal(ts(50), outcome.StartedAt);
        Assert.Equal(ts(100), outcome.FinishedAt);
        Assert.Equal(2u, outcome.TurnCount);
        Assert.Equal("ok", outcome.Summary);
    }

    [Fact]
    public void WorkerOutcomeRecord_FromRun_FallsBackToClaimedAt()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), null, 10);
        var outcome = WorkerOutcomeRecord.FromRun(run, WorkerOutcomeKind.Failed, ts(100), null, "err");
        Assert.Equal(ts(40), outcome.StartedAt);
        Assert.Equal("err", outcome.Error);
    }

    [Fact]
    public void WorkerOutcomeRecord_Json_RoundTrip()
    {
        var run = RunAttempt.New(wid(), iid(), iident(), "/path", ts(40), RetryAttempt.New(1).Value, 10)
            .MarkStarted(ts(50));
        var outcome = WorkerOutcomeRecord.FromRun(run, WorkerOutcomeKind.Succeeded, ts(100), "ok", null);
        var json = JsonSerializer.Serialize(outcome, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<WorkerOutcomeRecord>(json, DomainJsonOptions.Default);
        Assert.Equal(outcome, deserialized);
    }

    // ── ReleaseReason ───────────────────────────────────────────────────────

    [Fact]
    public void ReleaseReason_PreservesReactivationState_TrackerInactive_True()
    {
        Assert.True(ReleaseReason.TrackerInactive.PreservesReactivationState());
    }

    [Theory]
    [InlineData(ReleaseReason.Completed)]
    [InlineData(ReleaseReason.TrackerTerminal)]
    [InlineData(ReleaseReason.Cancelled)]
    [InlineData(ReleaseReason.RetryExhausted)]
    public void ReleaseReason_PreservesReactivationState_Others_False(ReleaseReason reason)
    {
        Assert.False(reason.PreservesReactivationState());
    }

    // ── RuntimeLivenessPhase → LivenessState mapping ────────────────────────

    [Theory]
    [InlineData(RuntimeLivenessPhase.WaitingOnPriorTurn, LivenessState.Active)]
    [InlineData(RuntimeLivenessPhase.RunningTurn, LivenessState.Active)]
    [InlineData(RuntimeLivenessPhase.Quiet, LivenessState.Quiet)]
    [InlineData(RuntimeLivenessPhase.Degraded, LivenessState.Degraded)]
    [InlineData(RuntimeLivenessPhase.Reconciling, LivenessState.Stalled)]
    [InlineData(RuntimeLivenessPhase.Cancelling, LivenessState.Stalled)]
    [InlineData(RuntimeLivenessPhase.Stalled, LivenessState.Stalled)]
    [InlineData(RuntimeLivenessPhase.Detached, LivenessState.Detached)]
    [InlineData(RuntimeLivenessPhase.Terminal, LivenessState.Terminal)]
    public void RuntimeLivenessPhase_LivenessState_Mapping(RuntimeLivenessPhase phase, LivenessState expected)
    {
        Assert.Equal(expected, phase.LivenessState());
    }

    // ── ToString overrides ──────────────────────────────────────────────────

    [Theory]
    [InlineData(RuntimeLivenessPhase.WaitingOnPriorTurn, "waiting_on_prior_turn")]
    [InlineData(RuntimeLivenessPhase.RunningTurn, "running_turn")]
    [InlineData(RuntimeLivenessPhase.Terminal, "terminal")]
    public void RuntimeLivenessPhase_ToString_SnakeCase(RuntimeLivenessPhase phase, string expected)
    {
        Assert.Equal(expected, phase.ToSnakeCaseString());
    }

    [Theory]
    [InlineData(LivenessState.Active, "active")]
    [InlineData(LivenessState.Stalled, "stalled")]
    public void LivenessState_ToString_SnakeCase(LivenessState state, string expected)
    {
        Assert.Equal(expected, state.ToSnakeCaseString());
    }

    [Theory]
    [InlineData(StreamHealth.Unknown, "unknown")]
    [InlineData(StreamHealth.HistorySyncing, "history_syncing")]
    public void StreamHealth_ToString_SnakeCase(StreamHealth health, string expected)
    {
        Assert.Equal(expected, health.ToSnakeCaseString());
    }

    [Theory]
    [InlineData(HistorySyncStatus.Idle, "idle")]
    [InlineData(HistorySyncStatus.InProgress, "in_progress")]
    public void HistorySyncStatus_ToString_SnakeCase(HistorySyncStatus status, string expected)
    {
        Assert.Equal(expected, status.ToSnakeCaseString());
    }

    [Theory]
    [InlineData(ReconnectStatus.Connected, "connected")]
    [InlineData(ReconnectStatus.Exhausted, "exhausted")]
    public void ReconnectStatus_ToString_SnakeCase(ReconnectStatus status, string expected)
    {
        Assert.Equal(expected, status.ToSnakeCaseString());
    }

    // ── Additional JSON round-trip parity ────────────────────────────────────

    [Fact]
    public void RetryEntry_Json_RoundTrip()
    {
        var entry = RetryEntry.Continuation(sampleIssue(), null, 0, ts(100), RetryPolicy.Default).Value;
        var json = JsonSerializer.Serialize(entry, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<RetryEntry>(json, DomainJsonOptions.Default);
        Assert.Equal(entry, deserialized);
    }

    [Fact]
    public void WorkspaceRecord_Json_RoundTrip()
    {
        var record = new WorkspaceRecord("/path/to/ws", WorkspaceKey.New("key_1").Value, true, ts(100), ts(200), null);
        var json = JsonSerializer.Serialize(record, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<WorkspaceRecord>(json, DomainJsonOptions.Default);
        Assert.Equal(record, deserialized);
    }

    [Fact]
    public void DetachMetadata_Json_RoundTrip()
    {
        var meta = new DetachMetadata(DetachReason.CancelFailed, ts(500), "running", "cancel failed");
        var json = JsonSerializer.Serialize(meta, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<DetachMetadata>(json, DomainJsonOptions.Default);
        Assert.Equal(meta, deserialized);
    }

    [Fact]
    public void RetryPolicy_Json_RoundTrip()
    {
        var policy = RetryPolicy.Default;
        var json = JsonSerializer.Serialize(policy, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<RetryPolicy>(json, DomainJsonOptions.Default);
        Assert.Equal(policy, deserialized);
    }

    [Fact]
    public void ConversationActivityEvent_Json_OmitsPayloadWhenNull()
    {
        var evt = new ConversationActivityEvent("e1", ts(100), "kind", "summary", null, 0);
        var json = JsonSerializer.Serialize(evt, DomainJsonOptions.Default);
        Assert.DoesNotContain("payload", json);
        Assert.Contains("sequence", json);
    }

    [Fact]
    public void ConversationActivityEvent_Json_RoundTrip_WithPayload()
    {
        var payload = JsonDocument.Parse("{\"key\":\"value\"}").RootElement.Clone();
        var evt = new ConversationActivityEvent("e1", ts(100), "kind", "summary", payload, 5);
        var json = JsonSerializer.Serialize(evt, DomainJsonOptions.Default);
        var deserialized = JsonSerializer.Deserialize<ConversationActivityEvent>(json, DomainJsonOptions.Default);
        Assert.Equal("e1", deserialized.EventId);
        Assert.Equal(5UL, deserialized.Sequence);
        Assert.NotNull(deserialized.Payload);
    }

    [Fact]
    public void ConversationActivityEvent_Json_SequenceDefaultsToZeroWhenAbsent()
    {
        var json = "{\"event_id\":\"e1\",\"happened_at\":100,\"kind\":\"kind\",\"summary\":\"summary\"}";
        var evt = JsonSerializer.Deserialize<ConversationActivityEvent>(json, DomainJsonOptions.Default);
        Assert.Equal(0UL, evt.Sequence);
    }

    [Fact]
    public void ConversationMetadata_AddRuntimeSeconds()
    {
        var conv = sampleConversation();
        conv.AddRuntimeSeconds(30);
        conv.AddRuntimeSeconds(10);
        Assert.Equal(40UL, conv.RuntimeSeconds);
    }

    [Fact]
    public void ConversationMetadata_Json_NumericFieldsAlwaysPresent()
    {
        var conv = sampleConversation();
        var json = JsonSerializer.Serialize(conv, DomainJsonOptions.Default);
        Assert.Contains("\"input_tokens\":0", json);
        Assert.Contains("\"output_tokens\":0", json);
        Assert.Contains("\"cache_read_tokens\":0", json);
        Assert.Contains("\"total_tokens\":0", json);
        Assert.Contains("\"runtime_seconds\":0", json);
        Assert.Contains("\"next_activity_sequence\":0", json);
    }

    [Fact]
    public void ConversationMetadata_Json_DefaultsNumericZeroWhenAbsent()
    {
        var json = "{\"conversation_id\":\"conv_1\",\"fresh_conversation\":true,\"stream_state\":\"ready\"}";
        var conv = JsonSerializer.Deserialize<ConversationMetadata>(json, DomainJsonOptions.Default);
        Assert.Equal(0UL, conv.InputTokens);
        Assert.Equal(0UL, conv.NextActivitySequence);
        Assert.Empty(conv.RecentActivity);
    }
}
