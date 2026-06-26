using System.Text.Json.Serialization;

namespace OpenSymphony.Planning;

public sealed record MilestoneSummary(
    string MilestoneId,
    string MilestoneName,
    int IssueCount,
    int ActiveIssueCount,
    int CompletedIssueCount,
    int CanceledIssueCount);

public sealed record BlockerSnapshot(
    string BlockerId,
    string BlockerIdentifier,
    string BlockerTitle,
    string BlockerState,
    bool IsTerminal);

public sealed record BlockerChain(
    string IssueId,
    string IssueIdentifier,
    string IssueTitle,
    List<BlockerSnapshot> Blockers,
    bool IsResolved);

public sealed record IssueSnapshot(
    string Id,
    string Identifier,
    string Title,
    string State,
    byte? Priority);

public sealed record ChildRef(string Id, string Identifier, string State);

public sealed record ParentChildRelationship(
    string ParentId,
    string ParentIdentifier,
    List<ChildRef> Children);

public sealed record LinearGraphAnalysis(
    string ProjectName,
    string ProjectId,
    DateTime AnalyzedAt,
    int TotalIssues,
    SortedDictionary<string, int> IssuesByState,
    SortedDictionary<byte, int> IssuesByPriority,
    List<MilestoneSummary> Milestones,
    List<BlockerChain> BlockerChains,
    List<IssueSnapshot> UnblockedIssues,
    List<IssueSnapshot> BlockedIssues,
    List<IssueSnapshot> TerminalIssues,
    List<IssueSnapshot> ActiveIssues,
    SortedDictionary<string, int> LabelDistribution,
    List<ParentChildRelationship> ParentChildRelationships,
    string ConstraintsSummary);

public sealed class LinearGraphAnalyzer
{
    private readonly string _projectName;
    private readonly string _projectId;

    public LinearGraphAnalyzer(string projectName, string projectId)
    {
        _projectName = projectName;
        _projectId = projectId;
    }

    public LinearGraphAnalysis Analyze(IReadOnlyList<TrackerIssue> issues)
    {
        var analyzedAt = DateTime.UtcNow;

        var issuesByState = new SortedDictionary<string, int>();
        var issuesByPriority = new SortedDictionary<byte, int>();
        var labelCounts = new SortedDictionary<string, int>();

        var milestonesMap = new SortedDictionary<string, MilestoneSummary>();
        var unblocked = new List<IssueSnapshot>();
        var blocked = new List<IssueSnapshot>();
        var terminal = new List<IssueSnapshot>();
        var active = new List<IssueSnapshot>();
        var parentMap = new SortedDictionary<string, ParentChildRelationship>();

        foreach (var issue in issues)
        {
            var snapshot = new IssueSnapshot(issue.Id, issue.Identifier, issue.Title, issue.State, issue.Priority);

            // Count by state
            issuesByState.TryGetValue(issue.State, out var sc);
            issuesByState[issue.State] = sc + 1;

            // Count by priority (ht: skip null priorities — SortedDictionary rejects null keys)
            if (issue.Priority is { } priority)
            {
                issuesByPriority.TryGetValue(priority, out var pc);
                issuesByPriority[priority] = pc + 1;
            }

            // Count labels
            foreach (var label in issue.Labels)
            {
                labelCounts.TryGetValue(label, out var lc);
                labelCounts[label] = lc + 1;
            }

            // Milestone tracking
            if (issue.ProjectMilestone is { } milestone)
            {
                if (!milestonesMap.TryGetValue(milestone.Id, out var ms))
                {
                    ms = new MilestoneSummary(milestone.Id, milestone.Name, 0, 0, 0, 0);
                    milestonesMap[milestone.Id] = ms;
                }
                ms = ms with { IssueCount = ms.IssueCount + 1 };

                var issueStateKind = TrackerIssueStateKindExtensions.FromTrackerType(issue.State);
                if (issueStateKind.IsTerminal())
                {
                    ms = issueStateKind switch
                    {
                        TrackerIssueStateKind.Completed => ms with { CompletedIssueCount = ms.CompletedIssueCount + 1 },
                        TrackerIssueStateKind.Canceled => ms with { CanceledIssueCount = ms.CanceledIssueCount + 1 },
                        _ => ms,
                    };
                }
                else
                {
                    ms = ms with { ActiveIssueCount = ms.ActiveIssueCount + 1 };
                }
                milestonesMap[milestone.Id] = ms;
            }

            // Blocker tracking
            var hasActiveBlockers = issue.BlockedBy.Any(b => !b.IsTerminal());
            if (hasActiveBlockers)
                blocked.Add(snapshot);
            else
                unblocked.Add(snapshot);

            // Terminal vs active classification
            var isTerminal = TrackerIssueStateKindExtensions.FromTrackerType(issue.State).IsTerminal();
            if (isTerminal)
                terminal.Add(snapshot);
            else
                active.Add(snapshot);

            // Parent-child tracking
            if (issue.Parent is { } parent)
            {
                if (!parentMap.TryGetValue(parent.Id, out var parentRel))
                {
                    parentRel = new ParentChildRelationship(parent.Id, parent.Identifier, new List<ChildRef>());
                    parentMap[parent.Id] = parentRel;
                }
                parentRel.Children.Add(new ChildRef(issue.Id, issue.Identifier, issue.State));
            }
        }

        // Build blocker chains
        var blockerChains = issues
            .Where(i => i.BlockedBy.Count > 0)
            .Select(issue => new BlockerChain(
                issue.Id,
                issue.Identifier,
                issue.Title,
                issue.BlockedBy.Select(b => new BlockerSnapshot(b.Id, b.Identifier, b.Title, b.State, b.IsTerminal())).ToList(),
                issue.BlockedBy.All(b => b.IsTerminal())))
            .ToList();

        // Build constraints summary
        var constraintsSummary = BuildConstraintsSummary(issuesByState, blockerChains, milestonesMap);

        return new LinearGraphAnalysis(
            _projectName, _projectId, analyzedAt, issues.Count,
            issuesByState, issuesByPriority,
            milestonesMap.Values.ToList(),
            blockerChains, unblocked, blocked, terminal, active,
            labelCounts, parentMap.Values.ToList(), constraintsSummary);
    }

    private static string BuildConstraintsSummary(
        SortedDictionary<string, int> issuesByState,
        List<BlockerChain> blockerChains,
        SortedDictionary<string, MilestoneSummary> milestones)
    {
        var summary = new List<string>();

        var totalActiveBlockers = blockerChains.Count(bc => !bc.IsResolved);
        if (totalActiveBlockers > 0)
            summary.Add($"{totalActiveBlockers} issue(s) have unresolved blockers");

        var totalTerminal = issuesByState
            .Where(kv => TrackerIssueStateKindExtensions.FromTrackerType(kv.Key).IsTerminal())
            .Sum(kv => kv.Value);
        if (totalTerminal > 0)
            summary.Add($"{totalTerminal} terminal issue(s)");

        var milestoneCount = milestones.Count;
        if (milestoneCount > 0)
            summary.Add($"{milestoneCount} milestone(s) defined");

        return summary.Count == 0 ? "No active constraints detected" : string.Join("; ", summary);
    }
}
