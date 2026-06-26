using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of gateway-schema event_journal + envelope types needed by OpenHands normalization.

public enum EntityKind
{
    Issue,
    SubIssue,
    Milestone,
    Run,
    Workspace,
    Conversation,
    TerminalSession,
    PlanningSession,
    Project,
    Repository,
    Agent,
    Harness,
    Unknown,
}

public sealed record EntityRef(EntityKind Kind, string Id, string? Identifier = null);

public sealed record SchemaVersion(ushort Major, ushort Minor, ushort Patch)
{
    public static SchemaVersion V1() => new(1, 0, 0);
    public string AsStr() => $"{Major}.{Minor}.{Patch}";
    public override string ToString() => AsStr();
}

public enum EventActorKind
{
    System,
    User,
    Agent,
    Harness,
}

public sealed record EventActor(EventActorKind Kind, string Id)
{
    public static EventActor System(string id) => new(EventActorKind.System, id);
    public static EventActor User(string id) => new(EventActorKind.User, id);
    public static EventActor Agent(string id) => new(EventActorKind.Agent, id);
    public static EventActor Harness(string id) => new(EventActorKind.Harness, id);

    public string KindLabel() => Kind switch
    {
        EventActorKind.System => "system",
        EventActorKind.User => "user",
        EventActorKind.Agent => "agent",
        EventActorKind.Harness => "harness",
        _ => Kind.ToString(),
    };

    public string ActorId() => Id;
}

// ht: EventKind as a record with a discriminator + optional source_kind/raw_kind.
//   Rust has ~30 variants; we only need the harness ones for normalization.
public sealed record EventKind
{
    public string Tag { get; }
    public string? SourceKind { get; }
    public string? RawKind { get; }

    private EventKind(string tag, string? sourceKind = null, string? rawKind = null)
    {
        Tag = tag;
        SourceKind = sourceKind;
        RawKind = rawKind;
    }

    public static EventKind HarnessEventNormalized(string sourceKind) => new("harness.event_normalized", sourceKind);
    public static EventKind HarnessToolCall => new("harness.tool_call");
    public static EventKind HarnessToolResult => new("harness.tool_result");
    public static EventKind HarnessConversationStateUpdate => new("harness.conversation_state_update");
    public static EventKind Unknown(string rawKind) => new(rawKind, null, rawKind);

    public bool IsUnknown => RawKind is not null;
    public bool IsHarnessConversationStateUpdate => Tag == "harness.conversation_state_update";
    public bool IsHarnessToolCall => Tag == "harness.tool_call";
    public bool IsHarnessToolResult => Tag == "harness.tool_result";
    public bool IsHarnessEventNormalized => Tag == "harness.event_normalized";
}

public sealed record EventRecord
{
    public string EventId { get; init; } = "";
    public ulong Sequence { get; init; }
    public SchemaVersion SchemaVersion { get; init; } = SchemaVersion.V1();
    public EventActor Actor { get; init; } = EventActor.System("system");
    public string? CorrelationId { get; init; }
    public List<EntityRef> EntityRefs { get; init; } = [];
    public DateTimeOffset HappenedAt { get; init; }
    public string Summary { get; init; } = "";
    public EventKind Kind { get; init; } = EventKind.Unknown("unspecified");
    public JsonElement? Payload { get; init; }
    public string? RawPayloadRef { get; init; }
}

public sealed class EventRecordBuilder
{
    private string? _eventId;
    private ulong _sequence;
    private SchemaVersion? _schemaVersion;
    private EventActor? _actor;
    private string? _correlationId;
    private readonly List<EntityRef> _entityRefs = [];
    private DateTimeOffset? _happenedAt;
    private string _summary = "";
    private EventKind? _kind;
    private JsonElement? _payload;
    private string? _rawPayloadRef;

    public EventRecordBuilder EventId(string id) { _eventId = id; return this; }
    public EventRecordBuilder Sequence(ulong seq) { _sequence = seq; return this; }
    public EventRecordBuilder SchemaVersion(SchemaVersion sv) { _schemaVersion = sv; return this; }
    public EventRecordBuilder Actor(EventActor actor) { _actor = actor; return this; }
    public EventRecordBuilder CorrelationId(string? id) { _correlationId = id; return this; }
    public EventRecordBuilder EntityRefs(List<EntityRef> refs) { _entityRefs.Clear(); _entityRefs.AddRange(refs); return this; }
    public EventRecordBuilder HappenedAt(DateTimeOffset ts) { _happenedAt = ts; return this; }
    public EventRecordBuilder Summary(string summary) { _summary = summary; return this; }
    public EventRecordBuilder Kind(EventKind kind) { _kind = kind; return this; }
    public EventRecordBuilder Payload(JsonElement payload) { _payload = payload; return this; }
    public EventRecordBuilder RawPayloadRef(string? ref_) { _rawPayloadRef = ref_; return this; }

    public EventRecord Build() => new()
    {
        EventId = _eventId ?? Guid.NewGuid().ToString(),
        Sequence = _sequence,
        SchemaVersion = _schemaVersion ?? global::OpenSymphony.GatewaySchema.SchemaVersion.V1(),
        Actor = _actor ?? EventActor.System("system"),
        CorrelationId = _correlationId,
        EntityRefs = _entityRefs,
        HappenedAt = _happenedAt ?? DateTimeOffset.UtcNow,
        Summary = _summary,
        Kind = _kind ?? EventKind.Unknown("unspecified"),
        Payload = _payload,
        RawPayloadRef = _rawPayloadRef,
    };
}
