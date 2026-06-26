using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using static OpenSymphony.Memory.Util;

namespace OpenSymphony.Memory;

public enum MemoryVisibility
{
    [YamlMember(Alias = "private")]
    Private,
    [YamlMember(Alias = "public")]
    Public,
}

public static class MemoryVisibilityExtensions
{
    public static string AsString(this MemoryVisibility v) => v == MemoryVisibility.Private ? "private" : "public";
    public static MemoryVisibility Default => MemoryVisibility.Private;
}

public enum KnowledgeScopeKind
{
    [YamlMember(Alias = "local_instance")] LocalInstance,
    [YamlMember(Alias = "organization")] Organization,
    [YamlMember(Alias = "project_set")] ProjectSet,
    [YamlMember(Alias = "project")] Project,
    [YamlMember(Alias = "milestone")] Milestone,
    [YamlMember(Alias = "work_item")] WorkItem,
    [YamlMember(Alias = "repository")] Repository,
    [YamlMember(Alias = "code_path")] CodePath,
    [YamlMember(Alias = "area")] Area,
}

public class KnowledgeScope
{
    public KnowledgeScopeKind Kind { get; set; }
    public string Id { get; set; } = "";
    public string? Label { get; set; }
}

public enum MemoryRecordKind
{
    [YamlMember(Alias = "issue_capsule")] IssueCapsule,
    [YamlMember(Alias = "topic_doc")] TopicDoc,
    [YamlMember(Alias = "code_context")] CodeContext,
    [YamlMember(Alias = "run_summary")] RunSummary,
}

public enum MemoryFreshness
{
    Current,
    Stale,
    Unknown,
}

public static class MemoryFreshnessExtensions
{
    public static string AsString(this MemoryFreshness f) => f switch
    {
        MemoryFreshness.Current => "current",
        MemoryFreshness.Stale => "stale",
        _ => "unknown",
    };
    public static MemoryFreshness Default => MemoryFreshness.Unknown;
}

public class MemorySourceRef
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Url { get; set; }
}

public class MemoryRecord
{
    public MemoryRecordKind Kind { get; set; }
    public List<KnowledgeScope> ScopeRefs { get; set; } = new();
    public List<MemorySourceRef> SourceRefs { get; set; } = new();
    public MemoryVisibility Visibility { get; set; }
    public string BodyRef { get; set; } = "";
    public DateTimeOffset? IndexedAt { get; set; }
    public MemoryFreshness Freshness { get; set; } = MemoryFreshness.Unknown;
}

public class ProviderStatus
{
    public string Provider { get; set; } = "";
    public bool Available { get; set; }
    public string? Detail { get; set; }
}

public class CodeIntelArtifact
{
    public string Provider { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<KnowledgeScope> ScopeRefs { get; set; } = new();
    public List<MemorySourceRef> SourceRefs { get; set; } = new();
    public string? Path { get; set; }
    public string? CommitSha { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
}

public class SearchResult
{
    public string IssueKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string CapsulePath { get; set; } = "";
    public List<string> Areas { get; set; } = new();
    public string Snippet { get; set; } = "";
}

public enum SourceSnapshotPolicy
{
    Disabled,
    Hashes,
    PrivateSnapshots,
}

public enum AreaStatus
{
    Candidate,
    Stable,
}

public static class AreaStatusExtensions
{
    public static string AsString(this AreaStatus s) => s == AreaStatus.Candidate ? "candidate" : "stable";
    public static AreaStatus Default => AreaStatus.Candidate;
}

public class AreaSourceRefs
{
    public List<string> Docs { get; set; } = new();
    public List<string> LinearLabels { get; set; } = new();
    public List<string> LinearMilestones { get; set; } = new();
    public List<string> LinearIssues { get; set; } = new();
    public List<string> GithubPrs { get; set; } = new();

    public bool IsEmpty() =>
        Docs.Count == 0 && LinearLabels.Count == 0 && LinearMilestones.Count == 0 &&
        LinearIssues.Count == 0 && GithubPrs.Count == 0;
}

public class RedactionConfig
{
    public List<string> DenyPatterns { get; set; } = new();
}

public class DocsConfig
{
    public string PublicRoot { get; set; } = "";
    public MemoryVisibility DefaultVisibility { get; set; } = MemoryVisibility.Public;
    public bool DenyPrivateLinks { get; set; } = true;
}

public class AreaConfig
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string DocsTarget { get; set; } = "";
    public MemoryVisibility Visibility { get; set; }
    public AreaStatus Status { get; set; } = AreaStatus.Candidate;
    public byte Confidence { get; set; }
    public List<string> Aliases { get; set; } = new();
    public AreaSourceRefs SourceRefs { get; set; } = new();
}

public class MemoryConfig
{
    public bool Enabled { get; set; } = true;
    public string ConfigPath { get; set; } = "";
    public string RepoRoot { get; set; } = "";
    public string MemoryRoot { get; set; } = "";
    public MemoryVisibility Visibility { get; set; } = MemoryVisibility.Private;
    public string IndexPath { get; set; } = "";
    public byte ConfidenceThreshold { get; set; } = 75;
    public SourceSnapshotPolicy SourceSnapshotPolicy { get; set; } = SourceSnapshotPolicy.Hashes;
    public bool MarkdownIndexes { get; set; } = true;
    public DocsConfig Docs { get; set; } = new();
    public SortedDictionary<string, AreaConfig> Areas { get; set; } = new();
    public RedactionConfig Redaction { get; set; } = new();

