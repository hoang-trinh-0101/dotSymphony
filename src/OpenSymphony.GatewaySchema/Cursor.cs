using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of cursor types for stream replay and pagination.

public sealed record StreamCursor(ulong Sequence, string Partition, ulong? TimestampAnchor = null)
{
    public static StreamCursor New(ulong sequence, string partition) => new(sequence, partition);
    public StreamCursor WithTimestampAnchor(ulong anchor) => this with { TimestampAnchor = anchor };
}

public sealed record PageCursor(string PageToken, uint PageSize)
{
    public static PageCursor First(uint pageSize) => new(string.Empty, pageSize);
}