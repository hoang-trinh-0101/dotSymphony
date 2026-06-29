using System.Text.Json;
using JVal = System.Text.Json.JsonValueKind;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands events.rs.

public sealed class LlmCompletionLogEvent
{
    public JsonElement Payload { get; init; }

    public (ulong Input, ulong Output)? TokenUsage()
    {
        // Try nested usage object (OpenAI format via LiteLLM)
        if (Payload.TryGetProperty("usage", out var usage))
        {
            var input = GetU64(usage, "prompt_tokens") ?? GetU64(usage, "input_tokens") ?? 0;
            var output = GetU64(usage, "completion_tokens") ?? GetU64(usage, "output_tokens") ?? 0;
            if (input > 0 || output > 0) return (input, output);
        }

        // Try flat fields
        var flatInput = GetU64(Payload, "input_tokens") ?? GetU64(Payload, "prompt_tokens") ?? 0;
        var flatOutput = GetU64(Payload, "output_tokens") ?? GetU64(Payload, "completion_tokens") ?? 0;
        if (flatInput > 0 || flatOutput > 0) return (flatInput, flatOutput);

        // Try total_tokens
        if (GetU64(Payload, "total_tokens") is { } total) return (0, total);
        if (GetU64(Payload, "tokens") is { } tokens) return (0, tokens);

        return null;
    }

    public string? Model() => Payload.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
        ? m.GetString() : null;

    private static ulong? GetU64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JVal.Number
            ? (ulong?)v.GetUInt64() : null;
}

public sealed record ConversationErrorEvent(JsonElement Payload);

public sealed record UnknownEvent(string Kind, JsonElement Payload, string? Key, JsonElement? Value);

public sealed record EventTextContent
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? ContentType { get; init; }
    public string? Text { get; init; }
}

public sealed record MessageEventPayload(string Role, List<EventTextContent> Content, string? TextPreview);

public sealed record ActionEventPayload(string ActionId, string? ToolName, string? Message, JsonElement Arguments);

public sealed record ObservationEventPayload(
    string ObservationId, string? ToolName, List<EventTextContent> Content,
    string? TextPreview, int? ExitCode);

public abstract record KnownEvent
{
    public sealed record ConversationStateUpdate(ConversationStateUpdatePayload Payload) : KnownEvent;
    public sealed record LlmCompletionLog(LlmCompletionLogEvent Event) : KnownEvent;
    public sealed record ConversationError(ConversationErrorEvent Event) : KnownEvent;
    public sealed record Message(MessageEventPayload Payload) : KnownEvent;
    public sealed record Action(ActionEventPayload Payload) : KnownEvent;
    public sealed record Observation(ObservationEventPayload Payload) : KnownEvent;
    public sealed record Unknown(UnknownEvent Event) : KnownEvent;

    public ActivitySummary? ActivitySummary() => this switch
    {
        Message msg => new(ActivityKind.Message,
            msg.Payload.TextPreview ?? FirstText(msg.Payload.Content) ?? "message", null),
        Action action => new(ActivityKind.ToolCall,
            action.Payload.Message ?? "action", action.Payload.ToolName),
        Observation obs => new(ActivityKind.ToolResult,
            obs.Payload.TextPreview ?? FirstText(obs.Payload.Content) ?? "result", obs.Payload.ToolName),
        ConversationStateUpdate su => su.Payload.ExecutionStatus is { } status
            ? new(ActivityKind.StateChange, $"status: {status}", null) : null,
        ConversationError err => err.Event.Payload.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? new(ActivityKind.Error, m.GetString()!, null) : null,
        LlmCompletionLog or Unknown => null,
        _ => null,
    };

