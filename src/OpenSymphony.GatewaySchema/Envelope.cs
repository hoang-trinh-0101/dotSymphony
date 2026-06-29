using System.Text.Json;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of envelope types. EntityKind and EntityRef are in EventJournal.cs.

public sealed record GatewayEnvelope(
    SchemaVersion SchemaVersion,
    StreamCursor Cursor,
    EntityRef EntityRef,
    string EventKind,
    JsonElement? Payload,
    JsonElement? RawPayload,
    DateTimeOffset EmittedAt
)
{
    public static GatewayEnvelope New(
        StreamCursor cursor,
        EntityRef entityRef,
        string eventKind,
        JsonElement payload
    ) => new(
        SchemaVersion.V1(),
        cursor,
        entityRef,
        eventKind,
        payload,
        payload.Clone(),
        DateTimeOffset.UtcNow
    );

    public static GatewayEnvelope FromRawPayload(
        StreamCursor cursor,
        EntityRef entityRef,
        string eventKind,
        JsonElement rawPayload
    ) => new(
        SchemaVersion.V1(),
        cursor,
        entityRef,
        eventKind,
        null,
        rawPayload,
        DateTimeOffset.UtcNow
    );
}

// ht: EntityRef factory methods (EntityRef record is in EventJournal.cs)
public static class EntityRefFactory
{
    public static EntityRef Issue(string id, string? identifier = null) => new(EntityKind.Issue, id, identifier);
    public static EntityRef Run(string id) => new(EntityKind.Run, id, null);
    public static EntityRef Terminal(string id) => new(EntityKind.TerminalSession, id, null);
}