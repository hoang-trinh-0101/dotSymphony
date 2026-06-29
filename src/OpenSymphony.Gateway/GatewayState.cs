using OpenSymphony.Control;
using OpenSymphony.Domain;

namespace OpenSymphony.Gateway;

// ht: minimal gateway state using Control.SnapshotStore.
//   Ceiling: O(1) clone cost for Arc-wrapped fields.
//   Upgrade path: partitioned state per-tenant for hosted mode.

public sealed record CodexReadinessCache
{
    private DateTimeOffset? _lastChecked;
    private GatewaySchema.CodexLocalReadiness? _cachedReadiness;
    private readonly object _lock = new();

    public async Task<GatewaySchema.CodexLocalReadiness> Readiness(string command)
    {
        lock (_lock)
        {
            if (_lastChecked.HasValue && DateTimeOffset.UtcNow - _lastChecked.Value < TimeSpan.FromSeconds(30))
            {
                return _cachedReadiness ?? GatewaySchema.CodexLocalReadiness.NotChecked();
            }
        }

        // ht: stub - real implementation would probe codex CLI
        var readiness = GatewaySchema.CodexLocalReadiness.NotChecked();

        lock (_lock)
        {
            _lastChecked = DateTimeOffset.UtcNow;
            _cachedReadiness = readiness;
        }

        return readiness;
    }
}

public sealed record GatewayState(
    SnapshotStore Store,
    InMemoryEventJournal Journal,
    StreamBroker Broker,
    ActionHandler ActionHandler,
    ILinearMutationClient? LinearMutations,
    CodexReadinessCache CodexReadinessCache,
    string? WebAssetsDir = null
)
{
    public static GatewayState Create(SnapshotStore store)
    {
        var journal = new InMemoryEventJournal(10_000, 256);
        var broker = new StreamBroker(journal);
        var actionHandler = new ActionHandler(journal);
        var codexCache = new CodexReadinessCache();

        return new GatewayState(store, journal, broker, actionHandler, null, codexCache);
    }
}