namespace OpenSymphony.Codex;

public record CodexBenchmarkRequirement
{
    public string Dimension { get; init; } = string.Empty;
    public string Probe { get; init; } = string.Empty;
    public string AcceptanceSignal { get; init; } = string.Empty;
}

public static class CodexBenchmark
{
    public static List<CodexBenchmarkRequirement> WebSocketBenchmarkRequirements() => new()
    {
        new()
        {
            Dimension = "throughput",
            Probe = "send a batch of JSON-RPC thread/loaded/list requests over ws://127.0.0.1",
            AcceptanceSignal = "all responses arrive with matching ids and measured requests/sec"
        },
        new()
        {
            Dimension = "queue behavior",
            Probe = "enqueue many requests without awaiting per-request responses",
            AcceptanceSignal = "response count matches sent count and p50/p95 latency is recorded"
        },
        new()
        {
            Dimension = "reconnect",
            Probe = "close the WebSocket, reconnect, and run initialize again",
            AcceptanceSignal = "new connection reaches ready state and responds after reconnect"
        },
        new()
        {
            Dimension = "secure exposure",
            Probe = "verify localhost-only default plus capability-token and signed-bearer flags",
            AcceptanceSignal = "non-loopback exposure remains gated by explicit auth settings"
        }
    };
}