    public string IssueCapsulePath(string issueKey) =>
        Path.Combine(MemoryRoot, "issues", SanitizeIssueKey(issueKey) + ".md");

    public AreaConfig AreaOrDefault(string slug)
    {
        slug = Slugify(slug);
        if (Areas.TryGetValue(slug, out var area))
            return CloneArea(area);
        return new AreaConfig
        {
            Slug = slug,
            Title = TitleizeSlug(slug),
            DocsTarget = Path.Combine(Docs.PublicRoot, slug + ".md"),
            Visibility = Docs.DefaultVisibility,
            Status = AreaStatus.Candidate,
            Confidence = 0,
            Aliases = new(),
            SourceRefs = new(),
        };
    }

    private static AreaConfig CloneArea(AreaConfig a) => new()
    {
        Slug = a.Slug,
        Title = a.Title,
        DocsTarget = a.DocsTarget,
        Visibility = a.Visibility,
        Status = a.Status,
        Confidence = a.Confidence,
        Aliases = new List<string>(a.Aliases),
        SourceRefs = new AreaSourceRefs
        {
            Docs = new(a.SourceRefs.Docs),
            LinearLabels = new(a.SourceRefs.LinearLabels),
            LinearMilestones = new(a.SourceRefs.LinearMilestones),
            LinearIssues = new(a.SourceRefs.LinearIssues),
            GithubPrs = new(a.SourceRefs.GithubPrs),
        },
    };
}

public class MemoryInitPlan
{
    public string ConfigPath { get; set; } = "";
    public string ConfigContents { get; set; } = "";
    public string GitignorePath { get; set; } = "";
    public string? GitignoreBefore { get; set; }
    public string GitignoreAfter { get; set; } = "";
}

public enum MemoryInitFileChange
{
    Created,
    Updated,
    Unchanged,
}

public class MemoryInitApplyReport
{
    public string ConfigPath { get; set; } = "";
    public MemoryInitFileChange Config { get; set; }
    public string GitignorePath { get; set; } = "";
    public MemoryInitFileChange Gitignore { get; set; }
}

public class MemoryScopeFilter
{
    public string? ProjectSet { get; set; }
    public string? Project { get; set; }
    public string? Milestone { get; set; }
    public string? Issue { get; set; }
    public string? Repo { get; set; }
    public string? Area { get; set; }
    public bool AllAccessible { get; set; }
}

public class StatusReport
{
    public int IssueCount { get; set; }
    public int WarningCount { get; set; }
    public int DocsPendingCount { get; set; }
    public List<StatusIssue> Issues { get; set; } = new();
}

public class StatusIssue
{
    public string IssueKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string? State { get; set; }
    public string? Milestone { get; set; }
    public string CapsulePath { get; set; } = "";
    public MemoryVisibility Visibility { get; set; }
    public List<string> Areas { get; set; } = new();
    public string DocsSyncStatus { get; set; } = "";
    public int WarningCount { get; set; }
}

public class MemoryReindexReport
{
    public int IssueCount { get; set; }
    public string IndexPath { get; set; } = "";
    public List<string> MarkdownIndexes { get; set; } = new();
    public int WarningCount { get; set; }
}

public class LintReport
{
    public List<LintFinding> Findings { get; set; } = new();
}

public class LintFinding
{
    public LintSeverity Severity { get; set; }
    public string? Path { get; set; }
    public string Message { get; set; } = "";
    public string? NextCommand { get; set; }
}

public enum LintSeverity
{
    Info,
    Warn,
    Error,
}

public enum LintCode
{
    OkfPrivateMemoryLink,
}

// Evidence types
public class SourceFile
{
    public List<IssueEvidence> Issues { get; set; } = new();
    public List<PullRequestEvidence> Prs { get; set; } = new();
    public SortedDictionary<string, IssueOverride> Overrides { get; set; } = new();
}

public class IssueEvidence
{
    public string? Id { get; set; }
    public string Identifier { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? Description { get; set; }
    public string? State { get; set; }
    public string? Milestone { get; set; }
    public string? MilestoneId { get; set; }
    public IssueLinkEvidence? Parent { get; set; }
    public List<IssueLinkEvidence> Children { get; set; } = new();
    public List<IssueLinkEvidence> BlockedBy { get; set; } = new();
    public List<string> Labels { get; set; } = new();
    public List<CommentEvidence> Comments { get; set; } = new();
    public List<ulong> LinkedPrs { get; set; } = new();
    public List<string> TaskFiles { get; set; } = new();
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class IssueLinkEvidence
{
    public string? Id { get; set; }
    public string Identifier { get; set; } = "";
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? State { get; set; }
}

public class CommentEvidence
{
    public string? Id { get; set; }
    public string? Author { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Source { get; set; }
}

public class PullRequestEvidence
{
    public ulong Number { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? Branch { get; set; }
    public string? Body { get; set; }
    public string? MergeSha { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public List<CommitEvidence> Commits { get; set; } = new();
    public List<ChangedFileEvidence> ChangedFiles { get; set; } = new();
    public List<CheckEvidence> Checks { get; set; } = new();
    public List<ReviewEvidence> Reviews { get; set; } = new();
}

public class CommitEvidence
{
    public string Sha { get; set; } = "";
    public string? Author { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public string Summary { get; set; } = "";
}

public class ChangedFileEvidence
{
    public string Path { get; set; } = "";
    public string? ChangeKind { get; set; }
}

public class CheckEvidence
{
    public string Name { get; set; } = "";
    public string? Conclusion { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class ReviewEvidence
{
    public string? Reviewer { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? Disposition { get; set; }
}

public class IssueOverride
{
    public List<ulong> Prs { get; set; } = new();
    public List<string> Areas { get; set; } = new();
}

public class IssueSelection
{
    public List<string> Identifiers { get; set; } = new();
    public string? Milestone { get; set; }
    public string? State { get; set; }
    public DateOnly? BeforeDate { get; set; }
    public string? BeforeIssue { get; set; }
    public string? Area { get; set; }
    public bool SinceLastSync { get; set; }
}

public class CapturePlan
{
    public bool Write { get; set; }
    public List<CaptureIssuePlan> Selected { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class CaptureIssuePlan
{
    public IssueEvidence Issue { get; set; } = new();
    public List<PullRequestEvidence> Prs { get; set; } = new();
    public string CapsulePath { get; set; } = "";
    public List<string> Areas { get; set; } = new();
    public List<string> DocsTargets { get; set; } = new();
    public string SourceHash { get; set; } = "";
    public bool AlreadyCaptured { get; set; }
    public bool Stale { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class CaptureWriteReport
{
    public List<string> WrittenCapsules { get; set; } = new();
    public string IndexPath { get; set; } = "";
    public List<string> MarkdownIndexes { get; set; } = new();
    public List<string> MilestoneNodes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class DocsSyncPlan
{
    public bool Write { get; set; }
    public List<string> SelectedIssueKeys { get; set; } = new();
    public List<DocsTargetPlan> Targets { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class DocsTargetPlan
{
    public string Area { get; set; } = "";
    public string Title { get; set; } = "";
    public string Path { get; set; } = "";
    public MemoryVisibility Visibility { get; set; }
    public bool Create { get; set; }
    public string? Before { get; set; }
    public string After { get; set; } = "";
    public string Diff { get; set; } = "";
    public List<string> IssueKeys { get; set; } = new();
}

public class ArchivePlan
{
    public bool Write { get; set; }
    public bool Force { get; set; }
    public List<ArchiveIssuePlan> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ArchiveIssuePlan
{
    public string IssueKey { get; set; } = "";
    public bool Eligible { get; set; }
    public string Reason { get; set; } = "";
    public string? CapsulePath { get; set; }
}

public class IndexedIssue
{
    public string IssueKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string? State { get; set; }
    public string? Milestone { get; set; }
    public List<string> Labels { get; set; } = new();
    public List<string> Areas { get; set; } = new();
    public string CapsulePath { get; set; } = "";
    public MemoryVisibility Visibility { get; set; }
    public string SourceHash { get; set; } = "";
    public int WarningCount { get; set; }
    public string DocsSyncStatus { get; set; } = "";
    public string? CompletionTime { get; set; }
    public string CapturedAt { get; set; } = "";
    public List<string> ChangedFiles { get; set; } = new();
    public string Body { get; set; } = "";

    // OKF extension fields
    public string ConceptId { get; set; } = "";
    public string ConceptType { get; set; } = "issue-capsule";
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<KnowledgeScope> ScopeRefs { get; set; } = new();
    public List<MemorySourceRef> SourceRefs { get; set; } = new();
    public List<OkfLink> Links { get; set; } = new();
    public List<OkfCitation> Citations { get; set; } = new();
    public MemoryFreshness Freshness { get; set; } = MemoryFreshness.Unknown;
    public List<string> Warnings { get; set; } = new();
}
