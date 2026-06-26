using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Planning;

// ht: Minimal domain types mirroring opensymphony_domain::TrackerIssue so that
// opensymphony-planning can be built standalone without a circular dependency.
// The real domain types are used when compiled via the root crate.

public enum TrackerIssueStateKind
{
    Backlog,
    Unstarted,
    Started,
    Completed,
    Canceled,
    Triage,
    Unknown,
}

public static class TrackerIssueStateKindExtensions
{
    public static TrackerIssueStateKind FromTrackerType(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "backlog" => TrackerIssueStateKind.Backlog,
            "unstarted" => TrackerIssueStateKind.Unstarted,
            "started" => TrackerIssueStateKind.Started,
            "completed" => TrackerIssueStateKind.Completed,
            "canceled" => TrackerIssueStateKind.Canceled,
            "triage" or "triaged" => TrackerIssueStateKind.Triage,
            _ => TrackerIssueStateKind.Unknown,
        };
    }

    public static bool IsTerminal(this TrackerIssueStateKind kind) =>
        kind == TrackerIssueStateKind.Completed || kind == TrackerIssueStateKind.Canceled;
}

public sealed record TrackerProjectMilestone(string Id, string Name);

public sealed record TrackerIssueRef(
    string Id,
    string Identifier,
    string? Title,
    string? Url,
    string State);

public sealed record TrackerIssueBlocker(
    string Id,
    string Identifier,
    string Title,
    string State,
    TrackerIssueStateKind? StateKind)
{
    public bool IsTerminal()
    {
        if (StateKind is { } kind)
            return kind.IsTerminal();
        return TrackerIssueStateKindExtensions.FromTrackerType(State).IsTerminal();
    }
}

public sealed record TrackerIssue(
    string Id,
    string Identifier,
    string Url,
    string Title,
    string? Description,
    byte? Priority,
    string State,
    List<string> Labels,
    string? ParentId,
    TrackerIssueRef? Parent,
    TrackerProjectMilestone? ProjectMilestone,
    List<TrackerIssueBlocker> BlockedBy,
    List<TrackerIssueRef> SubIssues,
    DateTime CreatedAt,
    DateTime UpdatedAt);