    public static KnownEvent FromEnvelope(EventEnvelope envelope) => envelope.Kind switch
    {
        "ConversationStateUpdateEvent" => DecodeStateUpdate(envelope) is { } p
            ? new ConversationStateUpdate(p) : new Unknown(UnknownEventFrom(envelope)),
        "LLMCompletionLogEvent" => new LlmCompletionLog(new LlmCompletionLogEvent { Payload = envelope.Payload }),
        "ConversationErrorEvent" => new ConversationError(new ConversationErrorEvent(envelope.Payload)),
        "MessageEvent" => new Message(DecodeMessageEvent(envelope)),
        "ActionEvent" => DecodeActionEvent(envelope) is { } a
            ? new Action(a) : new Unknown(UnknownEventFrom(envelope)),
        "ObservationEvent" => DecodeObservationEvent(envelope) is { } o
            ? new Observation(o) : new Unknown(UnknownEventFrom(envelope)),
        _ => new Unknown(UnknownEventFrom(envelope)),
    };

    private static string? FirstText(List<EventTextContent> content) =>
        content.FirstOrDefault(c => c.Text is not null)?.Text;

    private static MessageEventPayload DecodeMessageEvent(EventEnvelope envelope)
    {
        var role = envelope.Payload.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()! : "unknown";

        List<EventTextContent> content = [];
        JsonElement? contentElement = null;
        if (envelope.Payload.TryGetProperty("llm_message", out var msg) && msg.TryGetProperty("content", out var mc))
            contentElement = mc;
        else if (envelope.Payload.TryGetProperty("content", out var c))
            contentElement = c;

        if (contentElement is { } ce)
            content = JsonSerializer.Deserialize<List<EventTextContent>>(ce, OpenHandsJsonOptions.Default) ?? [];

        var textPreview = content.FirstOrDefault(x => x.Text is not null)?.Text;
        return new MessageEventPayload(role, content, textPreview);
    }

    private static ActionEventPayload? DecodeActionEvent(EventEnvelope envelope)
    {
        if (!envelope.Payload.TryGetProperty("action", out var action)) return null;
        var toolName = action.TryGetProperty("tool_name", out var tn) && tn.ValueKind == JsonValueKind.String
            ? tn.GetString() : null;
        var message = action.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() : null;
        return new ActionEventPayload(envelope.Id, toolName, message, action);
    }

    private static ObservationEventPayload? DecodeObservationEvent(EventEnvelope envelope)
    {
        if (!envelope.Payload.TryGetProperty("observation", out var obs)) return null;
        var toolName = obs.TryGetProperty("tool_name", out var tn) && tn.ValueKind == JsonValueKind.String
            ? tn.GetString() : null;
        int? exitCode = obs.TryGetProperty("exit_code", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? (int?)ec.GetInt64() : null;

        List<EventTextContent> content = [];
        if (obs.TryGetProperty("content", out var c))
            content = JsonSerializer.Deserialize<List<EventTextContent>>(c, OpenHandsJsonOptions.Default) ?? [];

        var textPreview = content.FirstOrDefault(x => x.Text is not null)?.Text;
        return new ObservationEventPayload(envelope.Id, toolName, content, textPreview, exitCode);
    }

    private static UnknownEvent UnknownEventFrom(EventEnvelope envelope) =>
        new(envelope.Kind, envelope.Payload, envelope.Key, envelope.Value);

    internal static ConversationStateUpdatePayload? DecodeStateUpdate(EventEnvelope envelope)
    {
        if (envelope.Payload.ValueKind != JsonValueKind.Null &&
            !(envelope.Payload.ValueKind == JsonValueKind.Object && NoProperties(envelope.Payload)))
        {
            if (TryDecodeStateUpdatePayload(envelope.Payload, out var direct))
                return direct;
            if (DecodeForwardCompatibleStateUpdate(envelope.Payload) is { } fc)
                return fc;
        }

        if (envelope.Key is not { } key) return null;
        if (envelope.Value is not { } value) return null;

        return key switch
        {
            "full_state" => new ConversationStateUpdatePayload
            {
                ExecutionStatus = value.TryGetProperty("execution_status", out var es) && es.ValueKind == JsonValueKind.String
                    ? es.GetString() : null,
                StateDelta = value,
            },
            "execution_status" => new ConversationStateUpdatePayload
            {
                ExecutionStatus = value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                StateDelta = JsonSerializer.SerializeToElement(new { execution_status = value }),
            },
            _ => new ConversationStateUpdatePayload
            {
                ExecutionStatus = null,
                StateDelta = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { ["other"] = value }),
            },
        };
    }

