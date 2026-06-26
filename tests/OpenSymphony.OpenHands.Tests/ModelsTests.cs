using System.Text.Json;
using OpenSymphony.OpenHands;
using OpenSymphony.GatewaySchema;
using OpenSymphony.Domain;

namespace OpenSymphony.OpenHands.Tests;

public class ModelsTests
{
    private static readonly Guid TestUuid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ConversationCreateRequest MakeRequest(Action<AgentConfig>? customize = null)
    {
        var agent = new AgentConfig(
            "Agent",
            new LlmConfig { Model = "fake-model", ApiKey = "fake-key" });
        customize?.Invoke(agent);
        return new ConversationCreateRequest(
            TestUuid,
            new WorkspaceConfig("/tmp/workspace", "LocalWorkspace"),
            "/tmp/workspace/.opensymphony/openhands",
            7, true,
            new ConfirmationPolicy("NeverConfirm"),
            agent);
    }

    [Fact]
    public void ConversationCreateRequest_Serializes_MinimalContract()
    {
        var request = MakeRequest();
        var json = JsonSerializer.Serialize(request, OpenHandsJsonOptions.Default);
        var value = JsonDocument.Parse(json).RootElement;

        Assert.Equal("11111111-2222-3333-4444-555555555555", value.GetProperty("conversation_id").GetString());
        Assert.Equal("/tmp/workspace", value.GetProperty("workspace").GetProperty("working_dir").GetString());
        Assert.Equal("LocalWorkspace", value.GetProperty("workspace").GetProperty("kind").GetString());
        Assert.Equal(7u, value.GetProperty("max_iterations").GetUInt32());
        Assert.True(value.GetProperty("stuck_detection").GetBoolean());
        Assert.Equal("NeverConfirm", value.GetProperty("confirmation_policy").GetProperty("kind").GetString());
        Assert.Equal("Agent", value.GetProperty("agent").GetProperty("kind").GetString());
        Assert.Equal("fake-model", value.GetProperty("agent").GetProperty("llm").GetProperty("model").GetString());
        Assert.Equal("fake-key", value.GetProperty("agent").GetProperty("llm").GetProperty("api_key").GetString());
    }

    [Fact]
    public void LlmConfig_Debug_RedactsApiKey()
    {
        var llm = new LlmConfig
        {
            Model = "openai/gpt-5.2-codex",
            ApiKey = "oauth-access-token",
            BaseUrl = "https://chatgpt.com/backend-api/codex",
            ExtraHeaders = new Dictionary<string, string>
            {
                ["chatgpt-account-id"] = "account-123",
                ["originator"] = "codex_cli_rs",
            },
        };

        var rendered = llm.ToString();
        Assert.Contains("<redacted>", rendered);
        Assert.DoesNotContain("oauth-access-token", rendered);
        Assert.DoesNotContain("account-123", rendered);
    }

    [Fact]
    public void ConversationRunRequest_Serializes_ToEmptyObject()
    {
        var json = JsonSerializer.Serialize(new ConversationRunRequest(), OpenHandsJsonOptions.Default);
        Assert.Equal("{}", json);
    }

    [Fact]
    public void ConversationCreateRequest_Serializes_OptionalCondenser()
    {
        var request = MakeRequest(a => { });
        // ht: we test that condenser is null by default (skip_serializing_if = null)
        var json = JsonSerializer.Serialize(request, OpenHandsJsonOptions.Default);
        var value = JsonDocument.Parse(json).RootElement;
        Assert.False(value.GetProperty("agent").TryGetProperty("condenser", out _));
    }

    [Fact]
    public void EventEnvelope_Deserializes_FlattenedAgentServerEvents()
    {
        var json = """
        {
            "id": "evt-message",
            "timestamp": "2026-03-23T12:07:58.942514",
            "source": "user",
            "kind": "MessageEvent",
            "llm_message": {
                "role": "user",
                "content": [{ "type": "text", "text": "hello", "cache_prompt": false }],
                "thinking_blocks": []
            },
            "activated_skills": [],
            "extended_content": []
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;
        var envelope = EventEnvelope.Deserialize(element);

        Assert.Equal("MessageEvent", envelope.Kind);
        Assert.True(envelope.Payload.TryGetProperty("llm_message", out var lm));
        Assert.True(lm.TryGetProperty("content", out var content));
        Assert.Equal("hello", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public void EventEnvelope_RoundTrips_NestedPayloadStateUpdates()
    {
        var envelope = EventEnvelope.StateUpdate("evt-state", "finished");
        var json = envelope.Serialize();
        var value = JsonDocument.Parse(json).RootElement;

        Assert.Equal("evt-state", value.GetProperty("id").GetString());
        Assert.Equal("runtime", value.GetProperty("source").GetString());
        Assert.Equal("ConversationStateUpdateEvent", value.GetProperty("kind").GetString());
        Assert.Equal("finished", value.GetProperty("execution_status").GetString());

        var decoded = EventEnvelope.Deserialize(value);
        Assert.Equal("finished", decoded.Payload.GetProperty("execution_status").GetString());
    }
}
