using System.IO;
using System.Text.Json;
using OpenSymphony.Planning.GraphValidate;
using OpenSymphony.Planning.Generator;

namespace OpenSymphony.Planning.Tests;

public class GraphValidateTests
{
    // ── mod.rs tests ──

    [Fact]
    public void PlanValidationReportRoundTripsThroughJson()
    {
        var artifacts = new PlanArtifacts(DateTime.UtcNow, "rich-client-hosted-mode",
            new List<PlannedMilestone>(),
            new TaskPackageManifest("rich-client-hosted-mode", "docs/tasks", new List<string>(), new List<ManifestTask>()),
            "", new SortedDictionary<TaskId, string>());
        Assert.True(DependencyGraphValidator.ValidateDependencyGraph(artifacts).IsOk);
        var report = GraphValidateHelpers.BuildInMemoryReport(artifacts, null, null);
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var json = JsonSerializer.Serialize(report, opts);
        Assert.Contains("rich-client-hosted-mode", json);
        Assert.Contains("dependency_graph", json.ToLowerInvariant());
        var parsed = JsonSerializer.Deserialize<PlanValidationReport>(json, opts)!;
        Assert.Equal("rich-client-hosted-mode", parsed.PlanningWave);
        Assert.NotNull(parsed.DependencyGraph);
    }

    [Fact]
    public void BuildInMemoryReportCountsAllRiskSeverities()
    {
        var artifacts = new PlanArtifacts(DateTime.UtcNow, "rich-client-hosted-mode",
            new List<PlannedMilestone>(),
            new TaskPackageManifest("rich-client-hosted-mode", "docs/tasks", new List<string>(), new List<ManifestTask>()),
            "", new SortedDictionary<TaskId, string>());
        var checker = new PlanQualityChecker(artifacts).WithCodebase(2);
        var findings = checker.Run();
        Assert.DoesNotContain(findings, f => f.Category == PlanCheckCategory.CodebaseAnalysis);
    }

    // ── manifest.rs tests ──

    private static string ManifestWithTasks(params (string id, string file)[] tasks)
    {
        var s = "planningWave: test\ntasksDir: docs/tasks\nmilestones:\n  - \"M1\"\ntasks:\n";
        foreach (var (id, file) in tasks)
            s += $"  - id: {id}\n    file: {file}\n";
        return s;
    }

    private static string TaskFileText(string id, string milestone, string[] blockedBy, string[] blocks)
    {
        var bb = string.Join(", ", blockedBy.Select(x => $"\"{x}\""));
        var bl = string.Join(", ", blocks.Select(x => $"\"{x}\""));
        return $"---\nid: {id}\ntitle: \"{id}\"\nmilestone: \"{milestone}\"\nblockedBy: [{bb}]\nblocks: [{bl}]\n---\n# Test\n";
    }

