using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Domain;

// ht: Rust serde(transparent) → bare JSON number. STJ handles Nullable<T> null
//   at the property level, so the converter only runs for non-null tokens.
public sealed class DurationMsConverter : JsonConverter<DurationMs>
{
    public override DurationMs Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DurationMs.New(reader.GetUInt64());

    public override void Write(Utf8JsonWriter writer, DurationMs value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.AsU64());
}

public sealed class TimestampMsConverter : JsonConverter<TimestampMs>
{
    public override TimestampMs Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => TimestampMs.New(reader.GetUInt64());

    public override void Write(Utf8JsonWriter writer, TimestampMs value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.AsU64());
}

// ht: For Nullable<StringIdentifier<TTag>> STJ handles null at property level.
//   Non-null token → New() which throws on trimmed-empty (Rust serde would fail).
public sealed class StringIdentifierConverter<TTag> : JsonConverter<StringIdentifier<TTag>>
    where TTag : IStringIdentifierTag
{
    public override StringIdentifier<TTag> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()!;
        var result = StringIdentifier<TTag>.New(s);
        if (result.IsErr)
            throw new JsonException(result.Error.Message);
        return result.Value;
    }

    public override void Write(Utf8JsonWriter writer, StringIdentifier<TTag> value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

public sealed class WorkspaceKeyConverter : JsonConverter<WorkspaceKey>
{
    public override WorkspaceKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()!;
        var result = WorkspaceKey.New(s);
        if (result.IsErr)
            throw new JsonException(result.Error.Message);
        return result.Value;
    }

    public override void Write(Utf8JsonWriter writer, WorkspaceKey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

// ht: Rust #[serde(transparent)] → bare JSON number. Reject 0 on read (NonZeroU32).
public sealed class RetryAttemptConverter : JsonConverter<RetryAttempt>
{
    public override RetryAttempt Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetUInt32();
        if (value == 0) throw new JsonException("retry attempt must be greater than zero");
        return new RetryAttempt(value);
    }

    public override void Write(Utf8JsonWriter writer, RetryAttempt value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Get());
}

// ht: For Nullable<RetryAttempt> STJ handles null at property level.
public sealed class RuntimeProgressSnapshotConverter : JsonConverter<RuntimeProgressSnapshot>
{
    public override RuntimeProgressSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var snap = new RuntimeProgressSnapshot
        {
            Phase = root.GetProperty("phase").Deserialize<RuntimeLivenessPhase>(options),
            LivenessState = root.GetProperty("liveness_state").Deserialize<LivenessState>(options),
            EventCount = root.GetProperty("event_count").GetUInt64(),
            EventDelta = root.GetProperty("event_delta").GetUInt64(),
            InputTokens = root.GetProperty("input_tokens").GetUInt64(),
            InputTokenDelta = root.GetProperty("input_token_delta").GetUInt64(),
            OutputTokens = root.GetProperty("output_tokens").GetUInt64(),
            OutputTokenDelta = root.GetProperty("output_token_delta").GetUInt64(),
            CacheReadTokens = root.GetProperty("cache_read_tokens").GetUInt64(),
            CacheReadTokenDelta = root.GetProperty("cache_read_token_delta").GetUInt64(),
            ExecutionStatus = root.TryGetProperty("execution_status", out var es) && es.ValueKind != JsonValueKind.Null ? es.GetString() : null,
            StreamHealth = root.GetProperty("stream_health").Deserialize<StreamHealth>(options),
            HistorySyncStatus = root.GetProperty("history_sync_status").Deserialize<HistorySyncStatus>(options),
            ReconnectStatus = root.GetProperty("reconnect_status").Deserialize<ReconnectStatus>(options),
            LastActivityAt = root.TryGetProperty("last_activity_at", out var la) && la.ValueKind != JsonValueKind.Null ? TimestampMs.New(la.GetUInt64()) : null,
            StallDeadlineAt = root.TryGetProperty("stall_deadline_at", out var sd) && sd.ValueKind != JsonValueKind.Null ? TimestampMs.New(sd.GetUInt64()) : null,
            LastEventCursor = root.TryGetProperty("last_event_cursor", out var lc) && lc.ValueKind != JsonValueKind.Null ? lc.GetString() : null,
            LastEventKind = root.TryGetProperty("last_event_kind", out var lk) && lk.ValueKind != JsonValueKind.Null ? lk.GetString() : null,
            LastEventAt = root.TryGetProperty("last_event_at", out var le) && le.ValueKind != JsonValueKind.Null ? TimestampMs.New(le.GetUInt64()) : null,
        };
        // ht: skip_serializing_if = Option::is_none → omit detach_metadata when null.
        if (root.TryGetProperty("detach_metadata", out var dm) && dm.ValueKind != JsonValueKind.Null)
            snap.DetachMetadata = dm.Deserialize<DetachMetadata>(options);
        return snap;
    }

    public override void Write(Utf8JsonWriter writer, RuntimeProgressSnapshot value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("phase");
        JsonSerializer.Serialize(writer, value.Phase, options);
        writer.WritePropertyName("liveness_state");
        JsonSerializer.Serialize(writer, value.LivenessState, options);
        writer.WriteNumber("event_count", value.EventCount);
        writer.WriteNumber("event_delta", value.EventDelta);
        writer.WriteNumber("input_tokens", value.InputTokens);
        writer.WriteNumber("input_token_delta", value.InputTokenDelta);
        writer.WriteNumber("output_tokens", value.OutputTokens);
        writer.WriteNumber("output_token_delta", value.OutputTokenDelta);
        writer.WriteNumber("cache_read_tokens", value.CacheReadTokens);
        writer.WriteNumber("cache_read_token_delta", value.CacheReadTokenDelta);
        if (value.ExecutionStatus is not null) writer.WriteString("execution_status", value.ExecutionStatus); else writer.WriteNull("execution_status");
        writer.WritePropertyName("stream_health");
        JsonSerializer.Serialize(writer, value.StreamHealth, options);
        writer.WritePropertyName("history_sync_status");
        JsonSerializer.Serialize(writer, value.HistorySyncStatus, options);
        writer.WritePropertyName("reconnect_status");
        JsonSerializer.Serialize(writer, value.ReconnectStatus, options);
        if (value.LastActivityAt is TimestampMs la) writer.WriteNumber("last_activity_at", la.AsU64()); else writer.WriteNull("last_activity_at");
        if (value.StallDeadlineAt is TimestampMs sd) writer.WriteNumber("stall_deadline_at", sd.AsU64()); else writer.WriteNull("stall_deadline_at");
        if (value.LastEventCursor is not null) writer.WriteString("last_event_cursor", value.LastEventCursor); else writer.WriteNull("last_event_cursor");
        if (value.LastEventKind is not null) writer.WriteString("last_event_kind", value.LastEventKind); else writer.WriteNull("last_event_kind");
        if (value.LastEventAt is TimestampMs le) writer.WriteNumber("last_event_at", le.AsU64()); else writer.WriteNull("last_event_at");
        if (value.DetachMetadata is not null)
        {
            writer.WritePropertyName("detach_metadata");
            JsonSerializer.Serialize(writer, value.DetachMetadata, options);
        }
        writer.WriteEndObject();
    }
}

