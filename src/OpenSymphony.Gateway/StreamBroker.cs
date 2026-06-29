namespace OpenSymphony.Gateway;

// ht: minimal WebSocket connection broker for event streaming.
//   Ceiling: O(connections) memory per active connection.
//   Upgrade path: connection pooling with backpressure handling.

public sealed class StreamBroker
{
    private readonly InMemoryEventJournal _journal;
    private readonly Dictionary<string, BrokerConnection> _connections = new();
    private readonly object _lock = new();

    public StreamBroker(InMemoryEventJournal journal)
    {
        _journal = journal;
    }

    public string RegisterConnection()
    {
        var connectionId = Guid.NewGuid().ToString();
        lock (_lock)
        {
            _connections[connectionId] = new BrokerConnection(connectionId);
        }
        return connectionId;
    }

    public void UnregisterConnection(string connectionId)
    {
        lock (_lock)
        {
            _connections.Remove(connectionId);
        }
    }

    public BrokerConnection? GetConnection(string connectionId)
    {
        lock (_lock)
        {
            return _connections.GetValueOrDefault(connectionId);
        }
    }

    public InMemoryEventJournal Journal => _journal;
}

public sealed class BrokerConnection
{
    public string ConnectionId { get; }
    public DateTimeOffset ConnectedAt { get; }

    public BrokerConnection(string connectionId)
    {
        ConnectionId = connectionId;
        ConnectedAt = DateTimeOffset.UtcNow;
    }
}