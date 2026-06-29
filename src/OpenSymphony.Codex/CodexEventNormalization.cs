using System.Text.Json;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Codex;

public static class CodexEventNormalization
{
    public static NormalizedCodexEvent? NormalizeServerNotification(JsonElement raw)
    {
        var method = raw.TryGetProperty("method", out var m) ? m.GetString() : null;
        if (method == null)
            return null;

        var kind = ClassifyEventKind(method);
        var paramsElement = raw.TryGetProperty("params", out var p) ? p : default;

        return new NormalizedCodexEvent
        {
            Kind = kind,
            Method = method,
            ThreadId = ExtractStringParam(paramsElement, new[] { "threadId", "thread_id" }),
            TurnId = ExtractStringParam(paramsElement, new[] { "turnId", "turn_id" }),
            ItemId = ExtractStringParam(paramsElement, new[] { "itemId", "item_id", "approvalId", "approval_id" }),
            MessageDelta = ExtractStringParam(paramsElement, new[] { "delta", "text", "content", "message" }),
            TokenUsage = ExtractTokenUsage(paramsElement),
            Raw = raw
        };
    }

    private static NormalizedCodexEventKind ClassifyEventKind(string method)
    {
        return method.ToLowerInvariant() switch
        {
            "thread/start" or "thread/started" => NormalizedCodexEventKind.ThreadStarted,
            "thread/status" or "thread/status_changed" => NormalizedCodexEventKind.ThreadStatusChanged,
            "token/usage" or "token_usage_updated" => NormalizedCodexEventKind.TokenUsageUpdated,
            "turn/start" or "turn/started" => NormalizedCodexEventKind.TurnStarted,
            "turn/complete" or "turn/completed" => NormalizedCodexEventKind.TurnCompleted,
            "turn/cancel" or "turn/cancelled" => NormalizedCodexEventKind.TurnCancelled,
            "turn/diff" or "turn/diff_updated" => NormalizedCodexEventKind.TurnDiffUpdated,
            "item/start" or "item/started" => NormalizedCodexEventKind.ItemStarted,
            "item/complete" or "item/completed" => NormalizedCodexEventKind.ItemCompleted,
            "message/delta" or "agent_message_delta" => NormalizedCodexEventKind.AgentMessageDelta,
            "command/output" or "command_execution_output_delta" => NormalizedCodexEventKind.CommandExecutionOutputDelta,
            "plan/delta" or "plan_delta" => NormalizedCodexEventKind.PlanDelta,
            "approval/request" or "approval_requested" => NormalizedCodexEventKind.ApprovalRequested,
            "approval/complete" or "approval_completed" => NormalizedCodexEventKind.ApprovalCompleted,
            "error" => NormalizedCodexEventKind.Error,
            _ => NormalizedCodexEventKind.Unknown
        };
    }

    private static string? ExtractStringParam(JsonElement element, string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static CodexTokenUsage? ExtractTokenUsage(JsonElement element)
    {
        var usage = element.TryGetProperty("usage", out var u) ? u : element;
        var input = ExtractNumberParam(usage, new[] { "input_tokens", "inputTokens", "prompt_tokens" });
        var output = ExtractNumberParam(usage, new[] { "output_tokens", "outputTokens", "completion_tokens" });
        var cache = ExtractNumberParam(usage, new[] { "cache_read_tokens", "cacheReadTokens", "cached_input_tokens" }) ?? 0;
        var total = ExtractNumberParam(usage, new[] { "total_tokens", "totalTokens" });

        if (input == null && output == null && total == null)
            return null;

        return new CodexTokenUsage
        {
            InputTokens = input ?? 0,
            OutputTokens = output ?? 0,
            CacheReadTokens = cache,
            TotalTokens = total ?? ((input ?? 0) + (output ?? 0) + cache)
        };
    }

    private static ulong? ExtractNumberParam(JsonElement element, string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
                return value.GetUInt64();
        }
        return null;
    }