public sealed class ConversationMetadataConverter : JsonConverter<ConversationMetadata>
{
    public override ConversationMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var meta = new ConversationMetadata(
            StringIdentifier<ConversationId>.New(root.GetProperty("conversation_id").GetString()!).Value)
        {
            ServerBaseUrl = root.TryGetProperty("server_base_url", out var sb) && sb.ValueKind != JsonValueKind.Null ? sb.GetString() : null,
            TransportTarget = root.TryGetProperty("transport_target", out var tt) && tt.ValueKind != JsonValueKind.Null ? tt.GetString() : null,
            HttpAuthMode = root.TryGetProperty("http_auth_mode", out var ha) && ha.ValueKind != JsonValueKind.Null ? ha.GetString() : null,
            WebsocketAuthMode = root.TryGetProperty("websocket_auth_mode", out var wa) && wa.ValueKind != JsonValueKind.Null ? wa.GetString() : null,
            WebsocketQueryParamName = root.TryGetProperty("websocket_query_param_name", out var wq) && wq.ValueKind != JsonValueKind.Null ? wq.GetString() : null,
            FreshConversation = root.GetProperty("fresh_conversation").GetBoolean(),
            RuntimeContractVersion = root.TryGetProperty("runtime_contract_version", out var rc) && rc.ValueKind != JsonValueKind.Null ? rc.GetString() : null,
            StreamState = root.GetProperty("stream_state").Deserialize<RuntimeStreamState>(options),
            LastEventId = root.TryGetProperty("last_event_id", out var lei) && lei.ValueKind != JsonValueKind.Null ? lei.GetString() : null,
            LastEventKind = root.TryGetProperty("last_event_kind", out var lek) && lek.ValueKind != JsonValueKind.Null ? lek.GetString() : null,
            LastEventAt = root.TryGetProperty("last_event_at", out var lea) && lea.ValueKind != JsonValueKind.Null ? TimestampMs.New(lea.GetUInt64()) : null,
            LastEventSummary = root.TryGetProperty("last_event_summary", out var les) && les.ValueKind != JsonValueKind.Null ? les.GetString() : null,
            RecentActivity = root.TryGetProperty("recent_activity", out var ra) && ra.ValueKind == JsonValueKind.Array
                ? ra.Deserialize<List<ConversationActivityEvent>>(options) ?? new()
                : new(),
            InputTokens = root.TryGetProperty("input_tokens", out var it) ? it.GetUInt64() : 0,
            OutputTokens = root.TryGetProperty("output_tokens", out var ot) ? ot.GetUInt64() : 0,
            CacheReadTokens = root.TryGetProperty("cache_read_tokens", out var cr) ? cr.GetUInt64() : 0,
            TotalTokens = root.TryGetProperty("total_tokens", out var tot) ? tot.GetUInt64() : 0,
            RuntimeSeconds = root.TryGetProperty("runtime_seconds", out var rs) ? rs.GetUInt64() : 0,
            NextActivitySequence = root.TryGetProperty("next_activity_sequence", out var nas) ? nas.GetUInt64() : 0,
        };
        return meta;
    }

    public override void Write(Utf8JsonWriter writer, ConversationMetadata value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("conversation_id", value.ConversationId.Value);
        if (value.ServerBaseUrl is not null) writer.WriteString("server_base_url", value.ServerBaseUrl); else writer.WriteNull("server_base_url");
        // ht: skip_serializing_if = Option::is_none → omit these 4 when null.
        if (value.TransportTarget is not null) writer.WriteString("transport_target", value.TransportTarget);
        if (value.HttpAuthMode is not null) writer.WriteString("http_auth_mode", value.HttpAuthMode);
        if (value.WebsocketAuthMode is not null) writer.WriteString("websocket_auth_mode", value.WebsocketAuthMode);
        if (value.WebsocketQueryParamName is not null) writer.WriteString("websocket_query_param_name", value.WebsocketQueryParamName);
        writer.WriteBoolean("fresh_conversation", value.FreshConversation);
        if (value.RuntimeContractVersion is not null) writer.WriteString("runtime_contract_version", value.RuntimeContractVersion); else writer.WriteNull("runtime_contract_version");
        writer.WritePropertyName("stream_state");
        JsonSerializer.Serialize(writer, value.StreamState, options);
        if (value.LastEventId is not null) writer.WriteString("last_event_id", value.LastEventId); else writer.WriteNull("last_event_id");
        if (value.LastEventKind is not null) writer.WriteString("last_event_kind", value.LastEventKind); else writer.WriteNull("last_event_kind");
        if (value.LastEventAt is TimestampMs lea) writer.WriteNumber("last_event_at", lea.AsU64()); else writer.WriteNull("last_event_at");
        if (value.LastEventSummary is not null) writer.WriteString("last_event_summary", value.LastEventSummary); else writer.WriteNull("last_event_summary");
        // ht: skip_serializing_if = Vec::is_empty → omit recent_activity when empty.
        if (value.RecentActivity.Count > 0)
        {
            writer.WritePropertyName("recent_activity");
            JsonSerializer.Serialize(writer, value.RecentActivity, options);
        }
        // ht: #[serde(default)] → always serialize these numerics.
        writer.WriteNumber("input_tokens", value.InputTokens);
        writer.WriteNumber("output_tokens", value.OutputTokens);
        writer.WriteNumber("cache_read_tokens", value.CacheReadTokens);
        writer.WriteNumber("total_tokens", value.TotalTokens);
        writer.WriteNumber("runtime_seconds", value.RuntimeSeconds);
        writer.WriteNumber("next_activity_sequence", value.NextActivitySequence);
        writer.WriteEndObject();
    }
}

