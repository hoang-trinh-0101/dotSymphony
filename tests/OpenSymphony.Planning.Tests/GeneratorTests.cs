using System.Collections.Generic;
using System.Text.Json;
using OpenSymphony.Planning.Generator;
using OpenSymphony.Domain;

namespace OpenSymphony.Planning.Tests;

public class GeneratorTests
{
    private static PlanningSession MakeSampleSession() =>
        new(new IntakeContext("test-wave", "Test project for unit testing",
            new List<string> { "All tests pass" },
            new List<string> { "Feature A", "Feature B" },
            new List<string> { "Must use Rust" },
            new List<string>(), new List<string>()), "docs/tasks");

    [Fact]
    public void YamlEscapeRoundTripsYamlIndicatorCharacters()
    {
        var raw = "? &anchor *alias # comment-ish\nquoted \"value\"\tcontrol\u0007";
        var yaml = $"title: \"{PlanGenerator.YamlEscape(raw)}\"\n";
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        var parsed = deserializer.Deserialize<Dictionary<string, string>>(yaml);
        Assert.Equal(raw, parsed["title"]);
    }

    [Fact]
    public void GeneratorProducesMilestonesWithIssuesAndSubissues()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var artifacts = generator.Generate().Value;
        Assert.NotEmpty(artifacts.Milestones);
        var totalIssues = artifacts.Milestones.Sum(m => m.Issues.Count);
        Assert.True(totalIssues > 0);
        foreach (var milestone in artifacts.Milestones)
            foreach (var issue in milestone.Issues)
                Assert.NotEmpty(issue.SubIssues);
    }

    [Fact]
    public void GeneratorProducesValidManifest()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var artifacts = generator.Generate().Value;
        Assert.Equal("test-wave", artifacts.Manifest.PlanningWave);
        Assert.Equal("docs/tasks", artifacts.Manifest.TasksDir);
        Assert.NotEmpty(artifacts.Manifest.Milestones);
        Assert.NotEmpty(artifacts.Manifest.Tasks);
        foreach (var milestoneName in artifacts.Manifest.Milestones)
            Assert.Contains(artifacts.Milestones, m => m.Name == milestoneName);
    }

    [Fact]
    public void GeneratorProducesMilestoneIndex()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var artifacts = generator.Generate().Value;
        Assert.Contains("# Project Milestones", artifacts.MilestoneIndex);
        foreach (var milestone in artifacts.Milestones)
            Assert.Contains(milestone.Name, artifacts.MilestoneIndex);
    }

    [Fact]
    public void GeneratorProducesTaskFiles()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var artifacts = generator.Generate().Value;
        Assert.NotEmpty(artifacts.TaskFiles);
        foreach (var milestone in artifacts.Milestones)
            foreach (var issue in milestone.Issues)
            {
                Assert.True(artifacts.TaskFiles.ContainsKey(issue.Id));
                foreach (var sub in issue.SubIssues)
                    Assert.True(artifacts.TaskFiles.ContainsKey(sub.Id));
            }
    }

    [Fact]
    public void GeneratorFailsWithoutRequirements()
    {
        var session = MakeSampleSession();
        session.Intake = session.Intake with { Requirements = new List<string>() };
        var generator = new PlanGenerator(session);
        var result = generator.Generate();
        Assert.True(result.IsErr);
        Assert.Equal(GenerationErrorKind.IncompleteSession, result.Error.Kind);
        Assert.Equal("requirements", result.Error.Message);
    }

    [Fact]
    public void RegenerationPreservesUnselectedArtifacts()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var original = generator.Generate().Value;
        var regenerated = generator.Regenerate(original, new RegenerationScope.Manifest()).Value;
        Assert.Equal(original.Milestones.Count, regenerated.Milestones.Count);
        Assert.Equal(original.MilestoneIndex, regenerated.MilestoneIndex);
        Assert.Equal(original.TaskFiles.Count, regenerated.TaskFiles.Count);
    }

    [Fact]
    public void RegenerationWithUnscopedIssuesRegeneratesAllIssues()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var original = generator.Generate().Value;
        var updatedSession = new PlanningSession(
            new IntakeContext("test-wave", "Test project for unit testing",
                new List<string> { "All tests pass" },
                new List<string> { "Feature C" },
                new List<string> { "Must use Rust" },
                new List<string>(), new List<string>()), "docs/tasks");
        var gen2 = new PlanGenerator(updatedSession);
        var regenerated = gen2.Regenerate(original, new RegenerationScope.Issues(null)).Value;
        var milestone = regenerated.Milestones[0];
        Assert.Single(milestone.Issues);
        Assert.Equal("Feature C", milestone.Issues[0].Title);
        Assert.NotEqual(original.MilestoneIndex, regenerated.MilestoneIndex);
        Assert.True(regenerated.TaskFiles.ContainsKey(milestone.Issues[0].Id));
        Assert.NotEqual(milestone.Id, milestone.Issues[0].Id);
    }

    [Fact]
    public void RegenerationWithUnscopedSubIssuesRegeneratesAllSubIssues()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var original = generator.Generate().Value;
        var originalIssue = original.Milestones[0].Issues[0];
        var originalSubIds = originalIssue.SubIssues.Select(s => s.Id).ToList();

        var updatedSession = MakeSampleSession();
        updatedSession.Intake = updatedSession.Intake with
        {
            Constraints = updatedSession.Intake.Constraints.Append("Must include operator evidence").ToList(),
        };
        var gen2 = new PlanGenerator(updatedSession);
        var regenerated = gen2.Regenerate(original, new RegenerationScope.SubIssues(null)).Value;
        var regIssue = regenerated.Milestones[0].Issues[0];
        var regSubIds = regIssue.SubIssues.Select(s => s.Id).ToList();
        Assert.Equal(originalIssue.Id, regIssue.Id);
        Assert.NotEqual(originalSubIds, regSubIds);
        Assert.Contains(regIssue.SubIssues[0].Context, entry => entry.Contains("Must include operator evidence"));
        Assert.True(regenerated.TaskFiles.ContainsKey(regIssue.SubIssues[0].Id));
    }

    [Fact]
    public void DependencyGraphValidationPassesForValidGraph()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var artifacts = generator.Generate().Value;
        Assert.True(DependencyGraphValidator.ValidateDependencyGraph(artifacts).IsOk);
    }

    [Fact]
    public void TaskIdsAreUnique()
    {
        var generator = new PlanGenerator(MakeSampleSession());
        var artifacts = generator.Generate().Value;
        var allIds = new HashSet<string>();
        var totalExpected = 0;
        foreach (var milestone in artifacts.Milestones)
        {
            totalExpected++;
            Assert.True(allIds.Add(milestone.Id.Value));
            foreach (var issue in milestone.Issues)
            {
                totalExpected++;
                Assert.True(allIds.Add(issue.Id.Value));
                foreach (var sub in issue.SubIssues)
                {
                    totalExpected++;
                    Assert.True(allIds.Add(sub.Id.Value));
                }
            }
        }
        Assert.Equal(totalExpected, allIds.Count);
        foreach (var task in artifacts.Manifest.Tasks)
            Assert.Contains(task.Id.Value, allIds);
    }

    private static PlanArtifacts CycleArtifacts(bool useBlocksOnly = false)
    {
        var a = new TaskId("TASK-001");
        var b = new TaskId("TASK-002");
        var c = new TaskId("TASK-003");
        PlannedIssue MakeIssue(TaskId id, List<TaskId> blockedBy, List<TaskId> blocks) => new(
            id, $"Task {id}", "S", new List<string>(), new List<string>(), new List<string>(),
            new List<AcceptanceCriterion>(), new List<string>(), new List<string>(), new List<string>(),
            null, TaskPriority.Normal, null, blockedBy, blocks, new List<PlannedSubIssue>(), null);
        var issues = new List<PlannedIssue>
        {
            MakeIssue(a, new List<TaskId> { c }, useBlocksOnly ? new List<TaskId> { b } : new List<TaskId>()),
            MakeIssue(b, new List<TaskId> { a }, useBlocksOnly ? new List<TaskId> { c } : new List<TaskId>()),
            MakeIssue(c, new List<TaskId> { b }, new List<TaskId>()),
        };
        return new PlanArtifacts(DateTime.UtcNow, "test",
            new List<PlannedMilestone> { new(new TaskId("MS-1"), "M1: Test", "Test goal", issues,
                new List<AcceptanceCriterion>(), new List<string>(), null) },
            new TaskPackageManifest("test", "docs/tasks", new List<string> { "M1: Test" },
                new List<ManifestTask>
                {
                    new(a, "docs/tasks/a.md"), new(b, "docs/tasks/b.md"), new(c, "docs/tasks/c.md"),
                }),
            "", new SortedDictionary<TaskId, string>());
    }

    [Fact]
    public void DependencyGraphValidationDetectsCycle()
    {
        var result = DependencyGraphValidator.ValidateDependencyGraph(CycleArtifacts());
        Assert.True(result.IsErr);
        Assert.Equal(GenerationErrorKind.CircularDependency, result.Error.Kind);
        Assert.Contains("Cycle", result.Error.Message);
    }

    [Fact]
    public void DependencyGraphValidationDetectsDeepCycle()
    {
        var result = DependencyGraphValidator.ValidateDependencyGraph(CycleArtifacts(useBlocksOnly: true));
        Assert.True(result.IsErr);
        Assert.Contains("Cycle", result.Error.Message);
    }

    [Fact]
    public void DependencyGraphValidationDetectsBlocksOnlyCycle()
    {
        var a = new TaskId("TASK-001");
        var b = new TaskId("TASK-002");
        var c = new TaskId("TASK-003");
        PlannedIssue MakeIssue(TaskId id, List<TaskId> blocks) => new(
            id, $"Task {id}", "S", new List<string>(), new List<string>(), new List<string>(),
            new List<AcceptanceCriterion>(), new List<string>(), new List<string>(), new List<string>(),
            null, TaskPriority.Normal, null, new List<TaskId>(), blocks, new List<PlannedSubIssue>(), null);
        var artifacts = new PlanArtifacts(DateTime.UtcNow, "test",
            new List<PlannedMilestone> { new(new TaskId("MS-1"), "M1: Test", "Test goal",
                new List<PlannedIssue>
                {
                    MakeIssue(a, new List<TaskId> { b }),
                    MakeIssue(b, new List<TaskId> { c }),
                    MakeIssue(c, new List<TaskId> { a }),
                },
                new List<AcceptanceCriterion>(), new List<string>(), null) },
            new TaskPackageManifest("test", "docs/tasks", new List<string> { "M1: Test" }, new List<ManifestTask>()),
            "", new SortedDictionary<TaskId, string>());
        Assert.True(DependencyGraphValidator.ValidateDependencyGraph(artifacts).IsErr);
    }

    // Session tests
    private static IntakeContext MakeSampleIntake() =>
        new("test-wave", "Test project for planning",
            new List<string> { "All tests pass" },
            new List<string> { "Feature A", "Feature B" },
            new List<string> { "Must use Rust" },
            new List<string> { "How to handle auth?" },
            new List<string> { "docs/architecture.md" });

    [Fact]
    public void PlanningSessionStartsWithEmptyAnalyses()
    {
        var session = new PlanningSession(MakeSampleIntake(), "docs/tasks");
        Assert.Null(session.CodebaseAnalysis);
        Assert.Null(session.LinearGraphAnalysis);
        Assert.Null(session.Research);
        Assert.False(session.IsComplete());
    }

    [Fact]
    public void PlanningSessionCanBeCompletedWithAllAnalyses()
    {
        var session = new PlanningSession(MakeSampleIntake(), "docs/tasks")
            .WithCodebaseAnalysis(new CodebaseAnalysis(".", new List<LanguageSignature>(), new List<PackageInfo>(),
                new List<string>(), new List<OwnershipSignal>(), new List<IntegrationPoint>(),
                new List<Convention>(), new List<AnalysisRisk>(), 0, 0, 0))
            .WithLinearGraphAnalysis(new LinearGraphAnalysis("Test", "test-1", DateTime.UtcNow, 0,
                new SortedDictionary<string, int>(), new SortedDictionary<byte, int>(),
                new List<MilestoneSummary>(), new List<BlockerChain>(),
                new List<IssueSnapshot>(), new List<IssueSnapshot>(),
                new List<IssueSnapshot>(), new List<IssueSnapshot>(),
                new SortedDictionary<string, int>(), new List<ParentChildRelationship>(), "None"))
            .WithResearch(new ResearchArtifactStore());
        Assert.True(session.IsComplete());
        Assert.Contains("test-wave", session.ContextSummary());
    }

    [Fact]
    public void ContextSummaryIncludesAllSections()
    {
        var session = new PlanningSession(MakeSampleIntake(), "docs/tasks");
        var summary = session.ContextSummary();
        Assert.Contains("Planning wave", summary);
        Assert.Contains("Requirements", summary);
        Assert.Contains("Constraints", summary);
        Assert.Contains("Codebase analysis", summary);
        Assert.Contains("Linear graph analysis", summary);
        Assert.Contains("Research artifacts", summary);
    }
}