    public static string EventSummary(NormalizedCodexEvent @event)
    {
        return @event.Kind switch
        {
            NormalizedCodexEventKind.ThreadStarted => $"Codex thread started{IdSuffix(@event.ThreadId)}",
            NormalizedCodexEventKind.TurnStarted => $"Codex turn started{IdSuffix(@event.TurnId)}",
            NormalizedCodexEventKind.TurnCompleted => $"Codex turn completed{IdSuffix(@event.TurnId)}",
            NormalizedCodexEventKind.TurnCancelled => $"Codex turn cancelled{IdSuffix(@event.TurnId)}",
            NormalizedCodexEventKind.ApprovalRequested => $"Codex requested approval{IdSuffix(@event.ItemId)}",
            NormalizedCodexEventKind.ApprovalCompleted => $"Codex approval completed{IdSuffix(@event.ItemId)}",
            NormalizedCodexEventKind.Error => ErrorSummary(@event) ?? "Codex app-server reported an error",
            NormalizedCodexEventKind.AgentMessageDelta =>
                BoundedRedactedPreview(@event.MessageDelta)?.Map(p => $"Codex assistant: {p}")
                ?? $"Codex event: {@event.Method}",
            NormalizedCodexEventKind.CommandExecutionOutputDelta =>
                CommandOutputSummary(@event) ?? $"Codex event: {@event.Method}",
            NormalizedCodexEventKind.PlanDelta =>
                PlanSummary(@event) ?? $"Codex event: {@event.Method}",
            NormalizedCodexEventKind.TurnDiffUpdated =>
                DiffSummary(@event) ?? "Codex diff updated",
            NormalizedCodexEventKind.TokenUsageUpdated =>
                TokenUsageSummary(@event) ?? $"Codex event: {@event.Method}",
            NormalizedCodexEventKind.ThreadStatusChanged =>
                ThreadStatusSummary(@event) ?? $"Codex event: {@event.Method}",
            NormalizedCodexEventKind.ItemStarted or NormalizedCodexEventKind.ItemCompleted =>
                ItemSummary(@event) ?? $"Codex event: {@event.Method}",
            NormalizedCodexEventKind.Unknown => $"Codex event: {@event.Method}",
            _ => $"Codex event: {@event.Method}"
        };
    }

    private static string IdSuffix(string? id) => id != null ? $" {id}" : "";

    private static string? ErrorSummary(NormalizedCodexEvent @event)
    {
        var message = @event.Raw.TryGetProperty("params", out var p) &&
                      p.TryGetProperty("message", out var m) ? m.GetString() : null;
        return BoundedRedactedPreview(message)?.Map(p => $"Codex app-server error: {p}");
    }

    private static string? CommandOutputSummary(NormalizedCodexEvent @event)
    {
        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var delta = @event.MessageDelta ??
                   ExtractStringParam(paramsElement, new[] { "delta", "output", "stdout", "stderr", "text", "content" }) ??
                   NestedStringParam(paramsElement, new[] { "output", "delta" }) ??
                   NestedStringParam(paramsElement, new[] { "output", "text" });
        return BoundedRedactedPreview(delta)?.Map(p => $"Codex command output: {p}");
    }

