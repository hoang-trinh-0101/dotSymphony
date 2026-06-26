using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands models.rs.
//   JSON serialization matches the Rust serde contract (snake_case, skip_null).

public static class OpenHandsConstants
{
    public const string LLM_SUMMARIZING_CONDENSER_KIND = "LLMSummarizingCondenser";
    internal const string LLM_SUMMARIZING_CONDENSER_USAGE_ID = "condenser";
}

public sealed record WorkspaceConfig(string WorkingDir, string Kind);

public sealed record ConfirmationPolicy(string Kind);

public sealed class LlmConfig
{
    public string Model { get; init; } = "";
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? UsageId { get; init; }
    public Dictionary<string, string>? ExtraHeaders { get; init; }
    public Dictionary<string, JsonElement>? LitellmExtraBody { get; init; }
    public bool? Stream { get; init; }

    public LlmConfig WithUsageId(string usageId) => new()
    {
        Model = Model,
        ApiKey = ApiKey,
        BaseUrl = BaseUrl,
        UsageId = usageId,
        ExtraHeaders = ExtraHeaders,
        LitellmExtraBody = LitellmExtraBody,
        Stream = Stream,
    };

    // ht: redact sensitive fields in ToString for debug safety.
    public override string ToString() =>
        $"LlmConfig(model={Model}, api_key={(ApiKey is null ? "null" : "<redacted>")}, base_url={BaseUrl})";
}

public sealed record CondenserConfig(string Kind, LlmConfig Llm, ulong MaxSize, ulong KeepFirst)
{
    public static CondenserConfig LlmSummarizing(LlmConfig llm, ulong maxSize, ulong keepFirst) =>
        new(OpenHandsConstants.LLM_SUMMARIZING_CONDENSER_KIND, llm.WithUsageId(OpenHandsConstants.LLM_SUMMARIZING_CONDENSER_USAGE_ID), maxSize, keepFirst);
}

public sealed record ToolConfig(string Name, Dictionary<string, JsonElement> Params);

public sealed record AgentConfig(
    string Kind,
    LlmConfig Llm,
    CondenserConfig? Condenser = null,
    List<ToolConfig>? Tools = null,
    List<string>? IncludeDefaultTools = null);

public sealed record ConversationCreateRequest(
    Guid ConversationId,
    WorkspaceConfig Workspace,
    string PersistenceDir,
    uint MaxIterations,
    bool StuckDetection,
    ConfirmationPolicy ConfirmationPolicy,
    AgentConfig Agent)
{
    public static ConversationCreateRequest DoctorProbe(
        string workingDir, string persistenceDir, string? model, string? apiKey) =>
        DoctorProbeWithConfig(workingDir, persistenceDir, new DoctorProbeConfig { Model = model, ApiKey = apiKey });

    public static ConversationCreateRequest DoctorProbeWithConfig(
        string workingDir, string persistenceDir, DoctorProbeConfig config)
    {
        var model = config.Model ?? "openai/gpt-5.4";
        return new ConversationCreateRequest(
            Guid.NewGuid(),
            new WorkspaceConfig(workingDir, "LocalWorkspace"),
            persistenceDir,
            config.MaxIterations,
            config.StuckDetection,
            new ConfirmationPolicy(config.ConfirmationPolicyKind),
            new AgentConfig(
                config.AgentKind,
                new LlmConfig { Model = model, ApiKey = config.ApiKey, BaseUrl = config.BaseUrl }));
    }
}

public sealed record DoctorProbeConfig
{
    public uint MaxIterations { get; init; } = 4;
    public bool StuckDetection { get; init; } = true;
    public string ConfirmationPolicyKind { get; init; } = "NeverConfirm";
    public string AgentKind { get; init; } = "Agent";
    public string? Model { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
}

public sealed record Conversation(
    [property: JsonPropertyName("conversation_id")] Guid ConversationId,
    WorkspaceConfig Workspace,
    string PersistenceDir,
    uint MaxIterations,
    bool StuckDetection,
    string ExecutionStatus,
    ConfirmationPolicy ConfirmationPolicy,
    AgentConfig Agent,
    JsonElement? Stats = null);

public sealed record TextContent
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    public string Text { get; init; } = "";
    public bool CachePrompt { get; init; }

    public static TextContent FromText(string value) => new() { Type = "text", Text = value, CachePrompt = false };
}

public sealed record SendMessageRequest(string Role, List<TextContent> Content, bool Run = false)
{
    public static SendMessageRequest UserText(string value) =>
        new("user", [TextContent.FromText(value)], false);
}

public sealed record ConversationRunRequest;

public sealed record AcceptedResponse
{
    public bool Success { get; init; } = true;
    public static AcceptedResponse Accepted() => new() { Success = true };
}

public sealed record ConversationStateUpdatePayload
{
    public string? ExecutionStatus { get; init; }
    public JsonElement StateDelta { get; init; } = default;
}

