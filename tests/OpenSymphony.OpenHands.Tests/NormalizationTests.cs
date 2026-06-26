using System.Text.Json;
using OpenSymphony.OpenHands;
using OpenSymphony.GatewaySchema;
using OpenSymphony.Domain;

namespace OpenSymphony.OpenHands.Tests;

public class NormalizationTests
{
    private static NormalizationContext Context()
    {
        var convId = StringIdentifier<ConversationId>.New("conv-123");
        return NormalizationContext.New("openhands-agent-server-v1", convId.Value).Value;
    }

    private static NormalizationContext ContextWithCorrelation()
    {
        var convId = StringIdentifier<ConversationId>.New("conv-123");
        return NormalizationContext.New("openhands-agent-server-v1", convId.Value).Value
            .WithCorrelationId("corr-100");
    }

    [Fact]
    public void Context_RejectsEmptyFields()
    {
        var convId = StringIdentifier<ConversationId>.New("x");
        Assert.True(NormalizationContext.New("", convId.Value).IsErr);
    }

    [Fact]
    public void StateUpdate_NormalizesWithStatusSummary()
    {
        var envelope = new EventEnvelope("evt-state", DateTimeOffset.UtcNow, "runtime",
            "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new
            {
                execution_status = "running",
                state_delta = new { execution_status = "running" },
            }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsHarnessConversationStateUpdate);
        Assert.Equal("runtime status: running", normalized.Record.Summary);
        Assert.Equal("evt-state", normalized.Record.EventId);
        Assert.Contains(normalized.Record.EntityRefs, r => r.Kind == EntityKind.Conversation && r.Id == "conv-123");
    }

