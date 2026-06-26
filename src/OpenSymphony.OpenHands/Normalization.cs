using System.Text.Json;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands normalization.rs.

public static class NormalizationConstants
{
    public const string UNKNOWN_RAW_REF_PREFIX = "raw://openhands/unknown";
}

public enum NormalizationError
{
    MissingHarnessId,
    MissingConversationId,
}

public sealed class NormalizationContext
{
    public string HarnessId { get; }
    public StringIdentifier<ConversationId> ConversationId { get; }
    public EntityRef? IssueEntity { get; private set; }
    public string? CorrelationId { get; private set; }
    public string SchemaVersion { get; private set; }

    private NormalizationContext(string harnessId, StringIdentifier<ConversationId> conversationId)
    {
        HarnessId = harnessId;
        ConversationId = conversationId;
        SchemaVersion = "v1";
    }

    public static Result<NormalizationContext, NormalizationError> New(
        string harnessId, StringIdentifier<ConversationId> conversationId)
    {
        if (string.IsNullOrEmpty(harnessId))
            return Result<NormalizationContext, NormalizationError>.Err(NormalizationError.MissingHarnessId);
        if (string.IsNullOrEmpty(conversationId.Value))
            return Result<NormalizationContext, NormalizationError>.Err(NormalizationError.MissingConversationId);
        return Result<NormalizationContext, NormalizationError>.Ok(new NormalizationContext(harnessId, conversationId));
    }

    public NormalizationContext WithIssueEntity(EntityRef entity) { IssueEntity = entity; return this; }
    public NormalizationContext WithCorrelationId(string id) { CorrelationId = id; return this; }
    public NormalizationContext WithSchemaVersion(string version) { SchemaVersion = version; return this; }

    internal EntityRef EntityRef() => new(GatewaySchema.EntityKind.Conversation, ConversationId.Value, null);
}

public sealed record NormalizedRecord(
    EventKind Kind,
    JsonElement Payload,
    string Summary,
    string? RawPayloadRef)
{
    internal NormalizedRecord WithRaw(JsonElement rawPayload)
    {
        var rawRef = RawPayloadRef ?? SyntheticRawRefForRaw();
        return this with { RawPayloadRef = rawRef };
    }

    private string SyntheticRawRefForRaw() =>
        $"{NormalizationConstants.UNKNOWN_RAW_REF_PREFIX}/synthetic/{Guid.NewGuid()}";
}

public sealed record NormalizedEvent(
    EventRecord Record,
    JsonElement RawPayload,
    string? RawPayloadRef);

public static class Normalization
{
    public static NormalizedEvent NormalizeEvent(EventEnvelope envelope, NormalizationContext context)
    {
        if (string.IsNullOrEmpty(envelope.Source))
        {
            var synthetic = new UnknownEvent(envelope.Kind, envelope.Payload, envelope.Key, envelope.Value);
            var record = NormalizeUnknown(envelope, synthetic);
            // Override summary for source_missing
            record = record with { Summary = $"source_missing envelope kind={envelope.Kind}" };
            return BuildNormalizedEvent(envelope, context, record);
        }

        var known = KnownEvent.FromEnvelope(envelope);
        var result = known switch
        {
            KnownEvent.ConversationStateUpdate _ => NormalizeStateUpdate(envelope),
            KnownEvent.Message msg => NormalizeMessage(envelope, msg.Payload),
            KnownEvent.Action action => NormalizeAction(envelope, action.Payload),
            KnownEvent.Observation obs => NormalizeObservation(envelope, obs.Payload),
            KnownEvent.LlmCompletionLog llm => NormalizeLlmCompletion(envelope, llm.Event),
            KnownEvent.ConversationError _ => NormalizeConversationError(envelope),
            KnownEvent.Unknown unknown => NormalizeUnknown(envelope, unknown.Event),
            _ => NormalizeUnknown(envelope, new UnknownEvent(envelope.Kind, envelope.Payload, envelope.Key, envelope.Value)),
        };
        return BuildNormalizedEvent(envelope, context, result);
    }

