using System.Text.Json;
using OpenSymphony.OpenHands;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.OpenHands.Tests;

public class EventsTests
{
    private static Conversation MakeConversation(JsonElement? stats = null) => new(
        Guid.Empty,
        new WorkspaceConfig("/tmp/workspace", "LocalWorkspace"),
        "/tmp/workspace/.opensymphony/openhands",
        4, true, "idle",
        new ConfirmationPolicy("NeverConfirm"),
        new AgentConfig("Agent", new LlmConfig { Model = "openai/gpt-5.4" }),
        stats);

    [Fact]
    public void KnownEvent_Decoding_PreservesKnownAndUnknownPayloads()
    {
        var stateUpdate = new EventEnvelope("evt-state", DateTimeOffset.UtcNow, "runtime",
            "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "running", state_delta = new { execution_status = "running" } }));
        var llmLog = new EventEnvelope("evt-llm", DateTimeOffset.UtcNow, "llm",
            "LLMCompletionLogEvent", JsonSerializer.SerializeToElement(new { model = "fake-model" }));
        var errorEvent = new EventEnvelope("evt-error", DateTimeOffset.UtcNow, "runtime",
            "ConversationErrorEvent", JsonSerializer.SerializeToElement(new { message = "boom" }));
        var unknownEvent = new EventEnvelope("evt-unknown", DateTimeOffset.UtcNow, "runtime",
            "ForwardCompatibleEvent", JsonSerializer.SerializeToElement(new { opaque = true }));

        Assert.IsType<KnownEvent.ConversationStateUpdate>(KnownEvent.FromEnvelope(stateUpdate));
        Assert.IsType<KnownEvent.LlmCompletionLog>(KnownEvent.FromEnvelope(llmLog));
        Assert.IsType<KnownEvent.ConversationError>(KnownEvent.FromEnvelope(errorEvent));
        var unknown = Assert.IsType<KnownEvent.Unknown>(KnownEvent.FromEnvelope(unknownEvent));
        Assert.Equal("ForwardCompatibleEvent", unknown.Event.Kind);
    }

    [Fact]
    public void KnownEvent_Decoding_PreservesFullMessageTextPreview()
    {
        var longText = "this message should stay intact past eighty characters so the TUI can wrap it fully";
        var envelope = new EventEnvelope("evt-message", DateTimeOffset.UtcNow, "user", "MessageEvent",
            JsonSerializer.SerializeToElement(new
            {
                role = "assistant",
                content = new[] { new { type = "text", text = longText } },
            }));

        var msg = Assert.IsType<KnownEvent.Message>(KnownEvent.FromEnvelope(envelope));
        Assert.Equal(longText, msg.Payload.TextPreview);
    }

    [Fact]
    public void KnownEvent_Decoding_PreservesFullObservationTextPreview()
    {
        var longText = "this observation should stay intact past eighty characters so the TUI can wrap it fully";
        var envelope = new EventEnvelope("evt-observation", DateTimeOffset.UtcNow, "runtime", "ObservationEvent",
            JsonSerializer.SerializeToElement(new
            {
                observation = new
                {
                    tool_name = "cat",
                    content = new[] { new { type = "text", text = longText } },
                },
            }));

        var obs = Assert.IsType<KnownEvent.Observation>(KnownEvent.FromEnvelope(envelope));
        Assert.Equal(longText, obs.Payload.TextPreview);
    }

    [Fact]
    public void EventCache_OrdersAndDeduplicatesNewEvents()
    {
        var cache = new EventCache();
        var now = DateTimeOffset.UtcNow;
        var newer = new EventEnvelope("evt-2", now, "runtime", "ConversationStateUpdateEvent", default);
        var older = new EventEnvelope("evt-1", now.AddSeconds(-10), "runtime", "ConversationStateUpdateEvent", default);

        var inserted = cache.MergeNewEvents([newer, older, older]);

        Assert.Equal(2, inserted.Count);
        Assert.Equal("evt-1", inserted[0].Id);
        Assert.Equal("evt-2", inserted[1].Id);
        Assert.Equal("evt-1", cache.Items[0].Id);
        Assert.Equal("evt-2", cache.Items[1].Id);
    }

    [Fact]
    public void StateMirror_Rebuild_KeepsLatestTerminalStatusAfterOutOfOrderEvents()
    {
        var conversation = MakeConversation();
        var now = DateTimeOffset.UtcNow;
        var running = new EventEnvelope("evt-running", now, "runtime", "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "running", state_delta = new { execution_status = "running" } }));
        var stale = new EventEnvelope("evt-queued", now.AddSeconds(-5), "runtime", "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "queued", state_delta = new { execution_status = "queued" } }));
        var finished = new EventEnvelope("evt-finished", now.AddSeconds(5), "runtime", "ConversationStateUpdateEvent",
            JsonSerializer.SerializeToElement(new { execution_status = "finished", state_delta = new { execution_status = "finished" } }));

        var cache = new EventCache();
        cache.MergeNewEvents([running, stale, finished]);

        var mirror = new ConversationStateMirror();
        mirror.RebuildFrom(conversation, cache.Items);

        Assert.Equal("finished", mirror.ExecutionStatus);
        Assert.Equal(TerminalExecutionStatus.Finished, mirror.TerminalStatus());
    }

    [Fact]
    public void StateMirror_IncludesStatsFromConversation()
    {
        var stats = JsonSerializer.SerializeToElement(new
        {
            usage_to_metrics = new
            {
                @default = new
                {
                    accumulated_token_usage = new
                    {
                        prompt_tokens = 1000,
                        completion_tokens = 500,
                        cache_read_tokens = 200,
                    }
                }
            }
        });
        var conversation = MakeConversation(stats);

        var mirror = new ConversationStateMirror();
        mirror.ApplyConversation(conversation);

        var (input, output, cacheRead) = mirror.AccumulatedTokenUsage()!.Value;
        Assert.Equal(1000UL, input);
        Assert.Equal(500UL, output);
        Assert.Equal(200UL, cacheRead);
    }
}