    private static bool NoProperties(JsonElement element) =>
        element.ValueKind != JsonValueKind.Object || !element.EnumerateObject().Any();

    private static bool TryDecodeStateUpdatePayload(JsonElement payload, out ConversationStateUpdatePayload result)
    {
        try
        {
            result = JsonSerializer.Deserialize<ConversationStateUpdatePayload>(payload, OpenHandsJsonOptions.Default)!;
            return true;
        }
        catch { result = null!; return false; }
    }

    private static ConversationStateUpdatePayload? DecodeForwardCompatibleStateUpdate(JsonElement payload)
    {
        JsonElement? stateDelta = null;
        if (payload.TryGetProperty("state_delta", out var sd))
            stateDelta = sd;
        else if (payload.TryGetProperty("execution_status", out var es) && es.ValueKind == JsonValueKind.String)
            stateDelta = JsonSerializer.SerializeToElement(new { execution_status = es.GetString() });

        if (stateDelta is null) return null;

        string? executionStatus = null;
        if (payload.TryGetProperty("execution_status", out var es2) && es2.ValueKind == JsonValueKind.String)
            executionStatus = es2.GetString();
        else if (stateDelta.Value.TryGetProperty("execution_status", out var es3) && es3.ValueKind == JsonValueKind.String)
            executionStatus = es3.GetString();

        return new ConversationStateUpdatePayload { ExecutionStatus = executionStatus, StateDelta = stateDelta.Value };
    }
}

public enum ActivityKind
{
    StateChange,
    Message,
    ToolCall,
    ToolResult,
    Error,
}

public static class ActivityKindExtensions
{
    public static string AsStr(this ActivityKind kind) => kind switch
    {
        ActivityKind.StateChange => "state",
        ActivityKind.Message => "message",
        ActivityKind.ToolCall => "tool",
        ActivityKind.ToolResult => "result",
        ActivityKind.Error => "error",
        _ => kind.ToString(),
    };
}

public sealed record ActivitySummary(ActivityKind Kind, string Preview, string? ToolName);

// ht: EventCache — dedup by ID, sorted by (timestamp, id).
public sealed class EventCache
{
    private readonly List<EventEnvelope> _events = [];
    private readonly HashSet<string> _ids = [];

    public bool Insert(EventEnvelope envelope)
    {
        if (!_ids.Add(envelope.Id)) return false;
        var pos = _events.BinarySearch(envelope, EventComparer.Instance);
        if (pos < 0) pos = ~pos;
        _events.Insert(pos, envelope);
        return true;
    }

    public List<EventEnvelope> MergeNewEvents(IEnumerable<EventEnvelope> events)
    {
        var inserted = events.Where(Insert).ToList();
        inserted.Sort(EventComparer.Instance);
        return inserted;
    }

    public int Extend(IEnumerable<EventEnvelope> events) => MergeNewEvents(events).Count;

    public IReadOnlyList<EventEnvelope> Items => _events;

    private sealed class EventComparer : IComparer<EventEnvelope>
    {
        public static readonly EventComparer Instance = new();
        public int Compare(EventEnvelope? x, EventEnvelope? y)
        {
            int c = x!.Timestamp.CompareTo(y!.Timestamp);
            return c != 0 ? c : string.Compare(x.Id, y.Id, StringComparison.Ordinal);
        }
    }
}

public enum TerminalExecutionStatus
{
    Finished,
    Error,
    Stuck,
}

public sealed class ConversationStateMirror
{
    public string? ExecutionStatus { get; private set; }
    public JsonElement RawState { get; private set; } = JsonSerializer.SerializeToElement(new { });

