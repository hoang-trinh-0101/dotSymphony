using System.Threading.Channels;
using OpenSymphony.Domain;

namespace OpenSymphony.Control;

// ht: Rust broadcast::channel(64) → per-subscriber bounded Channel with lag flag.
//   Ceiling: O(backlog) skip per catch-up read under sustained publication.
//   Upgrade path: drain channel on lag, or use a ring buffer with sequence tracking.
public sealed class SnapshotStore
{
    private SnapshotEnvelope _current;
    private readonly List<SnapshotSubscriber> _subscribers = new();
    private readonly object _lock = new();

    public SnapshotStore(ControlPlaneDaemonSnapshot initialSnapshot)
    {
        _current = new SnapshotEnvelope(1, DateTimeOffset.UtcNow, initialSnapshot);
    }

    public SnapshotEnvelope Current()
    {
        lock (_lock) return _current;
    }

    public SnapshotEnvelope Publish(ControlPlaneDaemonSnapshot snapshot)
    {
        SnapshotEnvelope next;
        lock (_lock)
        {
            next = new SnapshotEnvelope(_current.Sequence + 1, DateTimeOffset.UtcNow, snapshot);
            _current = next;
            foreach (var sub in _subscribers)
            {
                if (!sub.Channel.Writer.TryWrite(next))
                    sub.IsLagged = true;
            }
        }
        return next;
    }

    public SnapshotSubscriber Subscribe()
    {
        var sub = new SnapshotSubscriber();
        lock (_lock) _subscribers.Add(sub);
        return sub;
    }
}

public sealed class SnapshotSubscriber
{
    public System.Threading.Channels.Channel<SnapshotEnvelope> Channel { get; } =
        System.Threading.Channels.Channel.CreateBounded<SnapshotEnvelope>(64);
    public bool IsLagged { get; set; }
}

// ht: fast-forward lagged subscribers to the latest snapshot without draining backlog.
public static class SnapshotCatchUp
{
    public static SnapshotEnvelope? CatchUpLaggedReceiver(
        SnapshotStore store, ulong lastSentSequence)
    {
        var latest = store.Current();
        return latest.Sequence > lastSentSequence ? latest : null;
    }

    public static async Task<SnapshotEnvelope?> NextSnapshotEnvelope(
        SnapshotStore store, SnapshotSubscriber subscriber, ulong lastSentSequence)
    {
        var lastSent = lastSentSequence;
        while (true)
        {
            if (subscriber.IsLagged)
            {
                subscriber.IsLagged = false;
                var caught = CatchUpLaggedReceiver(store, lastSent);
                if (caught is { } envelope)
                {
                    lastSent = envelope.Sequence;
                    return envelope;
                }
            }

            if (!await subscriber.Channel.Reader.WaitToReadAsync())
                return null;

            while (subscriber.Channel.Reader.TryRead(out var envelope))
            {
                if (envelope.Sequence > lastSent)
                {
                    lastSent = envelope.Sequence;
                    return envelope;
                }
            }
        }
    }
}
