using OpenSymphony.Domain;

namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/normalize.rs.

internal static class Normalize
{
    private const ulong LinearMaxPriority = 4;

    public static TrackerIssue NormalizeIssue(LinearIssueNode node)
    {
        var state = NormalizeState(node.State);
        return new TrackerIssue
        {
            Id = node.Id,
            Identifier = node.Identifier,
            Url = node.Url,
            Title = node.Title,
            Description = node.Description,
            Priority = NormalizePriority(node.Priority),
            State = state.Name,
            StateKind = state.Kind,
            Labels = NormalizeLabels(node.Labels.Nodes),
            ParentId = NormalizeParentId(node.Parent),
            Parent = NormalizeParent(node.Parent),
            ProjectMilestone = NormalizeProjectMilestone(node.ProjectMilestone),
            BlockedBy = NormalizeBlockers(node.InverseRelations.Nodes),
            SubIssues = NormalizeSubIssues(node.Children.Nodes),
            CreatedAt = node.CreatedAt,
            UpdatedAt = node.UpdatedAt,
        };
    }

    public static TrackerIssueStateSnapshot NormalizeIssueState(LinearIssueStateNode node)
    {
        return new TrackerIssueStateSnapshot
        {
            Id = node.Id,
            Identifier = node.Identifier,
            State = NormalizeState(node.State),
            UpdatedAt = node.UpdatedAt,
        };
    }

    private static TrackerIssueState NormalizeState(LinearWorkflowState state)
    {
        return new TrackerIssueState
        {
            Id = state.Id,
            Name = state.Name,
            TrackerType = state.Kind,
            Kind = TrackerIssueStateKind.FromTrackerType(state.Kind),
        };
    }

    private static List<string> NormalizeLabels(List<LinearLabelNode> labels)
    {
        var result = labels.Select(l => l.Name).ToList();
        result.Sort(StringComparer.Ordinal);
        return result.Distinct().ToList();
    }

    private static List<TrackerIssueBlocker> NormalizeBlockers(List<LinearRelationNode> relations)
    {
        var blockers = relations
            .Where(r => r.RelationType == "blocks")
            .Select(r => NormalizeBlocker(r.Issue))
            .ToList();
        blockers.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        // dedup by id
        var seen = new HashSet<string>();
        return blockers.Where(b => seen.Add(b.Id)).ToList();
    }

    private static TrackerIssueBlocker NormalizeBlocker(LinearBlockerNode blocker)
    {
        return new TrackerIssueBlocker
        {
            Id = blocker.Id,
            Identifier = blocker.Identifier,
            Title = blocker.Title,
            State = NormalizeState(blocker.State),
        };
    }

    private static string? NormalizeParentId(LinearParentNode? parent)
        => parent?.Id;

    private static TrackerIssueRef? NormalizeParent(LinearParentNode? parent)
    {
        if (parent is null) return null;
        if (parent.Identifier is null) return null;
        return new TrackerIssueRef
        {
            Id = parent.Id,
            Identifier = parent.Identifier,
            Title = parent.Title,
            Url = parent.Url,
            State = parent.State?.Name ?? "unknown",
        };
    }

    private static TrackerProjectMilestone? NormalizeProjectMilestone(LinearProjectMilestoneNode? milestone)
    {
        if (milestone is null) return null;
        return new TrackerProjectMilestone
        {
            Id = milestone.Id,
            Name = milestone.Name,
        };
    }

    private static List<TrackerIssueRef> NormalizeSubIssues(List<LinearChildNode> children)
    {
        var subIssues = children.Select(child => new TrackerIssueRef
        {
            Id = child.Id,
            Identifier = child.Identifier,
            Title = child.Title,
            Url = child.Url,
            State = child.State.Name,
        }).ToList();
        subIssues.Sort((a, b) => string.Compare(a.Identifier, b.Identifier, StringComparison.Ordinal));
        // dedup by id
        var seen = new HashSet<string>();
        return subIssues.Where(s => seen.Add(s.Id)).ToList();
    }

    // ht: Rust normalize_priority returns Result<Option<u8>, LinearError>.
    //   C# throws LinearError on invalid input.
    internal static byte? NormalizePriority(double priority)
    {
        if (!double.IsFinite(priority) || priority < 0.0)
            throw LinearError.InvalidResponse($"Linear priority must be a finite non-negative number, got {priority}");

        var rounded = Math.Truncate(priority);
        if (Math.Abs(priority - rounded) > double.Epsilon)
            throw LinearError.InvalidResponse($"Linear priority must be an integer value, got {priority}");

        var value = (ulong)rounded;
        if (value == 0) return null;
        if (value <= LinearMaxPriority) return (byte)value;
        throw LinearError.InvalidResponse($"Linear priority must be between 0 and {LinearMaxPriority}, got {value}");
    }
}
