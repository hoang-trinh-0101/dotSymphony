using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of transport recommendation metadata.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransportProfile
{
    InProcessChannel,
    NativeIpc,
    TauriChannel,
    LoopbackHttp,
    LoopbackWebSocket,
    Sse,
    WebSocket,
    JsonRpcOverWebSocket,
}

public sealed record TransportRecommendation(
    TransportProfile Profile,
    byte Priority,
    string Description,
    uint ExpectedLatencyMs,
    ulong ExpectedThroughputKbps,
    bool ReconnectSupport,
    bool ReplaySupport,
    bool BinaryFrameSupport,
    bool AuthRequired
);