public sealed class ConversationActivityEventConverter : JsonConverter<ConversationActivityEvent>
{
    public override ConversationActivityEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var evt = new ConversationActivityEvent(
            root.GetProperty("event_id").GetString()!,
            TimestampMs.New(root.GetProperty("happened_at").GetUInt64()),
            root.GetProperty("kind").GetString()!,
            root.GetProperty("summary").GetString()!,
            root.TryGetProperty("payload", out var p) && p.ValueKind != JsonValueKind.Null
                ? p.Clone() : null,
            root.TryGetProperty("sequence", out var s) ? s.GetUInt64() : 0);
        return evt;
    }

    public override void Write(Utf8JsonWriter writer, ConversationActivityEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("event_id", value.EventId);
        writer.WriteNumber("happened_at", value.HappenedAt.AsU64());
        writer.WriteString("kind", value.Kind);
        writer.WriteString("summary", value.Summary);
        // ht: skip_serializing_if = Option::is_none → omit payload when null.
        if (value.Payload is not null)
        {
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, value.Payload.Value, options);
        }
        // ht: #[serde(default)] → always serialize sequence.
        writer.WriteNumber("sequence", value.Sequence);
        writer.WriteEndObject();
    }
}

public sealed class StallMetadataConverter : JsonConverter<StallMetadata>
{
    public override StallMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var startedAt = TimestampMs.New(root.GetProperty("started_at").GetUInt64());
        var lastActivityAt = TimestampMs.New(root.GetProperty("last_activity_at").GetUInt64());
        // ht: alias = "stall_timeout_ms" → read idle_timeout_ms OR stall_timeout_ms.
        DurationMs idleTimeout;
        if (root.TryGetProperty("idle_timeout_ms", out var ito))
            idleTimeout = DurationMs.New(ito.GetUInt64());
        else if (root.TryGetProperty("stall_timeout_ms", out var sto))
            idleTimeout = DurationMs.New(sto.GetUInt64());
        else
            throw new JsonException("missing idle_timeout_ms or alias stall_timeout_ms");
        DurationMs? cap = null;
        if (root.TryGetProperty("total_runtime_cap_ms", out var trc) && trc.ValueKind != JsonValueKind.Null)
            cap = DurationMs.New(trc.GetUInt64());
        var stalledAt = TimestampMs.New(root.GetProperty("stalled_at").GetUInt64());
        return new StallMetadata(startedAt, lastActivityAt, idleTimeout, cap, stalledAt);
    }

    public override void Write(Utf8JsonWriter writer, StallMetadata value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("started_at", value.StartedAt.AsU64());
        writer.WriteNumber("last_activity_at", value.LastActivityAt.AsU64());
        writer.WriteNumber("idle_timeout_ms", value.IdleTimeoutMs.AsU64());
        // ht: skip_serializing_if = Option::is_none → omit total_runtime_cap_ms when null.
        if (value.TotalRuntimeCapMs is { } cap)
            writer.WriteNumber("total_runtime_cap_ms", cap.AsU64());
        writer.WriteNumber("stalled_at", value.StalledAt.AsU64());
        writer.WriteEndObject();
    }
}