    [Fact]
    public void MessageEvent_NormalizesWithRoleAndPreview()
    {
        var envelope = new EventEnvelope("evt-message", DateTimeOffset.UtcNow, "agent", "MessageEvent",
            JsonSerializer.SerializeToElement(new
            {
                role = "assistant",
                content = new[] { new { type = "text", text = "hello world" } },
            }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsHarnessEventNormalized);
        Assert.Equal("MessageEvent", normalized.Record.Kind.SourceKind);
        Assert.NotNull(normalized.Record.Payload);
        Assert.True(normalized.Record.Payload.Value.TryGetProperty("role", out _));
        Assert.True(normalized.Record.Payload.Value.TryGetProperty("preview", out _));
        Assert.StartsWith("hello", normalized.Record.Summary);
    }

    [Fact]
    public void ActionEvent_NormalizesToolCallWithArguments()
    {
        var envelope = new EventEnvelope("evt-action", DateTimeOffset.UtcNow, "runtime", "ActionEvent",
            JsonSerializer.SerializeToElement(new
            {
                action = new { tool_name = "terminal", message = "running ls", command = "ls -la" },
            }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsHarnessToolCall);
        var payload = normalized.Record.Payload!.Value;
        Assert.Equal("terminal", payload.GetProperty("tool_name").GetString());
        Assert.Equal("evt-action", payload.GetProperty("action_id").GetString());
        Assert.Contains("running ls", normalized.Record.Summary);
    }

    [Fact]
    public void ObservationEvent_NormalizesToolResultWithExitCode()
    {
        var envelope = new EventEnvelope("evt-observation", DateTimeOffset.UtcNow, "runtime", "ObservationEvent",
            JsonSerializer.SerializeToElement(new
            {
                observation = new
                {
                    tool_name = "terminal",
                    exit_code = 0,
                    content = new[] { new { type = "text", text = "ok" } },
                },
            }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsHarnessToolResult);
        var payload = normalized.Record.Payload!.Value;
        Assert.Equal(0, payload.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public void LlmCompletionLog_ExtractsTokenUsage()
    {
        var envelope = new EventEnvelope("evt-llm", DateTimeOffset.UtcNow, "llm", "LLMCompletionLogEvent",
            JsonSerializer.SerializeToElement(new
            {
                model = "openai/gpt-5.4",
                usage = new { prompt_tokens = 100, completion_tokens = 200 },
            }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        var payload = normalized.Record.Payload!.Value;
        var usage = payload.GetProperty("usage");
        Assert.Equal(100UL, usage.GetProperty("prompt").GetUInt64());
        Assert.Equal(200UL, usage.GetProperty("completion").GetUInt64());
    }

    [Fact]
    public void ConversationError_NormalizesWithSummary()
    {
        var envelope = new EventEnvelope("evt-error", DateTimeOffset.UtcNow, "runtime", "ConversationErrorEvent",
            JsonSerializer.SerializeToElement(new { message = "OOM in tool" }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.Contains("OOM in tool", normalized.Record.Summary);
        Assert.True(normalized.Record.Kind.IsHarnessEventNormalized);
    }

    [Fact]
    public void UnknownEvent_KeepsRawPayloadForDiagnostics()
    {
        var envelope = new EventEnvelope("evt-unknown", DateTimeOffset.UtcNow, "runtime", "FutureOpenHandsEvent",
            JsonSerializer.SerializeToElement(new { future = true, details = "raw" }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsUnknown);
        Assert.Equal("FutureOpenHandsEvent", normalized.Record.Kind.RawKind);
        Assert.NotNull(normalized.Record.RawPayloadRef);
        Assert.StartsWith(NormalizationConstants.UNKNOWN_RAW_REF_PREFIX, normalized.Record.RawPayloadRef!);
        Assert.Equal("{\"future\":true,\"details\":\"raw\"}", normalized.RawPayload.GetRawText());
    }

    [Fact]
    public void ForwardCompatibleKeyValue_DecodesIntoUnknown()
    {
        var envelope = new EventEnvelope("evt-key-value", DateTimeOffset.UtcNow, "runtime", "ForwardStateDelta", default)
        {
            Key = "unknown_key",
            Value = JsonSerializer.SerializeToElement(new { structured = true }),
        };
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsUnknown);
        Assert.Equal("ForwardStateDelta", normalized.Record.Kind.RawKind);
        Assert.StartsWith(NormalizationConstants.UNKNOWN_RAW_REF_PREFIX, normalized.Record.RawPayloadRef!);
    }

    [Fact]
    public void Normalization_NeverPanicsOnEmptyPayloadOrUnknownKinds()
    {
        var envelope = new EventEnvelope("evt-edge", DateTimeOffset.UtcNow.AddSeconds(-1), "user", "AnythingAtAll", default);
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        // Must produce a normalized event without throwing
        Assert.True(normalized.Record.Kind.IsUnknown || normalized.Record.Kind.IsHarnessEventNormalized);
    }

    [Fact]
    public void CorrelationId_PropagatesIntoNormalizedEnvelope()
    {
        var envelope = new EventEnvelope("evt-state", DateTimeOffset.UtcNow, "runtime", "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "running" }));
        var normalized = Normalization.NormalizeEvent(envelope, ContextWithCorrelation());
        Assert.Equal("corr-100", normalized.Record.CorrelationId);
    }

    [Fact]
    public void UserSource_RoutesToEventUserActor()
    {
        var envelope = new EventEnvelope("evt-msg", DateTimeOffset.UtcNow, "user", "MessageEvent",
            JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content = new[] { new { type = "text", text = "hi" } },
            }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.Equal("user", normalized.Record.Actor.KindLabel());
    }

    [Fact]
    public void RuntimeSource_RoutesToSystemActor()
    {
        var envelope = new EventEnvelope("evt-runtime", DateTimeOffset.UtcNow, "runtime", "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "running" }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.Equal("system", normalized.Record.Actor.KindLabel());
        Assert.Equal("runtime", normalized.Record.Actor.ActorId());
    }

    [Fact]
    public void OpenhandsSource_RoutesToHarnessActor()
    {
        var envelope = new EventEnvelope("evt-other", DateTimeOffset.UtcNow, "some-other-source", "PersistedEvent", default);
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.Equal("harness", normalized.Record.Actor.KindLabel());
        Assert.Equal("openhands-agent-server-v1", normalized.Record.Actor.ActorId());
    }

    [Fact]
    public void EmptySource_IsTotal_RoutesToUnknownWithHarnessActor()
    {
        var envelope = new EventEnvelope("evt-source-missing", DateTimeOffset.UtcNow, "", "Anything",
            JsonSerializer.SerializeToElement(new { lost = true }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsUnknown);
        Assert.Equal("Anything", normalized.Record.Kind.RawKind);
        Assert.Contains("source_missing envelope kind=Anything", normalized.Record.Summary);
        Assert.Equal("openhands-agent-server-v1", normalized.Record.Actor.ActorId());
    }

    [Fact]
    public void RawPayloadForKnownEvent_IsTheEnvelopePayload()
    {
        var envelope = new EventEnvelope("evt-state", DateTimeOffset.UtcNow, "runtime", "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "running" }));
        var normalized = Normalization.NormalizeEvent(envelope, Context());
        Assert.True(normalized.Record.Kind.IsHarnessConversationStateUpdate);
        Assert.NotEqual(JsonValueKind.Null, normalized.RawPayload.ValueKind);
    }
}
