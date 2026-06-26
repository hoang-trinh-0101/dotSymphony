using System.Text.Json;
using OpenSymphony.Planning;

namespace OpenSymphony.Planning.Tests;

public class LinearGraphTests
{
    private static TrackerIssue MakeIssue(string id, string identifier, string title, string state, byte? priority) =>
        new(id, identifier, $"https://linear.app/test/issue/{identifier}", title, null, priority, state,
            new List<string> { "backend" }, null, null,
            new TrackerProjectMilestone("ms-1", "M1"),
            new List<TrackerIssueBlocker>(), new List<TrackerIssueRef>(), DateTime.UtcNow, DateTime.UtcNow);

    private static TrackerIssueBlocker MakeBlocker(string id, string identifier, string title, string stateName, bool isTerminal) =>
        new(id, identifier, title, stateName,
            isTerminal ? TrackerIssueStateKind.Completed : TrackerIssueStateKind.Started);

    [Fact]
    public void AnalyzeCountsIssuesByState()
    {
        var issues = new List<TrackerIssue>
        {
            MakeIssue("1", "COE-1", "Issue 1", "Todo", 1),
            MakeIssue("2", "COE-2", "Issue 2", "In Progress", 2),
            MakeIssue("3", "COE-3", "Issue 3", "In Progress", null),
            MakeIssue("4", "COE-4", "Issue 4", "Completed", 1),
        };
        var analyzer = new LinearGraphAnalyzer("TestProject", "proj-1");
        var analysis = analyzer.Analyze(issues);
        Assert.Equal(4, analysis.TotalIssues);
        Assert.Equal(1, analysis.IssuesByState["Todo"]);
        Assert.Equal(2, analysis.IssuesByState["In Progress"]);
        Assert.Equal(1, analysis.IssuesByState["Completed"]);
    }

    [Fact]
    public void AnalyzeTracksBlockerChains()
    {
        var issue = MakeIssue("1", "COE-1", "Blocked Issue", "Todo", 1);
        var blockers = new List<TrackerIssueBlocker>
        {
            MakeBlocker("b1", "COE-0", "Active Blocker", "In Progress", false),
            MakeBlocker("b2", "COE-01", "Completed Blocker", "Done", true),
        };
        var issueWithBlockers = issue with { BlockedBy = blockers };
        var analyzer = new LinearGraphAnalyzer("TestProject", "proj-1");
        var analysis = analyzer.Analyze(new List<TrackerIssue> { issueWithBlockers });
        Assert.Single(analysis.BlockerChains);
        Assert.False(analysis.BlockerChains[0].IsResolved);
        Assert.Equal(2, analysis.BlockerChains[0].Blockers.Count);
        Assert.Contains(analysis.BlockedIssues, i => i.Identifier == "COE-1");
    }

    [Fact]
    public void AnalyzeTracksMilestones()
    {
        var issues = new List<TrackerIssue>
        {
            MakeIssue("1", "COE-1", "Issue 1", "Todo", 1) with { ProjectMilestone = new TrackerProjectMilestone("ms-1", "M1") },
            MakeIssue("2", "COE-2", "Issue 2", "Completed", 2) with { ProjectMilestone = new TrackerProjectMilestone("ms-2", "M2") },
        };
        var analyzer = new LinearGraphAnalyzer("TestProject", "proj-1");
        var analysis = analyzer.Analyze(issues);
        Assert.Equal(2, analysis.Milestones.Count);
        var m1 = analysis.Milestones.Find(m => m.MilestoneName == "M1")!;
        Assert.Equal(1, m1.IssueCount);
        var m2 = analysis.Milestones.Find(m => m.MilestoneName == "M2")!;
        Assert.Equal(1, m2.CompletedIssueCount);
    }

    [Fact]
    public void AnalyzeTracksParentChildRelationships()
    {
        var issues = new List<TrackerIssue>
        {
            MakeIssue("1", "COE-1", "Parent", "Todo", 1),
            MakeIssue("2", "COE-2", "Child 1", "Completed", 2) with
            {
                ParentId = "1",
                Parent = new TrackerIssueRef("1", "COE-1", "Parent", null, "Todo"),
            },
            MakeIssue("3", "COE-3", "Child 2", "In Progress", null) with
            {
                ParentId = "1",
                Parent = new TrackerIssueRef("1", "COE-1", "Parent", null, "Todo"),
            },
        };
        var analyzer = new LinearGraphAnalyzer("TestProject", "proj-1");
        var analysis = analyzer.Analyze(issues);
        Assert.Single(analysis.ParentChildRelationships);
        Assert.Equal(2, analysis.ParentChildRelationships[0].Children.Count);
    }

    [Fact]
    public void AnalyzeSerializesToJson()
    {
        var issues = new List<TrackerIssue> { MakeIssue("1", "COE-1", "Test Issue", "Todo", 1) };
        var analyzer = new LinearGraphAnalyzer("TestProject", "proj-1");
        var analysis = analyzer.Analyze(issues);
        var json = JsonSerializer.Serialize(analysis);
        Assert.Contains("TestProject", json);
        Assert.Contains("COE-1", json);
        var deserialized = JsonSerializer.Deserialize<LinearGraphAnalysis>(json)!;
        Assert.Equal(analysis.ProjectName, deserialized.ProjectName);
        Assert.Equal(analysis.TotalIssues, deserialized.TotalIssues);
    }

    [Fact]
    public void AnalyzeEmptyIssuesetReturnsZeroCounts()
    {
        var analyzer = new LinearGraphAnalyzer("EmptyProject", "proj-0");
        var analysis = analyzer.Analyze(new List<TrackerIssue>());
        Assert.Equal(0, analysis.TotalIssues);
        Assert.Empty(analysis.IssuesByState);
        Assert.Empty(analysis.Milestones);
        Assert.Empty(analysis.BlockerChains);
        Assert.Equal("No active constraints detected", analysis.ConstraintsSummary);
    }

    [Fact]
    public void AnalyzeClassifiesTerminalVsActiveIssues()
    {
        var issues = new List<TrackerIssue>
        {
            MakeIssue("1", "COE-1", "Active", "In Progress", 1),
            MakeIssue("2", "COE-2", "Done", "Completed", 2),
            MakeIssue("3", "COE-3", "Canceled", "Canceled", null),
        };
        var analyzer = new LinearGraphAnalyzer("TestProject", "proj-1");
        var analysis = analyzer.Analyze(issues);
        Assert.Single(analysis.ActiveIssues);
        Assert.Equal(2, analysis.TerminalIssues.Count);
    }
}
