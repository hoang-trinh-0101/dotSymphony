using System.Text.Json;
using OpenSymphony.OpenHands;

namespace OpenSymphony.TestKit;

// ht: minimal port of opensymphony-testkit lib.rs.
//   Fake OpenHands server for integration testing.

public sealed class FakeOpenHandsConfig
{
    public int SearchPageSize { get; init; } = 2;
    public string RunTerminalStatus { get; init; } = "finished";
    public string InitialExecutionStatus { get; init; } = "idle";
}

public sealed class FakeConversationBuilder
{
    private readonly WorkspaceConfig _workspace;
    private readonly string _persistenceDir;
    private readonly uint _maxIterations;
    private readonly bool _stuckDetection;
    private string _executionStatus = "idle";
    private readonly ConfirmationPolicy _confirmationPolicy;
    private readonly AgentConfig _agent;

    public FakeConversationBuilder(ConversationCreateRequest request)
    {
        _workspace = request.Workspace;
        _persistenceDir = request.PersistenceDir;
        _maxIterations = request.MaxIterations;
        _stuckDetection = request.StuckDetection;
        _confirmationPolicy = request.ConfirmationPolicy;
        _agent = request.Agent;
    }

    public static FakeConversationBuilder FromRequest(ConversationCreateRequest request) =>
        new(request);

    public FakeConversationBuilder WithExecutionStatus(string status)
    {
        _executionStatus = status;
        return this;
    }

    public Conversation Build(Guid conversationId) => new(
        conversationId,
        _workspace,
        _persistenceDir,
        _maxIterations,
        _stuckDetection,
        _executionStatus,
        _confirmationPolicy,
        _agent);
}

public sealed class FakeEventStreamBuilder
{
    private readonly DateTimeOffset _baseTimestamp;

    public FakeEventStreamBuilder() : this(DateTimeOffset.UtcNow) { }

    public FakeEventStreamBuilder(DateTimeOffset baseTimestamp)
    {
        _baseTimestamp = baseTimestamp;
    }

    public EventEnvelope CustomAt(string id, long offsetMs, string source, string kind, JsonElement payload) =>
        new(id, _baseTimestamp.AddMilliseconds(offsetMs), source, kind, payload);

    public EventEnvelope StateUpdateAt(string id, long offsetMs, string executionStatus)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            execution_status = executionStatus,
            state_delta = new { execution_status = executionStatus },
        });
        return CustomAt(id, offsetMs, "runtime", "ConversationStateUpdateEvent", payload);
    }

    public EventEnvelope LlmCompletionAt(string id, long offsetMs, string model, ulong tokens)
    {
        var payload = JsonSerializer.SerializeToElement(new { model, tokens });
        return CustomAt(id, offsetMs, "llm", "LLMCompletionLogEvent", payload);
    }

    public EventEnvelope ConversationErrorAt(string id, long offsetMs, string message)
    {
        var payload = JsonSerializer.SerializeToElement(new { message });
        return CustomAt(id, offsetMs, "runtime", "ConversationErrorEvent", payload);
    }
}

public sealed class FakeSearchScript
{
    private readonly List<SearchConversationEventsResponse> _responses = [];

    public FakeSearchScript Response(List<EventEnvelope> events)
    {
        _responses.Add(new SearchConversationEventsResponse { Events = events });
        return this;
    }

    public FakeSearchScript PagedResponse(List<EventEnvelope> events, string? nextPageId)
    {
        _responses.Add(new SearchConversationEventsResponse { Events = events, NextPageId = nextPageId });
        return this;
    }

    public IReadOnlyList<SearchConversationEventsResponse> Responses => _responses;
}

public enum FakeSocketAction
{
    Event,
    Text,
    Ping,
    Close
}

public sealed class FakeSocketScript
{
    private readonly List<(FakeSocketAction Kind, object? Data)> _actions = [];

    public FakeSocketScript Event(EventEnvelope evt)
    {
        _actions.Add((FakeSocketAction.Event, evt));
        return this;
    }

    public FakeSocketScript Text(string payload)
    {
        _actions.Add((FakeSocketAction.Text, payload));
        return this;
    }

    public FakeSocketScript Ping(byte[] payload)
    {
        _actions.Add((FakeSocketAction.Ping, payload));
        return this;
    }

    public FakeSocketScript Close()
    {
        _actions.Add((FakeSocketAction.Close, null));
        return this;
    }

    public IReadOnlyList<(FakeSocketAction Kind, object? Data)> Actions => _actions;
}

public sealed class FakeServerError : Exception
{
    public FakeServerError(string message) : base(message) { }
}

// ht: FakeOpenHandsServer uses ASP.NET Core minimal APIs.
//   For minimal test support, only basic builders are ported.
//   Full HTTP/WebSocket server is YAGNI until tests require it.