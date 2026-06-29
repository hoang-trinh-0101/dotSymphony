using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Domain;

// ── Control-plane snapshot types (ported from control_plane.rs) ────────────

public sealed record SnapshotEnvelope(
    [property: JsonPropertyName("sequence")] ulong Sequence,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("snapshot")] ControlPlaneDaemonSnapshot Snapshot);

public sealed record ControlPlaneDaemonSnapshot(
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("daemon")] ControlPlaneDaemonStatus Daemon,
    [property: JsonPropertyName("agent_server")] ControlPlaneAgentServerStatus AgentServer,
    [property: JsonPropertyName("memory_server")] ControlPlaneMemoryServerStatus MemoryServer,
    [property: JsonPropertyName("metrics")] ControlPlaneMetricsSnapshot Metrics,
    [property: JsonPropertyName("issues")] List<ControlPlaneIssueSnapshot> Issues,
    [property: JsonPropertyName("recent_events")] List<ControlPlaneRecentEvent> RecentEvents)
{
    public int IssueCount => Issues.Count;
}

public sealed record ControlPlaneDaemonStatus(
    [property: JsonPropertyName("state")] ControlPlaneDaemonState State,
    [property: JsonPropertyName("last_poll_at")] DateTimeOffset LastPollAt,
    [property: JsonPropertyName("workspace_root")] string WorkspaceRoot,
    [property: JsonPropertyName("status_line")] string StatusLine);

[JsonConverter(typeof(SnakeCaseEnumConverter<ControlPlaneDaemonState>))]
public enum ControlPlaneDaemonState
{
    Starting,
    Ready,
    Degraded,
    Stopped,
}

public sealed record ControlPlaneAgentServerStatus(
    [property: JsonPropertyName("reachable")] bool Reachable,
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("conversation_count")] uint ConversationCount,
    [property: JsonPropertyName("status_line")] string StatusLine);

public sealed record ControlPlaneMemoryServerStatus(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("reachable")] bool Reachable,
    [property: JsonPropertyName("endpoint")] string? Endpoint,
    [property: JsonPropertyName("status_line")] string StatusLine)
{
    public static ControlPlaneMemoryServerStatus Default => new(false, false, null, "disabled");
}

public sealed record ControlPlaneMetricsSnapshot(
    [property: JsonPropertyName("running_issues")] uint RunningIssues,
    [property: JsonPropertyName("retry_queue_depth")] uint RetryQueueDepth,
    [property: JsonPropertyName("input_tokens")] ulong InputTokens,
    [property: JsonPropertyName("output_tokens")] ulong OutputTokens,
    [property: JsonPropertyName("cache_read_tokens")] ulong CacheReadTokens,
    [property: JsonPropertyName("total_tokens")] ulong TotalTokens,
    [property: JsonPropertyName("total_cost_micros")] ulong TotalCostMicros);