    private static string? NestedStringParam(JsonElement element, string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (!current.TryGetProperty(key, out var next))
                return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? PlanSummary(NormalizedCodexEvent @event)
    {
        var text = @event.MessageDelta ??
                  (@event.Raw.TryGetProperty("params", out var p) && p.TryGetProperty("text", out var t) ? t.GetString() : null);
        return BoundedRedactedPreview(text)?.Map(p => $"Codex plan: {p}");
    }

    private static string? DiffSummary(NormalizedCodexEvent @event)
    {
        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var summary = ExtractStringParam(paramsElement, new[] { "summary", "path", "filePath", "file" });
        var preview = BoundedRedactedPreview(summary)?.Map(p => $"Codex diff updated: {p}");
        if (preview != null)
            return preview;

        if (paramsElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            return $"Codex diff updated: {files.GetArrayLength()} file(s)";

        return null;
    }

    private static string? TokenUsageSummary(NormalizedCodexEvent @event)
    {
        if (@event.TokenUsage != null)
        {
            var usage = @event.TokenUsage;
            if (usage.CacheReadTokens > 0)
                return $"Codex token usage: {usage.InputTokens} input, {usage.OutputTokens} output, {usage.CacheReadTokens} cache";
            return $"Codex token usage: {usage.InputTokens} input, {usage.OutputTokens} output";
        }

        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var usageElement = paramsElement.TryGetProperty("usage", out var u) ? u : paramsElement;
        var input = ExtractNumberParam(usageElement, new[] { "input_tokens", "inputTokens", "prompt_tokens" });
        var output = ExtractNumberParam(usageElement, new[] { "output_tokens", "outputTokens", "completion_tokens" });
        var cache = ExtractNumberParam(usageElement, new[] { "cache_read_tokens", "cacheReadTokens", "cached_input_tokens" }) ?? 0;

        if (input == null || output == null)
            return null;

        if (cache > 0)
            return $"Codex token usage: {input} input, {output} output, {cache} cache";
        return $"Codex token usage: {input} input, {output} output";
    }

    private static string? ThreadStatusSummary(NormalizedCodexEvent @event)
    {
        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var status = ExtractStringParam(paramsElement, new[] { "status" });
        return BoundedRedactedPreview(status)?.Map(s => $"Codex thread status: {s}");
    }

    private static string? ItemSummary(NormalizedCodexEvent @event)
    {
        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var label = ExtractStringParam(paramsElement, new[] { "title", "label", "kind", "itemType", "type" });
        var preview = BoundedRedactedPreview(label);
        if (preview == null)
            return null;

        var verb = @event.Kind == NormalizedCodexEventKind.ItemStarted ? "started" : "completed";
        return $"Codex item {verb}: {preview}";
    }

    private const int SummaryPreviewChars = 160;

    private static string? BoundedRedactedPreview(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        var cleaned = new string(raw.Select(c => char.IsControl(c) && !char.IsWhiteSpace(c) ? ' ' : c).ToArray());
        var words = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        var redacted = new List<string>();
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant();
            var colonIndex = word.IndexOf(':');
            if (colonIndex >= 0)
            {
                var key = TrimNonAlphanumeric(word[..colonIndex]).ToLowerInvariant();
                if (key == "authorization" || IsSecretKey(key))
                {
                    redacted.Add($"{word[..colonIndex]}:[redacted]");
                    break;
                }
            }

            var bareKey = TrimNonAlphanumeric(lower);
            if (bareKey == "authorization" || IsSecretKey(bareKey))
            {
                redacted.Add(word);
                redacted.Add("[redacted]");
                break;
            }

            redacted.Add(RedactInlineSecret(word));
        }

        return TruncateChars(string.Join(" ", redacted), SummaryPreviewChars);
    }

    private static string TrimNonAlphanumeric(string value)
    {
        var start = 0;
        while (start < value.Length && !char.IsLetterOrDigit(value[start]) && value[start] != '_')
            start++;
        var end = value.Length - 1;
        while (end >= start && !char.IsLetterOrDigit(value[end]) && value[end] != '_')
            end--;
        return value.Substring(start, end - start + 1);
    }

    private static string RedactInlineSecret(string word)
    {
        foreach (var delimiter in new[] { '=', ':' })
        {
            var index = word.IndexOf(delimiter);
            if (index < 0)
                continue;

            var key = TrimNonAlphanumeric(word[..index]).ToLowerInvariant();
            if (IsSecretKey(key))
                return $"{word[..index]}{delimiter}[redacted]";
        }
        return word;
    }

    private static bool IsSecretKey(string key) => key switch
    {
        "api_key" or "apikey" or "access_token" or "refresh_token" or "authorization"
            or "password" or "secret" or "token" => true,
        _ => false
    };

    private static string TruncateChars(string value, int maxChars)
    {
        if (value.Length <= maxChars)
            return value;
        return value[..Math.Max(0, maxChars - 3)] + "...";
    }

    public static (EventKind kind, string summary) EventJournalKindAndSummary(NormalizedCodexEvent @event)
    {
        var kind = @event.Kind switch
        {
            NormalizedCodexEventKind.ApprovalRequested => EventKind.ApprovalRequested,
            NormalizedCodexEventKind.ApprovalCompleted => ApprovalCompletedKind(@event),
            NormalizedCodexEventKind.Error => EventKind.RunFailed,
            NormalizedCodexEventKind.ThreadStatusChanged => ThreadStatusKind(@event),
            NormalizedCodexEventKind.Unknown => EventKind.Unknown(@event.Method),
            _ => EventKind.HarnessEventNormalized(@event.Method)
        };
        return (kind, EventSummary(@event));
    }

    private static EventKind ApprovalCompletedKind(NormalizedCodexEvent @event)
    {
        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var decision = paramsElement.TryGetProperty("decision", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()?.ToLowerInvariant() : null;
        return decision switch
        {
            "approve" => EventKind.ApprovalGranted,
            "reject" => EventKind.ApprovalDenied,
            _ => EventKind.HarnessEventNormalized(@event.Method)
        };
    }

    private static EventKind ThreadStatusKind(NormalizedCodexEvent @event)
    {
        var paramsElement = @event.Raw.TryGetProperty("params", out var p) ? p : default;
        var status = paramsElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()?.ToLowerInvariant() : null;
        return status switch
        {
            "completed" => EventKind.RunCompleted,
            "failed" => EventKind.RunFailed,
            "cancelled" => EventKind.RunCancelled,
            _ => EventKind.HarnessEventNormalized(@event.Method)
        };
    }

    public static ApprovalRiskSummary InferApprovalRisk(JsonElement @params)
    {
        var reasons = new List<string>();
        var command = ExtractStringParam(@params, new[] { "command", "shellCommand", "toolCommand" });
        var normalizedCommand = command?.ToLowerInvariant();
        var filePath = ExtractStringParam(@params, new[] { "filePath", "path" });

        ApprovalRiskLevel level;
        if (normalizedCommand != null)
        {
            if (normalizedCommand.Contains("sudo") || normalizedCommand.Contains("rm -rf") ||
                normalizedCommand.Contains("chmod") || normalizedCommand.Contains("chown"))
            {
                reasons.Add("Command can mutate privileged or destructive host state.");
                level = ApprovalRiskLevel.High;
            }
            else
            {
                reasons.Add("Command execution requires explicit operator approval.");
                level = ApprovalRiskLevel.Medium;
            }
        }
        else if (filePath != null)
        {
            reasons.Add("File write can mutate workspace or host state.");
            level = ApprovalRiskLevel.Medium;
        }
        else
        {
            level = ApprovalRiskLevel.Unknown;
        }

        return new ApprovalRiskSummary(level, reasons);
    }
}

internal static class OptionExtensions
{
    public static TResult? Map<TSource, TResult>(this TSource? source, Func<TSource, TResult> mapper) =>
        source != null ? mapper(source) : default;
}