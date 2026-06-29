using OpenSymphony.Domain;

namespace OpenSymphony.Orchestrator;

// ht: Port of older/crates/opensymphony-orchestrator/src/selection.rs.
//   TrackerIssue.BlockedBy and SubIssues use the Domain TrackerIssueBlocker/TrackerIssueRef
//   types which expose IsTerminal() / IsTerminal(HashSet<string>) respectively.
public static class Selection
{
    public static bool IssueBlockedByNonTerminalBlockers(TrackerIssue issue) =>
        issue.BlockedBy.Any(b => !b.IsTerminal());

    public static bool ParentIssueBlockedByIncompleteChildren(TrackerIssue issue, HashSet<string> terminalStates) =>
        issue.SubIssues.Count > 0 && issue.SubIssues.Any(s => !s.IsTerminal(terminalStates));

    public static bool ShouldDispatchIssue(TrackerIssue issue, HashSet<string> terminalStates) =>
        !IssueBlockedByNonTerminalBlockers(issue) &&
        !ParentIssueBlockedByIncompleteChildren(issue, terminalStates);

    public static List<TrackerIssue> FilterIssuesForDispatch(
        IEnumerable<TrackerIssue> issues, HashSet<string> terminalStates)
    {
        var filtered = issues.Where(i => ShouldDispatchIssue(i, terminalStates)).ToList();
        SortIssuesForDispatch(filtered);
        return filtered;
    }

    public static void SortIssuesForDispatch(List<TrackerIssue> issues)
    {
        issues.Sort((left, right) =>
        {
            var c = PriorityRank(left).CompareTo(PriorityRank(right));
            if (c != 0) return c;
            c = left.SubIssues.Count.CompareTo(right.SubIssues.Count);
            if (c != 0) return c;
            c = left.CreatedAt.CompareTo(right.CreatedAt);
            if (c != 0) return c;
            return string.Compare(left.Identifier, right.Identifier, StringComparison.Ordinal);
        });
    }

    static byte PriorityRank(TrackerIssue issue) => issue.Priority ?? byte.MaxValue;
}
