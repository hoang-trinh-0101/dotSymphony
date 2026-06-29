using OpenSymphony.Domain;
using OpenSymphony.Orchestrator;

namespace OpenSymphony.Orchestrator.Tests;

public class SelectionTests
{
    static HashSet<string> TerminalStates() => new(StringComparer.Ordinal) { "Done", "Canceled" };

    static TrackerIssueState State(string name, TrackerIssueStateKind kind) => new()
    {
        Id = $"state-{name.ToLowerInvariant().Replace(' ', '-')}",
        Name = name,
        TrackerType = kind.Label,
        Kind = kind,
    };

    static TrackerIssueBlocker Blocker(string identifier, TrackerIssueState state) => new()
    {
        Id = $"issue-{identifier.ToLowerInvariant()}",
        Identifier = identifier,
        Title = $"Issue {identifier}",
        State = state,
    };

    static TrackerIssueRef Child(string identifier, string state) => new()
    {
        Id = $"issue-{identifier.ToLowerInvariant()}",
        Identifier = identifier,
        Title = null,
        Url = null,
        State = state,
    };

    static TrackerIssue Issue(
        string identifier, byte? priority, string createdAt,
        List<TrackerIssueBlocker> blockedBy, List<TrackerIssueRef> subIssues) => new()
    {
        Id = $"issue-{identifier.ToLowerInvariant()}",
        Identifier = identifier,
        Url = $"https://linear.app/example/{identifier}",
        Title = $"Issue {identifier}",
        Description = null,
        Priority = priority,
        State = "In Progress",
        StateKind = TrackerIssueStateKind.Started,
        Labels = new(),
        ParentId = null,
        Parent = null,
        ProjectMilestone = null,
        BlockedBy = blockedBy,
        SubIssues = subIssues,
        CreatedAt = DateTimeOffset.Parse(createdAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTimeOffset.Parse(createdAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    [Fact]
    public void ParentIssueIsBlockedWhenAnyChildIsNonTerminal()
    {
        var issue = Issue("COE-277", 1, "2026-03-22T00:00:00Z",
            new(), new() { Child("COE-278", "In Progress"), Child("COE-279", "Done") });

        Assert.True(Selection.ParentIssueBlockedByIncompleteChildren(issue, TerminalStates()));
    }

    [Fact]
    public void ParentIssueIsReadyWhenAllChildrenAreTerminal()
    {
        var issue = Issue("COE-277", 1, "2026-03-22T00:00:00Z",
            new(), new() { Child("COE-278", "Done"), Child("COE-279", "Canceled") });

        Assert.False(Selection.ParentIssueBlockedByIncompleteChildren(issue, TerminalStates()));
    }

    [Fact]
    public void BlockerCheckComposesWithHierarchyCheck()
    {
        var issue = Issue("COE-277", 1, "2026-03-22T00:00:00Z",
            new() { Blocker("COE-260", State("In Progress", TrackerIssueStateKind.Started)) },
            new() { Child("COE-278", "Done") });

        Assert.True(Selection.IssueBlockedByNonTerminalBlockers(issue));
        Assert.False(Selection.ShouldDispatchIssue(issue, TerminalStates()));
    }

    [Fact]
    public void SortPrefersLeafIssuesBeforeParentsWhenPrioritiesMatch()
    {
        var issues = new List<TrackerIssue>
        {
            Issue("COE-277", 1, "2026-03-20T00:00:00Z", new(), new() { Child("COE-278", "Done") }),
            Issue("COE-278", 1, "2026-03-21T00:00:00Z", new(), new()),
        };

        Selection.SortIssuesForDispatch(issues);

        Assert.Equal(new[] { "COE-278", "COE-277" }, issues.Select(i => i.Identifier).ToArray());
    }

    [Fact]
    public void FilterSkipsParentUntilChildrenFinish()
    {
        var issues = new List<TrackerIssue>
        {
            Issue("COE-277", 1, "2026-03-20T00:00:00Z", new(), new() { Child("COE-278", "In Progress") }),
            Issue("COE-278", 1, "2026-03-21T00:00:00Z", new(), new()),
        };

        var filtered = Selection.FilterIssuesForDispatch(issues, TerminalStates());

        Assert.Single(filtered);
        Assert.Equal("COE-278", filtered[0].Identifier);
    }

    [Fact]
    public void NestedHierarchyDispatchesOnlyTheLeafIssue()
    {
        var issues = new List<TrackerIssue>
        {
            Issue("COE-P1", 1, "2026-03-20T00:00:00Z", new(), new() { Child("COE-S1", "In Progress") }),
            Issue("COE-S1", 1, "2026-03-21T00:00:00Z", new(), new() { Child("COE-SS1", "In Progress") }),
            Issue("COE-SS1", 1, "2026-03-22T00:00:00Z", new(), new()),
        };

        var filtered = Selection.FilterIssuesForDispatch(issues, TerminalStates());

        Assert.Single(filtered);
        Assert.Equal("COE-SS1", filtered[0].Identifier);
    }

    [Fact]
    public void AddingANewChildReblocksTheParentOnTheNextSnapshot()
    {
        var terminalStates = TerminalStates();
        var parent = Issue("COE-277", 1, "2026-03-20T00:00:00Z", new(), new());

        Assert.True(Selection.ShouldDispatchIssue(parent, terminalStates));

        parent.SubIssues.Add(Child("COE-278", "Todo"));

        Assert.False(Selection.ShouldDispatchIssue(parent, terminalStates));
    }
}
