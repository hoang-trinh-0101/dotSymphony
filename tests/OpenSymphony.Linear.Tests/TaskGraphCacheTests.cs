using OpenSymphony.Domain;
using OpenSymphony.Linear;

namespace OpenSymphony.Linear.Tests;

public class TaskGraphCacheTests
{
    private static TrackerIssue BuildTestIssue(string id, string identifier, string state)
    {
        return new TrackerIssue
        {
            Id = id,
            Identifier = identifier,
            Url = $"https://linear.app/test/issue/{identifier}",
            Title = $"Issue {identifier}",
            Description = null,
            Priority = 1,
            State = state,
            StateKind = TrackerIssueStateKindFromName(state),
            Labels = ["backend"],
            ParentId = null,
            Parent = null,
            ProjectMilestone = new TrackerProjectMilestone
            {
                Id = "ms-1",
                Name = "M1",
            },
            BlockedBy = new(),
            SubIssues = new(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static TrackerIssueStateKind TrackerIssueStateKindFromName(string state)
    {
        var lower = state.Trim().ToLowerInvariant();
        return lower switch
        {
            "backlog" => TrackerIssueStateKind.Backlog,
            "todo" => TrackerIssueStateKind.Unstarted,
            "in progress" or "review" or "human review" => TrackerIssueStateKind.Started,
            "done" or "completed" or "closed" => TrackerIssueStateKind.Completed,
            "canceled" or "cancelled" => TrackerIssueStateKind.Canceled,
            _ => TrackerIssueStateKind.Unknown(lower),
        };
    }

    [Fact]
    public void CacheUpsertEntitiesTracksSyncTimestamp()
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(300));
        var issues = new List<TrackerIssue> { BuildTestIssue("lin-1", "COE-1", "In Progress") };
        var syncedBefore = DateTimeOffset.UtcNow;
        cache.UpsertEntities(issues);
        var syncedAfter = DateTimeOffset.UtcNow;

        Assert.Equal(1, cache.EntityCount);
        var entity = cache.GetEntity("lin-1")!;
        Assert.Equal("COE-1", entity.Identifier);
        Assert.True(entity.SyncedAt >= syncedBefore);
        Assert.True(entity.SyncedAt <= syncedAfter);
        Assert.Equal(entity.SyncedAt, cache.LastSyncedAt);
    }

    [Fact]
    public void CacheUpsertOverlayByIssueId()
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(300));
        var overlay = new RuntimeOverlay
        {
            IssueId = "lin-1",
            Eligible = true,
            Queued = false,
            ActiveRunId = "run-1",
            LastOutcome = null,
            RetryCount = 0,
            WorkspaceId = null,
            ConversationId = null,
            LastEventAt = null,
            ValidationStatus = null,
            BlockerSummary = null,
            SyncedAt = DateTimeOffset.UtcNow,
        };
        cache.UpsertOverlay(overlay);

        Assert.Equal(1, cache.OverlayCount);
        var result = cache.GetOverlay("lin-1")!;
        Assert.True(result.Eligible);
    }

    [Fact]
    public void CacheClearOverlayRemovesEntry()
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(300));
        cache.UpsertOverlay(new RuntimeOverlay
        {
            IssueId = "lin-1",
            Eligible = true,
            Queued = false,
            ActiveRunId = "run-1",
            LastOutcome = null,
            RetryCount = 0,
            WorkspaceId = null,
            ConversationId = null,
            LastEventAt = null,
            ValidationStatus = null,
            BlockerSummary = null,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        cache.ClearOverlay("lin-1");
        Assert.Equal(0, cache.OverlayCount);
    }

    [Fact]
    public void CacheIsExpiredReturnsTrueWhenTtlPassed()
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(1));
        cache.UpsertEntities([BuildTestIssue("lin-1", "COE-1", "Todo")]);
        Assert.False(cache.IsExpired());

        // ht: force last_synced_at into the past to simulate TTL expiry.
        //   We can't directly set LastSyncedAt (private set), so we use reflection.
        var prop = typeof(TaskGraphCache).GetProperty("LastSyncedAt");
        prop!.SetValue(cache, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2));
        Assert.True(cache.IsExpired());
    }

    [Fact]
    public void CacheIsExpiredReturnsTrueWhenNeverSynced()
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(300));
        Assert.True(cache.IsExpired());
    }

    [Fact]
    public void StateKindLabelMapsStableKinds()
    {
        Assert.Equal("completed", TaskGraphCache.StateKindLabel(TrackerIssueStateKind.Completed));
        Assert.Equal("started", TaskGraphCache.StateKindLabel(TrackerIssueStateKind.Started));
        Assert.Equal("unstarted", TaskGraphCache.StateKindLabel(TrackerIssueStateKind.Unstarted));
        Assert.Equal("backlog", TaskGraphCache.StateKindLabel(TrackerIssueStateKind.Backlog));
        Assert.Equal("unknown", TaskGraphCache.StateKindLabel(TrackerIssueStateKind.Unknown("custom")));
    }

    [Fact]
    public void FromTrackerIssueConvertsMilestoneAndBlockers()
    {
        var issue = BuildTestIssue("lin-1", "COE-1", "Done");
        cache_upsert_entities_helper(issue, out var entity);
        Assert.NotNull(entity.ProjectMilestone);
        Assert.Equal("M1", entity.ProjectMilestone!.Name);
        Assert.Equal("completed", entity.StateKind);
    }

    [Fact]
    public void CacheEntitiesIteratorYieldsAll()
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(300));
        cache.UpsertEntities([
            BuildTestIssue("lin-1", "COE-1", "Todo"),
            BuildTestIssue("lin-2", "COE-2", "In Progress"),
        ]);
        var ids = cache.Entities.Select(kv => kv.Key).ToList();
        Assert.Contains("lin-1", ids);
        Assert.Contains("lin-2", ids);
    }

    // ht: helper to get a CachedLinearEntity from a TrackerIssue via the cache.
    private static void cache_upsert_entities_helper(TrackerIssue issue, out CachedLinearEntity entity)
    {
        var cache = new TaskGraphCache("default", TimeSpan.FromSeconds(300));
        cache.UpsertEntities([issue]);
        entity = cache.GetEntity(issue.Id)!;
    }
}
