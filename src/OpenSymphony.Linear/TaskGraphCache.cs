using OpenSymphony.Domain;

namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/task_graph_cache.rs.

public sealed class CachedLinearEntity
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string StateKind { get; set; } = "";
    public byte? Priority { get; set; }
    public List<string> Labels { get; set; } = new();
    public string? ParentId { get; set; }
    public CachedMilestone? ProjectMilestone { get; set; }
    public List<CachedBlockerRef> BlockedBy { get; set; } = new();
    public List<CachedIssueRef> SubIssues { get; set; } = new();
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

public sealed class CachedMilestone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public override bool Equals(object? obj) => obj is CachedMilestone other && Id == other.Id && Name == other.Name;
    public override int GetHashCode() => HashCode.Combine(Id, Name);
}

public sealed class CachedBlockerRef
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public bool IsTerminal { get; set; }

    public override bool Equals(object? obj) => obj is CachedBlockerRef other && Id == other.Id && Identifier == other.Identifier && Title == other.Title && State == other.State && IsTerminal == other.IsTerminal;
    public override int GetHashCode() => HashCode.Combine(Id, Identifier, Title, State, IsTerminal);
}

public sealed class CachedIssueRef
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string State { get; set; } = "";

    public override bool Equals(object? obj) => obj is CachedIssueRef other && Id == other.Id && Identifier == other.Identifier && State == other.State;
    public override int GetHashCode() => HashCode.Combine(Id, Identifier, State);
}

public sealed class RuntimeOverlay
{
    public string IssueId { get; set; } = "";
    public bool Eligible { get; set; }
    public bool Queued { get; set; }
    public string? ActiveRunId { get; set; }
    public string? LastOutcome { get; set; }
    public uint RetryCount { get; set; }
    public string? WorkspaceId { get; set; }
    public string? ConversationId { get; set; }
    public DateTimeOffset? LastEventAt { get; set; }
    public string? ValidationStatus { get; set; }
    public string? BlockerSummary { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

public sealed class TaskGraphCache
{
    private readonly Dictionary<string, CachedLinearEntity> _entities = new();
    private readonly Dictionary<string, RuntimeOverlay> _overlays = new();

    public string ProjectId { get; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    private readonly TimeSpan _ttl;

    public TaskGraphCache(string projectId, TimeSpan ttl)
    {
        ProjectId = projectId;
        _ttl = ttl;
    }

    public void UpsertEntities(List<TrackerIssue> issues)
    {
        var syncedAt = DateTimeOffset.UtcNow;
        foreach (var issue in issues)
        {
            var entity = FromTrackerIssue(issue);
            entity.SyncedAt = syncedAt;
            _entities[entity.Id] = entity;
        }
        LastSyncedAt = syncedAt;
    }

    public void UpsertOverlay(RuntimeOverlay overlay)
        => _overlays[overlay.IssueId] = overlay;

    public void ClearOverlay(string issueId)
        => _overlays.Remove(issueId);

    public CachedLinearEntity? GetEntity(string id)
        => _entities.TryGetValue(id, out var entity) ? entity : null;

    public RuntimeOverlay? GetOverlay(string id)
        => _overlays.TryGetValue(id, out var overlay) ? overlay : null;

    public bool IsExpired()
    {
        if (LastSyncedAt is not DateTimeOffset synced) return true;
        return DateTimeOffset.UtcNow - synced > _ttl;
    }

    public int EntityCount => _entities.Count;
    public int OverlayCount => _overlays.Count;

    public IEnumerable<KeyValuePair<string, CachedLinearEntity>> Entities => _entities;
    public IEnumerable<KeyValuePair<string, RuntimeOverlay>> Overlays => _overlays;

    public static string StateKindLabel(TrackerIssueStateKind kind)
    {
        return kind switch
        {
            TrackerIssueStateKind.BacklogKind => "backlog",
            TrackerIssueStateKind.UnstartedKind => "unstarted",
            TrackerIssueStateKind.StartedKind => "started",
            TrackerIssueStateKind.CompletedKind => "completed",
            TrackerIssueStateKind.CanceledKind => "canceled",
            TrackerIssueStateKind.TriageKind => "triage",
            TrackerIssueStateKind.UnknownKind => "unknown",
            _ => "unknown",
        };
    }

    private static CachedLinearEntity FromTrackerIssue(TrackerIssue issue)
    {
        return new CachedLinearEntity
        {
            Id = issue.Id,
            Identifier = issue.Identifier,
            Title = issue.Title,
            State = issue.State,
            StateKind = StateKindLabel(issue.StateKind),
            Priority = issue.Priority,
            Labels = issue.Labels,
            ParentId = issue.ParentId,
            ProjectMilestone = issue.ProjectMilestone is null ? null : new CachedMilestone
            {
                Id = issue.ProjectMilestone.Id,
                Name = issue.ProjectMilestone.Name,
            },
            BlockedBy = issue.BlockedBy.Select(b => new CachedBlockerRef
            {
                Id = b.Id,
                Identifier = b.Identifier,
                Title = b.Title,
                State = b.State.Name,
                IsTerminal = b.IsTerminal(),
            }).ToList(),
            SubIssues = issue.SubIssues.Select(s => new CachedIssueRef
            {
                Id = s.Id,
                Identifier = s.Identifier,
                State = s.State,
            }).ToList(),
            Url = issue.Url,
            CreatedAt = issue.CreatedAt,
            UpdatedAt = issue.UpdatedAt,
            SyncedAt = DateTimeOffset.UtcNow,
        };
    }
}
