using System.Text.Json;

namespace OpenSymphony.Memory;

/// <summary>
/// In-memory index store that replaces DuckDB. Stores indexed issues, areas,
/// changed files, doc sync runs, and doc memory links.
/// </summary>
public class MemoryIndexStore
{
    public Dictionary<string, IndexedIssueRow> Issues { get; } = new();
    public List<IssueAreaRow> IssueAreas { get; } = new();
    public List<PullRequestRow> PullRequests { get; } = new();
    public List<ChangedFileRow> ChangedFiles { get; } = new();
    public List<CheckRow> Checks { get; } = new();
    public List<ReviewRow> Reviews { get; } = new();
    public Dictionary<string, AreaRow> Areas { get; } = new();
    public List<DocSyncRunRow> DocSyncRuns { get; } = new();
    public List<DocMemoryLinkRow> DocMemoryLinks { get; } = new();
    public int SchemaVersion { get; set; }

    public void Migrate()
    {
        SchemaVersion = MemoryConstants.MemorySchemaVersion;
        // Ensure all OKF columns exist with defaults on existing rows
        foreach (var row in Issues.Values)
        {
            row.ConceptId ??= "";
            row.ConceptType ??= "issue-capsule";
            row.TagsJson ??= "[]";
            row.ScopeRefsJson ??= "[]";
            row.SourceRefsJson ??= "[]";
            row.LinksJson ??= "[]";
            row.CitationsJson ??= "[]";
            row.Freshness ??= "unknown";
            row.WarningsJson ??= "[]";
        }
    }

    public void ClearAll()
    {
        Issues.Clear();
        IssueAreas.Clear();
        PullRequests.Clear();
        ChangedFiles.Clear();
        Checks.Clear();
        Reviews.Clear();
        Areas.Clear();
        // Note: DocSyncRuns and DocMemoryLinks are NOT cleared by ClearAll
        // to match the Rust behavior where reindex only clears specific tables
    }

    public void ClearReindexTables()
    {
        Issues.Clear();
        IssueAreas.Clear();
        PullRequests.Clear();
        ChangedFiles.Clear();
        Checks.Clear();
        Reviews.Clear();
        Areas.Clear();
    }

    public List<IndexedIssue> LoadIndexedIssues()
    {
        var result = new List<IndexedIssue>();
        foreach (var row in Issues.Values.OrderBy(r => r.IssueKey))
        {
            var labels = JsonSerializer.Deserialize<List<string>>(row.LabelsJson ?? "[]") ?? new();
            var areas = IssueAreas.Where(a => a.IssueKey == row.IssueKey).Select(a => a.Area).OrderBy(a => a).ToList();
            var changedFiles = ChangedFiles.Where(c => c.IssueKey == row.IssueKey).Select(c => c.FilePath).OrderBy(p => p).ToList();
            result.Add(new IndexedIssue
            {
                IssueKey = row.IssueKey,
                Title = row.Title,
                State = row.State,
                Milestone = row.Milestone,
                Labels = labels,
                Areas = areas,
                CapsulePath = row.CapsulePath,
                Visibility = row.Visibility == "public" ? MemoryVisibility.Public : MemoryVisibility.Private,
                SourceHash = row.SourceHash,
                WarningCount = row.WarningCount,
                DocsSyncStatus = row.DocsSyncStatus,
                CompletionTime = row.CompletionTime,
                CapturedAt = row.CapturedAt,
                ChangedFiles = changedFiles,
                Body = row.Body,
                ConceptId = row.ConceptId ?? "",
                ConceptType = row.ConceptType ?? "issue-capsule",
                Description = row.Description,
                Tags = JsonSerializer.Deserialize<List<string>>(row.TagsJson ?? "[]") ?? new(),
                ScopeRefs = JsonSerializer.Deserialize<List<KnowledgeScope>>(row.ScopeRefsJson ?? "[]") ?? new(),
                SourceRefs = JsonSerializer.Deserialize<List<MemorySourceRef>>(row.SourceRefsJson ?? "[]") ?? new(),
                Links = JsonSerializer.Deserialize<List<OkfLink>>(row.LinksJson ?? "[]") ?? new(),
                Citations = JsonSerializer.Deserialize<List<OkfCitation>>(row.CitationsJson ?? "[]") ?? new(),
                Freshness = (row.Freshness ?? "unknown") switch
                {
                    "current" => MemoryFreshness.Current,
                    "stale" => MemoryFreshness.Stale,
                    _ => MemoryFreshness.Unknown,
                },
                Warnings = JsonSerializer.Deserialize<List<string>>(row.WarningsJson ?? "[]") ?? new(),
            });
        }
        return result;
    }

    public IndexedIssue? FindIndexedIssue(string issueKey)
    {
        issueKey = Util.NormalizeIssueKey(issueKey);
        return LoadIndexedIssues().FirstOrDefault(i => i.IssueKey == issueKey);
    }
}

public class IndexedIssueRow
{
    public string IssueKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string? State { get; set; }
    public string? Milestone { get; set; }
    public string LabelsJson { get; set; } = "[]";
    public string? CompletionTime { get; set; }
    public string ArchiveStatus { get; set; } = "not_archived";
    public string CapsulePath { get; set; } = "";
    public string Visibility { get; set; } = "private";
    public string SourceHash { get; set; } = "";
    public int WarningCount { get; set; }
    public string DocsSyncStatus { get; set; } = "pending";
    public string Body { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    // OKF extension columns
    public string? ConceptId { get; set; } = "";
    public string? ConceptType { get; set; } = "issue-capsule";
    public string? Description { get; set; }
    public string? TagsJson { get; set; } = "[]";
    public string? ScopeRefsJson { get; set; } = "[]";
    public string? SourceRefsJson { get; set; } = "[]";
    public string? LinksJson { get; set; } = "[]";
    public string? CitationsJson { get; set; } = "[]";
    public string? Freshness { get; set; } = "unknown";
    public string? WarningsJson { get; set; } = "[]";
}

public class IssueAreaRow
{
    public string IssueKey { get; set; } = "";
    public string Area { get; set; } = "";
}

public class PullRequestRow
{
    public string IssueKey { get; set; } = "";
    public long Number { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? Branch { get; set; }
    public string? MergeSha { get; set; }
    public string? MergedAt { get; set; }
}

public class ChangedFileRow
{
    public string IssueKey { get; set; } = "";
    public long PrNumber { get; set; }
    public string FilePath { get; set; } = "";
    public string? ChangeKind { get; set; }
}

public class CheckRow
{
    public string IssueKey { get; set; } = "";
    public long PrNumber { get; set; }
    public string Name { get; set; } = "";
    public string? Conclusion { get; set; }
    public string? CompletedAt { get; set; }
}

public class ReviewRow
{
    public string IssueKey { get; set; } = "";
    public long PrNumber { get; set; }
    public string? Reviewer { get; set; }
    public string? State { get; set; }
    public string? SubmittedAt { get; set; }
    public string? Disposition { get; set; }
}

public class AreaRow
{
    public string Area { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DocsTarget { get; set; } = "";
}

public class DocSyncRunRow
{
    public string RunId { get; set; } = "";
    public string SelectedIssuesJson { get; set; } = "[]";
    public string TargetDocsJson { get; set; } = "[]";
    public string GeneratedAt { get; set; } = "";
    public string Status { get; set; } = "";
}

public class DocMemoryLinkRow
{
    public string TopicDoc { get; set; } = "";
    public string IssueKey { get; set; } = "";
    public string Visibility { get; set; } = "";
}
