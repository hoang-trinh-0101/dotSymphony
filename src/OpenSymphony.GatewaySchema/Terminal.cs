using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of terminal/log types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TerminalFrameKind
{
    Stdout,
    Stderr,
    Log,
    Prompt,
    Status,
    EndOfStream,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TerminalEncoding
{
    Utf8,
    Base64,
}

public sealed record TerminalLogAssociation(
    string RunId,
    string WorkspaceId,
    string? CommandId = null,
    string? IssueId = null,
    string? SubIssueId = null,
    string? HarnessSessionId = null
);

public sealed record TerminalFrame(
    SchemaVersion SchemaVersion,
    ulong FrameSequence,
    string StreamId,
    string RunId,
    string TerminalSessionId,
    TerminalFrameKind FrameKind,
    TerminalEncoding Encoding,
    string Content,
    DateTimeOffset Timestamp,
    TerminalLogAssociation Association = null!,
    string? CorrelationId = null,
    string? SourceEventId = null,
    string? FrameId = null
)
{
    public TerminalFrame() : this(SchemaVersion.V1(), 0, "", "", "", default, default, "", DateTimeOffset.UtcNow) { }
}

public sealed record TerminalSession(
    SchemaVersion SchemaVersion,
    string TerminalSessionId,
    string RunId,
    TerminalLogAssociation Association,
    ulong FrameCount,
    ulong TotalBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ulong CurrentCursor
);

public sealed record TerminalSnapshot(
    SchemaVersion SchemaVersion,
    string TerminalSessionId,
    string RunId,
    List<TerminalFrame> Frames,
    ulong TotalFrames,
    bool Truncated,
    ulong Cursor,
    TerminalSession? Session = null
);