    public void ApplyConversation(Conversation conversation)
    {
        RawState = JsonSerializer.SerializeToElement(new { });
        ApplyConversationExecutionStatus(conversation);
        if (conversation.Stats is { } stats)
        {
            var merged = MergeJson(RawState, JsonSerializer.SerializeToElement(new { stats }));
            RawState = merged;
        }
    }

    public void ApplyConversationExecutionStatus(Conversation conversation)
    {
        var status = conversation.ExecutionStatus;
        ExecutionStatus = status;
        var obj = RawState.ValueKind == JsonValueKind.Object
            ? RawState.Deserialize<Dictionary<string, JsonElement>>()!
            : [];
        obj["execution_status"] = JsonSerializer.SerializeToElement(status);
        RawState = JsonSerializer.SerializeToElement(obj);
    }

    public void RebuildFrom(Conversation conversation, IReadOnlyList<EventEnvelope> events)
    {
        ApplyConversation(conversation);
        foreach (var evt in events) ApplyEvent(evt);
    }

    public void ApplyEvent(EventEnvelope envelope)
    {
        if (KnownEvent.FromEnvelope(envelope) is KnownEvent.ConversationStateUpdate payload)
        {
            if (payload.Payload.ExecutionStatus is { } status)
                ExecutionStatus = status;
            RawState = MergeJson(RawState, payload.Payload.StateDelta);
        }
    }

    public void ApplyTokenCounts(ulong inputDelta, ulong outputDelta, ulong cacheReadDelta)
    {
        if (inputDelta == 0 && outputDelta == 0 && cacheReadDelta == 0) return;
        var (prevInput, prevOutput, prevCacheRead) = AccumulatedTokenUsage() ?? (0, 0, 0);
        var delta = JsonSerializer.SerializeToElement(new
        {
            stats = new
            {
                usage_to_metrics = new
                {
                    @default = new
                    {
                        accumulated_token_usage = new
                        {
                            prompt_tokens = prevInput + inputDelta,
                            completion_tokens = prevOutput + outputDelta,
                            cache_read_tokens = prevCacheRead + cacheReadDelta,
                        }
                    }
                }
            }
        });
        RawState = MergeJson(RawState, delta);
    }

    public TerminalExecutionStatus? TerminalStatus() => ExecutionStatus switch
    {
        "finished" => TerminalExecutionStatus.Finished,
        "error" => TerminalExecutionStatus.Error,
        "stuck" => TerminalExecutionStatus.Stuck,
        _ => null,
    };

    public (ulong Input, ulong Output, ulong CacheRead)? AccumulatedTokenUsage()
    {
        if (!RawState.TryGetProperty("stats", out var stats)) return null;
        if (!stats.TryGetProperty("usage_to_metrics", out var utm)) return null;
        if (!utm.TryGetProperty("default", out var def)) return null;
        if (!def.TryGetProperty("accumulated_token_usage", out var usage)) return null;

        var input = usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JVal.Number
            ? pt.GetUInt64() : 0;
        var output = usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JVal.Number
            ? ct.GetUInt64() : 0;
        var cacheRead = usage.TryGetProperty("cache_read_tokens", out var cr) && cr.ValueKind == JVal.Number
            ? cr.GetUInt64() : 0;

        return input > 0 || output > 0 || cacheRead > 0 ? (input, output, cacheRead) : null;
    }

    // ht: deep-merge two JSON objects. Non-object values replace.
    internal static JsonElement MergeJson(JsonElement target, JsonElement delta)
    {
        if (target.ValueKind == JsonValueKind.Object && delta.ValueKind == JsonValueKind.Object)
        {
            var dict = target.Deserialize<Dictionary<string, JsonElement>>()!;
            foreach (var prop in delta.EnumerateObject())
            {
                if (dict.TryGetValue(prop.Name, out var existing))
                    dict[prop.Name] = MergeJson(existing, prop.Value);
                else
                    dict[prop.Name] = prop.Value;
            }
            return JsonSerializer.SerializeToElement(dict);
        }
        return delta;
    }
}