    public static NormalizedRecord NormalizeStateUpdate(EventEnvelope envelope)
    {
        var summary = SummarizeStateUpdate(envelope.Payload);
        return new NormalizedRecord(EventKind.HarnessConversationStateUpdate, envelope.Payload, summary, null);
    }

    public static NormalizedRecord NormalizeMessage(EventEnvelope envelope, MessageEventPayload payload)
    {
        JsonElement? content = null;
        if (envelope.Payload.TryGetProperty("llm_message", out var msg) && msg.TryGetProperty("content", out var mc))
            content = mc;
        else if (envelope.Payload.TryGetProperty("content", out var c))
            content = c;

        var payloadJson = JsonSerializer.SerializeToElement(new
        {
            role = payload.Role,
            preview = payload.TextPreview,
            content = content ?? default(JsonElement),
        });
        var summary = payload.TextPreview ?? $"[{payload.Role}]";
        return new NormalizedRecord(EventKind.HarnessEventNormalized(envelope.Kind), payloadJson, summary, null);
    }

    public static NormalizedRecord NormalizeAction(EventEnvelope envelope, ActionEventPayload payload)
    {
        var body = new Dictionary<string, JsonElement>
        {
            ["action_id"] = JsonSerializer.SerializeToElement(payload.ActionId),
            ["tool_name"] = payload.ToolName is not null
                ? JsonSerializer.SerializeToElement(payload.ToolName)
                : JsonSerializer.SerializeToElement((string?)null),
            ["message"] = payload.Message is not null
                ? JsonSerializer.SerializeToElement(payload.Message)
                : JsonSerializer.SerializeToElement((string?)null),
        };
        if (payload.Arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in payload.Arguments.EnumerateObject())
            {
                if (!body.ContainsKey(prop.Name))
                    body[prop.Name] = prop.Value;
            }
        }
        var summary = payload.Message ?? payload.ToolName ?? "tool call";
        return new NormalizedRecord(EventKind.HarnessToolCall, JsonSerializer.SerializeToElement(body), summary, null);
    }

    public static NormalizedRecord NormalizeObservation(EventEnvelope envelope, ObservationEventPayload payload)
    {
        JsonElement content = default;
        if (envelope.Payload.TryGetProperty("observation", out var obs) && obs.TryGetProperty("content", out var c))
            content = c;

        var payloadJson = JsonSerializer.SerializeToElement(new
        {
            observation_id = payload.ObservationId,
            tool_name = payload.ToolName,
            exit_code = payload.ExitCode,
            preview = payload.TextPreview,
            content = content,
        });
        var summary = payload.TextPreview ?? payload.ToolName ?? "tool result";
        return new NormalizedRecord(EventKind.HarnessToolResult, payloadJson, summary, null);
    }

    public static NormalizedRecord NormalizeLlmCompletion(EventEnvelope envelope, LlmCompletionLogEvent payload)
    {
        var usage = payload.TokenUsage() is { } tu
            ? JsonSerializer.SerializeToElement(new { prompt = tu.Input, completion = tu.Output })
            : JsonSerializer.SerializeToElement((object?)null);
        var usageId = payload.Model() ?? (envelope.Payload.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() : null);

        var payloadJson = JsonSerializer.SerializeToElement(new { model = usageId, usage = usage });
        var summary = usageId is not null ? $"llm completion ({usageId})" : "llm completion";
        return new NormalizedRecord(EventKind.HarnessEventNormalized(envelope.Kind), payloadJson, summary, null);
    }

    public static NormalizedRecord NormalizeConversationError(EventEnvelope envelope)
    {
        var message = envelope.Payload.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : envelope.Payload.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() : "conversation error";
        return new NormalizedRecord(
            EventKind.HarnessEventNormalized(envelope.Kind),
            envelope.Payload,
            $"conversation error: {message}", null);
    }

    public static NormalizedRecord NormalizeUnknown(EventEnvelope envelope, UnknownEvent _unknown)
    {
        var rawPayload = UnknownPayload(envelope);
        var rawPayloadRef = SyntheticRawRef(envelope);
        var payload = envelope.Key is not null
            ? JsonSerializer.SerializeToElement(new
            {
                key = envelope.Key,
                value = envelope.Value ?? default(JsonElement),
            })
            : JsonSerializer.SerializeToElement((object?)null);

        var record = new NormalizedRecord(
            EventKind.Unknown(envelope.Kind),
            payload,
            $"unknown openhands event: {envelope.Kind}",
            rawPayloadRef);
        return record.WithRaw(rawPayload);
    }

    private static JsonElement UnknownPayload(EventEnvelope envelope)
    {
        if (envelope.Payload.ValueKind != JsonValueKind.Null)
            return envelope.Payload;
        if (envelope.Key is not null && envelope.Value is not null)
            return JsonSerializer.SerializeToElement(new { key = envelope.Key, value = envelope.Value });
        return JsonSerializer.SerializeToElement((object?)null);
    }

    private static string SyntheticRawRef(EventEnvelope envelope) =>
        $"{NormalizationConstants.UNKNOWN_RAW_REF_PREFIX}/{envelope.Source}/{envelope.Kind}/{envelope.Id}";

    private static NormalizedEvent BuildNormalizedEvent(
        EventEnvelope envelope, NormalizationContext context, NormalizedRecord record)
    {
        var actor = DeriveActor(envelope, context);
        var entityRef = context.EntityRef();

        var entityRefs = new List<EntityRef> { entityRef };
        if (context.IssueEntity is { } issue)
            entityRefs.Add(issue);

        var rawPayloadRef = record.RawPayloadRef;

        var builder = new EventRecordBuilder()
            .EventId(string.IsNullOrEmpty(envelope.Id) ? Guid.NewGuid().ToString() : envelope.Id)
            .Actor(actor)
            .Kind(record.Kind)
            .HappenedAt(envelope.Timestamp)
            .Summary(record.Summary)
            .EntityRefs(entityRefs);

        if (context.CorrelationId is { } corr)
            builder.CorrelationId(corr);
        if (rawPayloadRef is { } rawRef)
            builder.RawPayloadRef(rawRef);
        builder.Payload(record.Payload);

        var rawPayload = RawPayloadIfUnknown(envelope, record);
        return new NormalizedEvent(builder.Build(), rawPayload, rawPayloadRef);
    }

    private static JsonElement RawPayloadIfUnknown(EventEnvelope envelope, NormalizedRecord record)
    {
        if (record.Kind.IsUnknown || record.RawPayloadRef is not null)
            return UnknownPayload(envelope);
        return envelope.Payload;
    }

    private static EventActor DeriveActor(EventEnvelope envelope, NormalizationContext context) =>
        envelope.Source switch
        {
            "user" => EventActor.User(envelope.Source),
            "agent" or "assistant" => EventActor.Agent(envelope.Source),
            "llm" => EventActor.Agent(envelope.Source),
            "runtime" or "system" => EventActor.System(envelope.Source),
            _ => EventActor.Harness(context.HarnessId),
        };

    private static string SummarizeStateUpdate(JsonElement payload)
    {
        string? status = null;
        if (payload.TryGetProperty("execution_status", out var es) && es.ValueKind == JsonValueKind.String)
            status = es.GetString();
        else if (payload.TryGetProperty("state_delta", out var sd) &&
                 sd.TryGetProperty("execution_status", out var sd2) && sd2.ValueKind == JsonValueKind.String)
            status = sd2.GetString();

        return status is not null ? $"runtime status: {status}" : "runtime state update";
    }
}