// ht: EventEnvelope uses custom serialization to flatten payload into the top-level object.
//   Matches the Rust Serialize/Deserialize impl that spreads payload fields alongside id/timestamp/source/kind.
public sealed class EventEnvelope
{
    public string Id { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public string Source { get; init; } = "";
    public string Kind { get; init; } = "";
    public JsonElement Payload { get; init; } = default;
    public string? Key { get; init; }
    public JsonElement? Value { get; init; }

    public EventEnvelope() { }

    public EventEnvelope(string id, DateTimeOffset timestamp, string source, string kind, JsonElement payload)
    {
        Id = id; Timestamp = timestamp; Source = source; Kind = kind; Payload = payload;
    }

    public static EventEnvelope StateUpdate(string id, string executionStatus)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            execution_status = executionStatus,
            state_delta = new { execution_status = executionStatus },
        });
        return new EventEnvelope
        {
            Id = id,
            Timestamp = DateTimeOffset.UtcNow,
            Source = "runtime",
            Kind = "ConversationStateUpdateEvent",
            Payload = payload,
        };
    }

    public string Serialize()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["timestamp"] = Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
            ["source"] = Source,
            ["kind"] = Kind,
        };
        if (Key is not null) dict["key"] = Key;
        if (Value is not null) dict["value"] = Value.Value;

        if (Payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in Payload.EnumerateObject())
                dict[prop.Name] = prop.Value.Deserialize<JsonElement>();
        }
        else if (Payload.ValueKind != JsonValueKind.Null && Payload.ValueKind != JsonValueKind.Undefined)
        {
            dict["payload"] = Payload;
        }

        return JsonSerializer.Serialize(dict, OpenHandsJsonOptions.Default);
    }

    public static EventEnvelope Deserialize(JsonElement element)
    {
        var raw = new Dictionary<string, JsonElement>();
        foreach (var prop in element.EnumerateObject())
            raw[prop.Name] = prop.Value;

        string id = raw.TryGetValue("id", out var idVal) && idVal.ValueKind == JsonValueKind.String
            ? idVal.GetString()! : throw new JsonException("missing required field `id`");

        DateTimeOffset timestamp = ParseTimestamp(
            raw.TryGetValue("timestamp", out var tsVal)
                ? tsVal : throw new JsonException("missing required field `timestamp`"));

        string source = raw.TryGetValue("source", out var srcVal) && srcVal.ValueKind == JsonValueKind.String
            ? srcVal.GetString()! : "";

        string kind = raw.TryGetValue("kind", out var kindVal) && kindVal.ValueKind == JsonValueKind.String
            ? kindVal.GetString()! : throw new JsonException("missing required field `kind`");

        string? key = raw.TryGetValue("key", out var keyVal) && keyVal.ValueKind == JsonValueKind.String
            ? keyVal.GetString() : null;

        JsonElement? value = raw.TryGetValue("value", out var valueVal) ? valueVal : null;

        raw.Remove("id"); raw.Remove("timestamp"); raw.Remove("source"); raw.Remove("kind");
        raw.Remove("key"); raw.Remove("value");

        var nestedPayload = raw.TryGetValue("payload", out var npVal) ? npVal : (JsonElement?)null;
        raw.Remove("payload");

        JsonElement payload;
        if (nestedPayload is { } np && np.ValueKind == JsonValueKind.Object && raw.Count == 0)
            payload = np;
        else if (nestedPayload is { } np2 && (np2.ValueKind == JsonValueKind.Null) && raw.Count == 0)
            payload = JsonSerializer.SerializeToElement(raw);
        else if (nestedPayload is { } np3 && raw.Count == 0)
            payload = np3;
        else if (nestedPayload is { } np4)
        {
            raw["payload"] = np4;
            payload = JsonSerializer.SerializeToElement(raw);
        }
        else if (raw.Count == 0)
            payload = JsonSerializer.SerializeToElement(raw);
        else
            payload = JsonSerializer.SerializeToElement(raw);

        return new EventEnvelope
        {
            Id = id, Timestamp = timestamp, Source = source, Kind = kind,
            Payload = payload, Key = key, Value = value,
        };
    }

    private static DateTimeOffset ParseTimestamp(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException("expected `timestamp` to be a string timestamp");
        var raw = value.GetString()!;
        if (DateTimeOffset.TryParse(raw, out var parsed))
            return parsed;
        // ht: fallback for naive UTC timestamps without timezone suffix
        if (DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var naive))
            return new DateTimeOffset(naive, TimeSpan.Zero);
        throw new JsonException($"cannot parse timestamp `{raw}`");
    }
}

public sealed record SearchConversationEventsResponse
{
    [JsonPropertyName("events")]
    [JsonPropertyAliasName("items")]
    public List<EventEnvelope> Events { get; init; } = [];
    public string? NextPageId { get; init; }
}

// ht: minimal JSON property alias attribute — System.Text.Json doesn't have [JsonAlias].
internal sealed class JsonPropertyAliasNameAttribute : Attribute
{
    public string Alias { get; }
    public JsonPropertyAliasNameAttribute(string alias) => Alias = alias;
}