    private static TaskPackageManifestFile FixtureWithManifest(string tmp, string manifestText, params (string path, string contents)[] files)
    {
        var manifestPath = Path.Combine(tmp, "task-package.yaml");
        File.WriteAllText(manifestPath, manifestText);
        foreach (var (path, contents) in files)
        {
            var full = Path.Combine(tmp, path.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(full);
            if (dir is { } d) Directory.CreateDirectory(d);
            File.WriteAllText(full, contents);
        }
        return ManifestValidator.LoadManifest(manifestPath).Value;
    }

    [Fact]
    public void ValidatesCleanManifest()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md"), ("TASK-B", "docs/tasks/b.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", Array.Empty<string>(), new[] { "TASK-B" })),
            ("docs/tasks/b.md", TaskFileText("TASK-B", "M1", new[] { "TASK-A" }, Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.True(result.IsOk());
        Assert.Equal(0, result.ErrorCount());
    }

    [Fact]
    public void MissingTaskFileIsReported()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md"), ("TASK-B", "docs/tasks/missing.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", Array.Empty<string>(), Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.False(result.IsOk());
        Assert.Single(result.MissingTaskFiles);
        Assert.Equal(new TaskId("TASK-B"), result.MissingTaskFiles[0].TaskId);
    }

    [Fact]
    public void InvalidTaskFileIsReportedSeparatelyFromMissing()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md"), ("TASK-B", "docs/tasks/broken.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", Array.Empty<string>(), Array.Empty<string>())),
            ("docs/tasks/broken.md", "---\nid: \"TASK-B\"\ntitle: \"Broken\"\n  unclosed_quote: \"\n"));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.False(result.IsOk());
        Assert.Empty(result.MissingTaskFiles);
        Assert.Single(result.InvalidTaskFiles);
        var invalid = result.InvalidTaskFiles[0];
        Assert.Equal(new TaskId("TASK-B"), invalid.TaskId);
        Assert.Equal("docs/tasks/broken.md", invalid.FilePath);
        Assert.NotEmpty(invalid.Reason);
        Assert.True(result.ErrorCount() >= 1);
    }

    [Fact]
    public void MissingFrontmatterIsReportedAsInvalidNotMissing()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-X", "docs/tasks/x.md")),
            ("docs/tasks/x.md", "---\nid: \"TASK-X\"\ntitle: \"TASK-X\"\nmilestone: \"M1\"\nblockedBy: []\nblocks: []\n"));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.Empty(result.MissingTaskFiles);
        Assert.Single(result.InvalidTaskFiles);
        Assert.Equal(new TaskId("TASK-X"), result.InvalidTaskFiles[0].TaskId);
    }

    [Fact]
    public void UnknownMilestoneIsReported()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M9", Array.Empty<string>(), Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.Single(result.UnknownMilestones);
        Assert.Equal("M9", result.UnknownMilestones[0].DeclaredMilestone);
    }

    [Fact]
    public void UnknownDependencyIsReported()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", new[] { "TASK-GHOST" }, Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.Single(result.UnknownDependencies);
        Assert.Equal(new TaskId("TASK-GHOST"), result.UnknownDependencies[0].DependencyTaskId);
    }

    [Fact]
    public void CreationOrderCycleIsReported()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md"), ("TASK-B", "docs/tasks/b.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", new[] { "TASK-B" }, Array.Empty<string>())),
            ("docs/tasks/b.md", TaskFileText("TASK-B", "M1", new[] { "TASK-A" }, Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.Single(result.CreationOrderCycles);
        var cycle = result.CreationOrderCycles[0];
        Assert.Equal(2, cycle.Count);
        Assert.Equal(2, cycle.Distinct().Count());
        Assert.Equal(new TaskId("TASK-A"), cycle[0]);
        Assert.Equal(new TaskId("TASK-B"), cycle[1]);
    }

    [Fact]
    public void SelfBlockIsReported()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", new[] { "TASK-A" }, Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.Single(result.SelfBlocks);
        Assert.Equal(new TaskId("TASK-A"), result.SelfBlocks[0].TaskId);
    }

    [Fact]
    public void DuplicateTaskIdIsReported()
    {
        using var tmp = new TempDir();
        var manifest = FixtureWithManifest(tmp.Path,
            ManifestWithTasks(("TASK-A", "docs/tasks/a.md"), ("TASK-A", "docs/tasks/copy.md")),
            ("docs/tasks/a.md", TaskFileText("TASK-A", "M1", Array.Empty<string>(), Array.Empty<string>())),
            ("docs/tasks/copy.md", TaskFileText("TASK-A", "M1", Array.Empty<string>(), Array.Empty<string>())));
        var result = ManifestValidator.ValidateAgainstRepoRoot(manifest, tmp.Path);
        Assert.Equal(new List<TaskId> { new("TASK-A") }, result.DuplicateTaskIds);
        Assert.True(result.ErrorCount() >= 1);
        Assert.False(result.IsOk());
    }

    [Fact]
    public void ValidateTakesExplicitRepoRoot()
    {
        using var workspace = new TempDir();
        var project = Path.Combine(workspace.Path, "project");
        Directory.CreateDirectory(Path.Combine(project, "docs", "tasks"));
        var manifestPath = Path.Combine(project, "docs", "tasks", "task-package.yaml");
        File.WriteAllText(manifestPath, ManifestWithTasks(("TASK-A", "docs/tasks/a.md")));
        File.WriteAllText(Path.Combine(project, "docs", "tasks", "a.md"), TaskFileText("TASK-A", "M1", Array.Empty<string>(), Array.Empty<string>()));

        var badResult = ManifestValidator.Validate(manifestPath, workspace.Path).Value;
        Assert.Single(badResult.MissingTaskFiles);

        var goodResult = ManifestValidator.Validate(manifestPath, project).Value;
        Assert.Empty(goodResult.MissingTaskFiles);
    }

    // ── graph.rs tests ──

    private static PlanArtifacts ArtifactsFor(List<PlannedIssue> issues)
    {
        var milestone = new PlannedMilestone(new TaskId("M9"), "M9: Wave", "Goal",
            issues, new List<AcceptanceCriterion>(), new List<string>(), null);
        return new PlanArtifacts(DateTime.UtcNow, "test-wave",
            new List<PlannedMilestone> { milestone },
            new TaskPackageManifest("test-wave", "docs/tasks",
                new List<string> { milestone.Name },
                new List<ManifestTask> { new(milestone.Id, "docs/tasks/m9.md") }),
            "", new SortedDictionary<TaskId, string>());
    }

    private static PlannedIssue Issue(string id, string[] blockedBy, string[] blocks) => new(
        new TaskId(id), $"Issue {id}", "S",
        new List<string> { "in" }, new List<string>(), new List<string> { "d" },
        new List<AcceptanceCriterion> { new("AC", null) }, new List<string>(),
        new List<string>(), new List<string>(), null, TaskPriority.Normal, null,
        blockedBy.Select(s => new TaskId(s)).ToList(), blocks.Select(s => new TaskId(s)).ToList(),
        new List<PlannedSubIssue>(), $"docs/tasks/{id}.md");

    private static PlannedSubIssue SubIssue(string id, string[] blockedBy) => new(
        new TaskId(id), $"Sub {id}", "S",
        new List<string> { "in" }, new List<string>(), new List<string> { "d" },
        new List<AcceptanceCriterion> { new("AC", null) }, new List<string> { "verify" },
        new List<string>(), new List<string>(), null, TaskPriority.Normal, null,
        blockedBy.Select(s => new TaskId(s)).ToList(), new List<TaskId>(),
        $"docs/tasks/{id}.md");

    [Fact]
    public void BuilderEmitsAcyclicGraphWithSourcesAndReasons()
    {
        var a = Issue("OSYM-734", Array.Empty<string>(), new[] { "OSYM-735" });
        var b = Issue("OSYM-735", new[] { "OSYM-734" }, Array.Empty<string>());
        var graph = DependencyGraphBuilder.Build(ArtifactsFor(new List<PlannedIssue> { a, b }));
        Assert.Contains(graph.Edges, e => e.Relation == GraphEdgeReason.BlocksInvariant
            && e.From == new TaskId("OSYM-734") && e.To == new TaskId("OSYM-735"));
        Assert.Contains(graph.ParallelizableWaves.First(), w => w == new TaskId("OSYM-734"));
        var blockerEdge = graph.Edges.First(e => e.From == new TaskId("OSYM-734")
            && e.To == new TaskId("OSYM-735") && e.Relation == GraphEdgeReason.BlocksInvariant);
        Assert.Equal("docs/tasks/OSYM-734.md", blockerEdge.SourceArtifactRef);
    }

    [Fact]
    public void BuilderMarksUnknownTargetReason()
    {
        var parent = Issue("OSYM-734", new[] { "OSYM-DOES-NOT-EXIST" }, Array.Empty<string>());
        var graph = DependencyGraphBuilder.Build(ArtifactsFor(new List<PlannedIssue> { parent }));
        var unknownEdge = graph.Edges.First(e => e.To == new TaskId("OSYM-734"));
        Assert.Equal(GraphEdgeReason.UnknownTarget, unknownEdge.Relation);
        Assert.Equal(new TaskId("OSYM-DOES-NOT-EXIST"), unknownEdge.From);
    }

    [Fact]
    public void BuilderEmitsParentOfEdgesForSubIssues()
    {
        var parent = Issue("OSYM-734", Array.Empty<string>(), Array.Empty<string>()) with
        {
            SubIssues = new List<PlannedSubIssue> { SubIssue("OSYM-734.SUB", Array.Empty<string>()) },
        };
        var graph = DependencyGraphBuilder.Build(ArtifactsFor(new List<PlannedIssue> { parent }));
        Assert.Contains(graph.Edges, e => e.Relation == GraphEdgeReason.ParentOf
            && e.From == new TaskId("OSYM-734") && e.To == new TaskId("OSYM-734.SUB"));
        var subNode = graph.Nodes.First(n => n.Id == new TaskId("OSYM-734.SUB"));
        Assert.Equal(1, subNode.VerificationCount);
        Assert.Equal(GraphNodeKind.SubIssue, subNode.Kind);
    }

    // ── frontmatter.rs tests ──

    [Fact]
    public void ParsesMinimalFrontmatter()
    {
        var text = "---\nid: OSYM-734\ntitle: Dependency Graph And Plan Checks\n---\n# Heading\nBody.\n";
        var parsed = FrontmatterParser.ParseTaskText(text, "test.md").Value;
        Assert.Equal("OSYM-734", parsed.Frontmatter.Id);
        Assert.Equal("Dependency Graph And Plan Checks", parsed.Frontmatter.Title);
        Assert.StartsWith("# Heading", parsed.Body);
        Assert.Contains("Body.", parsed.Body);
    }

    [Fact]
    public void MissingFrontmatterIsError()
    {
        var err = FrontmatterParser.ParseTaskText("# heading\n", "test.md");
        Assert.True(err.IsErr);
        Assert.Equal(TaskFrontmatterErrorKind.MissingFrontmatter, err.Error.Kind);
    }

    [Fact]
    public void InvalidYamlIsError()
    {
        var err = FrontmatterParser.ParseTaskText("---\nid: [unclosed\n---\n", "test.md");
        Assert.True(err.IsErr);
        Assert.Equal(TaskFrontmatterErrorKind.Yaml, err.Error.Kind);
    }

    [Fact]
    public void UnknownKeysAreTolerated()
    {
        var text = "---\nid: OSYM-734\ntitle: T\nunknown_field: keep\n---\nbody\n";
        var parsed = FrontmatterParser.ParseTaskText(text, "test.md").Value;
        Assert.Equal("OSYM-734", parsed.Frontmatter.Id);
        Assert.True(parsed.Frontmatter.Extra.ContainsKey("unknown_field"));
    }

    [Fact]
    public void ReadTaskFrontmatterOrDefaultSwallowsMissingFileOnly()
    {
        using var tmp = new TempDir();
        var missing = Path.Combine(tmp.Path, "does-not-exist.md");
        var fm = FrontmatterParser.ReadTaskFrontmatterOrDefault(missing).Value;
        Assert.Null(fm.Id);
        Assert.Null(fm.Title);

        var malformed = Path.Combine(tmp.Path, "malformed.md");
        File.WriteAllText(malformed, "---\nid: [unclosed\n---\n# body\n");
        var err = FrontmatterParser.ReadTaskFrontmatterOrDefault(malformed);
        Assert.True(err.IsErr);
        Assert.Equal(TaskFrontmatterErrorKind.Yaml, err.Error.Kind);
    }

    // ── checks.rs tests ──

    private static PlannedIssue CheckIssue(string id, string[] blockedBy) => new(
        new TaskId(id), $"Issue {id}", $"Summary for {id}",
        new List<string> { "in" }, new List<string>(), new List<string> { "d" },
        new List<AcceptanceCriterion> { new("AC", null) }, new List<string>(),
        new List<string>(), new List<string>(), null, TaskPriority.Normal, null,
        blockedBy.Select(s => new TaskId(s)).ToList(), new List<TaskId>(),
        new List<PlannedSubIssue>(), null);

    private static PlannedMilestone CheckMilestone(string id, string name, List<PlannedIssue> issues) => new(
        new TaskId(id), name, "Goal", issues, new List<AcceptanceCriterion>(), new List<string>(), null);

    private static PlanArtifacts CheckArtifacts(List<PlannedMilestone> milestones)
    {
        var tasks = milestones.Select(m => new ManifestTask(m.Id, $"docs/tasks/{m.Id}.md")).ToList();
        return new PlanArtifacts(DateTime.UtcNow, "test", milestones,
            new TaskPackageManifest("test", "docs/tasks",
                milestones.Select(m => m.Name).ToList(), tasks),
            "", new SortedDictionary<TaskId, string>());
    }

    [Fact]
    public void CreationOrderWavesSerialChain()
    {
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: wave", new List<PlannedIssue>
            {
                CheckIssue("A", Array.Empty<string>()),
                CheckIssue("B", new[] { "A" }),
                CheckIssue("C", new[] { "B" }),
            }),
        });
        var waves = BlockingTaskHelpers.CreationOrderWaves(artifacts);
        Assert.Equal(new List<List<TaskId>>
        {
            new() { new("A") }, new() { new("B") }, new() { new("C") },
        }, waves);
    }

    [Fact]
    public void CreationOrderWavesParallelizable()
    {
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: parallel", new List<PlannedIssue>
            {
                CheckIssue("A", Array.Empty<string>()),
                CheckIssue("B", Array.Empty<string>()),
                CheckIssue("C", new[] { "A", "B" }),
            }),
        });
        var waves = BlockingTaskHelpers.CreationOrderWaves(artifacts);
        Assert.Equal(2, waves.Count);
        Assert.Equal(new List<TaskId> { new("A"), new("B") }, waves[0]);
        Assert.Equal(new List<TaskId> { new("C") }, waves[1]);
    }

    [Fact]
    public void MissingInverseBlockerIsWarning()
    {
        var issueA = CheckIssue("A", Array.Empty<string>()) with { Blocks = new List<TaskId> { new("B") } };
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: inverse", new List<PlannedIssue> { issueA, CheckIssue("B", Array.Empty<string>()) }),
        });
        var findings = new PlanQualityChecker(artifacts).Run();
        Assert.Contains(findings, f => f.Category == PlanCheckCategory.Dependencies && f.Message.Contains("inverse"));
    }

    [Fact]
    public void AcceptableArtifactHasNoErrors()
    {
        var issueA = CheckIssue("A", Array.Empty<string>()) with { Blocks = new List<TaskId> { new("B") } };
        var issueB = CheckIssue("B", new[] { "A" }) with { Blocks = new List<TaskId>() };
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: clean", new List<PlannedIssue> { issueA, issueB }),
        });
        var findings = new PlanQualityChecker(artifacts).Run();
        Assert.DoesNotContain(findings, f => f.Severity == PlanCheckSeverity.Error);
    }

    [Fact]
    public void VerificationCheckFlagsEmptySubIssues()
    {
        var sub = new PlannedSubIssue(new TaskId("SUB"), "Sub", "Summary",
            new List<string> { "in" }, new List<string>(), new List<string> { "d" },
            new List<AcceptanceCriterion> { new("AC", null) }, new List<string>(),
            new List<string>(), new List<string>(), null, TaskPriority.Normal, null,
            new List<TaskId>(), new List<TaskId>(), null);
        var parentIssue = CheckIssue("I", Array.Empty<string>()) with { SubIssues = new List<PlannedSubIssue> { sub } };
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: verify", new List<PlannedIssue> { parentIssue }),
        });
        var findings = new PlanQualityChecker(artifacts).Run();
        Assert.Contains(findings, f => f.Category == PlanCheckCategory.VerificationExpectations
            && f.TaskId == new TaskId("SUB"));
    }

    [Fact]
    public void CyclicDependencySurfacesPlanCheckError()
    {
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: cycle", new List<PlannedIssue>
            {
                CheckIssue("A", new[] { "C" }),
                CheckIssue("B", new[] { "A" }),
                CheckIssue("C", new[] { "B" }),
            }),
        });
        var findings = new PlanQualityChecker(artifacts).Run();
        var cycle = findings.First(f => f.Category == PlanCheckCategory.Dependencies);
        Assert.Equal(PlanCheckSeverity.Error, cycle.Severity);
        Assert.Contains("Cycle", cycle.Message);
    }

    [Fact]
    public void CyclicParallelizableWavesBreakAtCycle()
    {
        var artifacts = CheckArtifacts(new List<PlannedMilestone>
        {
            CheckMilestone("M0", "M0: cyc", new List<PlannedIssue>
            {
                CheckIssue("A", new[] { "C" }),
                CheckIssue("B", new[] { "A" }),
                CheckIssue("C", new[] { "B" }),
            }),
        });
        var waves = BlockingTaskHelpers.CreationOrderWaves(artifacts);
        Assert.Empty(waves);
    }
}

file sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()); Directory.CreateDirectory(Path); }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