public sealed record ControlPlaneIssueSnapshot(
    [property: JsonPropertyName("identifier")] string Identifier,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("tracker_state")] string TrackerState,
    [property: JsonPropertyName("runtime_state")] ControlPlaneIssueRuntimeState RuntimeState,
    [property: JsonPropertyName("last_outcome")] ControlPlaneWorkerOutcome LastOutcome,
    [property: JsonPropertyName("last_event_at")] DateTimeOffset LastEventAt,
    [property: JsonPropertyName("conversation_id_suffix")] string ConversationIdSuffix,
    [property: JsonPropertyName("workspace_path_suffix")] string WorkspacePathSuffix,
    [property: JsonPropertyName("retry_count")] uint RetryCount,
    [property: JsonPropertyName("claimed_at")] DateTimeOffset? ClaimedAt,
    [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("finished_at")] DateTimeOffset? FinishedAt,
    [property: JsonPropertyName("turn_count")] uint TurnCount,
    [property: JsonPropertyName("max_turns")] uint MaxTurns,
    [property: JsonPropertyName("runtime_seconds")] ulong RuntimeSeconds,
    [property: JsonPropertyName("blocked")] bool Blocked,
    [property: JsonPropertyName("blocked_by")] List<string> BlockedBy,
    [property: JsonPropertyName("server_base_url")] string? ServerBaseUrl,
    [property: JsonPropertyName("transport_target")] string? TransportTarget,
    [property: JsonPropertyName("http_auth_mode")] string? HttpAuthMode,
    [property: JsonPropertyName("websocket_auth_mode")] string? WebsocketAuthMode,
    [property: JsonPropertyName("websocket_query_param_name")] string? WebsocketQueryParamName,
    [property: JsonPropertyName("recent_events")] List<ControlPlaneConversationEvent> RecentEvents,
    [property: JsonPropertyName("modified_files")] List<ControlPlaneFileChange> ModifiedFiles,
    [property: JsonPropertyName("input_tokens")] ulong InputTokens,
    [property: JsonPropertyName("output_tokens")] ulong OutputTokens,
    [property: JsonPropertyName("cache_read_tokens")] ulong CacheReadTokens,
    [property: JsonPropertyName("total_tokens")] ulong TotalTokens,
    [property: JsonPropertyName("detached")] bool Detached,
    [property: JsonPropertyName("cancel_acknowledged")] bool CancelAcknowledged,
    [property: JsonPropertyName("cancel_failed")] bool CancelFailed);

public sealed record ControlPlaneConversationEvent(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("happened_at")] DateTimeOffset HappenedAt,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("sequence")] ulong Sequence);

public sealed record ControlPlaneFileChange(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("change_kind")] ControlPlaneFileChangeKind ChangeKind,
    [property: JsonPropertyName("lines_added")] uint LinesAdded,
    [property: JsonPropertyName("lines_removed")] uint LinesRemoved,
    [property: JsonPropertyName("diff")] string? Diff);

[JsonConverter(typeof(SnakeCaseEnumConverter<ControlPlaneFileChangeKind>))]
public enum ControlPlaneFileChangeKind
{
    Created,
    Modified,
    Removed,
}

[JsonConverter(typeof(SnakeCaseEnumConverter<ControlPlaneIssueRuntimeState>))]
public enum ControlPlaneIssueRuntimeState
{
    Idle,
    Running,
    Paused,
    RetryQueued,
    Releasing,
    Completed,
    Failed,
}

[JsonConverter(typeof(SnakeCaseEnumConverter<ControlPlaneWorkerOutcome>))]
public enum ControlPlaneWorkerOutcome
{
    Unknown,
    Running,
    Continued,
    Completed,
    Failed,
    Canceled,
}

public sealed record ControlPlaneRecentEvent(
    [property: JsonPropertyName("happened_at")] DateTimeOffset HappenedAt,
    [property: JsonPropertyName("issue_identifier")] string? IssueIdentifier,
    [property: JsonPropertyName("kind")] ControlPlaneRecentEventKind Kind,
    [property: JsonPropertyName("summary")] string Summary);

[JsonConverter(typeof(SnakeCaseEnumConverter<ControlPlaneRecentEventKind>))]
public enum ControlPlaneRecentEventKind
{
    WorkerStarted,
    WorkspacePrepared,
    StreamAttached,
    SnapshotPublished,
    WorkerCompleted,
    RetryScheduled,
    ClientAttached,
    ClientDetached,
    Warning,
}

// ht: minimal snake_case enum converter — avoids relying on JsonStringEnumConverter
//   naming policy support which varies by runtime version.
public sealed class SnakeCaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<string, T> ReadMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<T, string> WriteMap = new();

    static SnakeCaseEnumConverter()
    {
        foreach (var name in Enum.GetNames<T>())
        {
            var snake = ToSnakeCase(name);
            ReadMap[snake] = Enum.Parse<T>(name);
            WriteMap[Enum.Parse<T>(name)] = snake;
        }
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()!;
        if (ReadMap.TryGetValue(s, out var val)) return val;
        throw new JsonException($"unknown {typeof(T).Name} variant: {s}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(WriteMap[value]);

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