// ht: Rust #[serde(tag = "state", content = "details")] internally-tagged enum.
//   Writes {"state":"<snake>","details":{...}} and reads back the right subclass.
public sealed class SchedulerStateConverter : JsonConverter<SchedulerState>
{
    public override SchedulerState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var tag = root.GetProperty("state").GetString();
        var details = root.GetProperty("details");
        return tag switch
        {
            "unclaimed" => new SchedulerStateUnclaimed(TimestampMs.New(details.GetProperty("since").GetUInt64())),
            "claimed" => new SchedulerStateClaimed(details.GetProperty("run").Deserialize<RunAttempt>(options)!),
            "running" => new SchedulerStateRunning(
                details.GetProperty("run").Deserialize<RunAttempt>(options)!,
                details.GetProperty("stall").Deserialize<StallMetadata>(options)),
            "retry_queued" => new SchedulerStateRetryQueued(details.GetProperty("retry").Deserialize<RetryEntry>(options)!),
            "released" => new SchedulerStateReleased(
                TimestampMs.New(details.GetProperty("released_at").GetUInt64()),
                details.GetProperty("reason").Deserialize<ReleaseReason>(options)),
            _ => throw new JsonException($"unknown scheduler state tag: {tag}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, SchedulerState value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("state", value.Status.ToSnakeCaseString());
        writer.WritePropertyName("details");
        switch (value)
        {
            case SchedulerStateUnclaimed u:
                writer.WriteStartObject();
                writer.WriteNumber("since", u.Since.AsU64());
                writer.WriteEndObject();
                break;
            case SchedulerStateClaimed c:
                writer.WriteStartObject();
                writer.WritePropertyName("run");
                JsonSerializer.Serialize(writer, c.Run, options);
                writer.WriteEndObject();
                break;
            case SchedulerStateRunning r:
                writer.WriteStartObject();
                writer.WritePropertyName("run");
                JsonSerializer.Serialize(writer, r.Run, options);
                writer.WritePropertyName("stall");
                JsonSerializer.Serialize(writer, r.Stall, options);
                writer.WriteEndObject();
                break;
            case SchedulerStateRetryQueued q:
                writer.WriteStartObject();
                writer.WritePropertyName("retry");
                JsonSerializer.Serialize(writer, q.Retry, options);
                writer.WriteEndObject();
                break;
            case SchedulerStateReleased rel:
                writer.WriteStartObject();
                writer.WriteNumber("released_at", rel.ReleasedAt.AsU64());
                writer.WritePropertyName("reason");
                JsonSerializer.Serialize(writer, rel.Reason, options);
                writer.WriteEndObject();
                break;
            default:
                throw new JsonException($"unknown scheduler state type: {value.GetType()}");
        }
        writer.WriteEndObject();
    }
}

// ht: Centralized options mirroring Rust serde defaults:
//   - snake_case property naming (fields are already snake_case in Rust, but this
//     keeps C# PascalCase properties mapping to snake_case JSON).
//   - snake_case enum naming via JsonStringEnumConverter.
//   - DefaultIgnoreCondition = Never so Option=null fields serialize as null
//     (per-record attributes / custom converters handle the skip_serializing_if cases).
public static class DomainJsonOptions
{
    public static JsonSerializerOptions Default { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
                new DurationMsConverter(),
                new TimestampMsConverter(),
                new StringIdentifierConverter<ConversationId>(),
                new StringIdentifierConverter<IssueId>(),
                new StringIdentifierConverter<IssueIdentifier>(),
                new StringIdentifierConverter<TrackerStateId>(),
                new StringIdentifierConverter<WorkerId>(),
                new WorkspaceKeyConverter(),
                new NormalizedIssueConverter(),
                new RetryAttemptConverter(),
                new RuntimeProgressSnapshotConverter(),
                new ConversationMetadataConverter(),
                new ConversationActivityEventConverter(),
                new StallMetadataConverter(),
                new SchedulerStateConverter(),
            },
        };
        return options;
    }
}
