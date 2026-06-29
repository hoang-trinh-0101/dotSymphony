using System.Threading.Channels;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Gateway;

// ht: minimal in-memory event journal with broadcast pub/sub.
//   Capacity: 10,000 events, 256 subscribers (matches Rust constants).
//   Ceiling: O(n) scan for all_events() and query operations.
//   Upgrade path: persistent storage with cursor-based pagination.

public sealed class InMemoryEventJournal
{
    private readonly List<EventRecord> _events = new();
    private readonly object _lock = new();
    private readonly List<JournalSubscriber> _subscribers = new();
    private readonly int _capacity;
    private readonly int _maxSubscribers;
    private ulong _sequence = 0;

    public InMemoryEventJournal(int capacity = 10_000, int maxSubscribers = 256)
    {
        _capacity = capacity;
        _maxSubscribers = maxSubscribers;
    }

    public async Task<EventRecord> Append(EventRecord record)
    {
        lock (_lock)
        {
            _sequence++;
            var sequenced = record with { Sequence = _sequence };
            _events.Add(sequenced);

            // Evict oldest if over capacity
            if (_events.Count > _capacity)
            {
                _events.RemoveAt(0);
            }

            // Broadcast to subscribers
            foreach (var sub in _subscribers)
            {
                if (!sub.Channel.Writer.TryWrite(sequenced))
                {
                    sub.IsLagged = true;
                }
            }

            return sequenced;
        }
    }

    public async Task<List<EventRecord>> AllEvents()
    {
        lock (_lock)
        {
            return new List<EventRecord>(_events);
        }
    }

    public JournalSubscriber Subscribe()
    {
        lock (_lock)
        {
            if (_subscribers.Count >= _maxSubscribers)
            {
                throw new InvalidOperationException($"Maximum subscribers {_maxSubscribers} reached");
            }

            var sub = new JournalSubscriber();
            _subscribers.Add(sub);
            return sub;
        }
    }

    public void Unsubscribe(JournalSubscriber subscriber)
    {
        lock (_lock)
        {
            _subscribers.Remove(subscriber);
        }
    }
}

public sealed class JournalSubscriber
{
    public Channel<EventRecord> Channel { get; }
    public bool IsLagged { get; set; }

    public JournalSubscriber()
    {
        Channel = System.Threading.Channels.Channel.CreateUnbounded<EventRecord>();
    }
}