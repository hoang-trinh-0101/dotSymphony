using OpenSymphony.Planning.Compiler;
using OpenSymphony.Planning.Generator;

namespace OpenSymphony.Planning.Tests;

public class CompilerTests
{
    private static PlanArtifacts SampleArtifact(string planningWave)
    {
        var issueId = new TaskId("OSYM-733");
        var subImpl = new TaskId("OSYM-733-IMPL");
        var subVal = new TaskId("OSYM-733-VAL");

        var issue = new PlannedIssue(
            issueId, "Milestone, issue, sub-issue compiler", "Compile planning artifacts into Linear hierarchy",
            new List<string> { "Compile planner" }, new List<string> { "Linear mutation" },
            new List<string> { "Plan compiler" },
            new List<AcceptanceCriterion> { new("Compiler emits manifest-driven task package", "cargo test") },
            new List<string> { "cargo test -p opensymphony" },
            new List<string> { "PRD 4.6.3" }, new List<string> { "Spec is referenced" },
            null, TaskPriority.Urgent, 5, new List<TaskId>(), new List<TaskId>(),
            new List<PlannedSubIssue>
            {
                new(subImpl, "Implement milestone/issue/sub-issue compiler", "Implementation unit for compiler",
                    new List<string> { "Compiler body" }, new List<string> { "Publish flow" },
                    new List<string> { "Compiler module" },
                    new List<AcceptanceCriterion> { new("Compiler module compiles", "cargo check") },
                    new List<string> { "cargo test -p opensymphony compiler" },
                    new List<string> { "PRD 4.6.3" }, new List<string> { "Spec referenced" },
                    null, TaskPriority.Urgent, 3, new List<TaskId>(), new List<TaskId> { subVal },
                    "docs/tasks/osym-733-impl.md"),
                new(subVal, "Validate compiler output", "Validation sub-issue",
                    new List<string> { "Tests" }, new List<string>(),
                    new List<string> { "Validation tests" },
                    new List<AcceptanceCriterion> { new("Tests pass", null) },
                    new List<string> { "cargo test" },
                    new List<string> { "PRD 4.6.3" }, new List<string> { "Implementation done" },
                    null, TaskPriority.Urgent, 2, new List<TaskId> { subImpl }, new List<TaskId>(),
                    "docs/tasks/osym-733-val.md"),
            },
            "docs/tasks/osym-733-milestone-issue-and-sub-issue-compiler.md");

        var tasks = new List<ManifestTask>
        {
            new(issueId, "docs/tasks/osym-733-milestone-issue-and-sub-issue-compiler.md"),
        };
        foreach (var sub in issue.SubIssues)
            if (sub.TaskFile is { } file)
                tasks.Add(new ManifestTask(sub.Id, file));

        var manifest = new TaskPackageManifest(planningWave, "docs/tasks",
            new List<string> { "M9: Collaborative Planning Alpha" }, tasks);

        return new PlanArtifacts(DateTime.UtcNow, planningWave,
            new List<PlannedMilestone>
            {
                new(new TaskId("OSYM-MS-9"), "M9: Collaborative Planning Alpha", "Deliver compiler layer",
                    new List<PlannedIssue> { issue }, new List<AcceptanceCriterion>(), new List<string>(), null),
            },
            manifest, "", new SortedDictionary<TaskId, string>());
    }

    [Fact]
    public void CompileCompletePlanIsPublishable()
    {
        var compiler = PlanCompiler.New();
        var result = compiler.Compile(SampleArtifact("rich-client-hosted-mode"));
        Assert.True(result.IsPublishable());
        Assert.Empty(result.TaxonomyViolations);
        Assert.Equal("rich-client-hosted-mode", result.PlanningWave);
        Assert.Contains("planningWave: rich-client-hosted-mode", result.ManifestYaml);
        Assert.Contains("planningWave: rich-client-hosted-mode", result.PublishReceiptYaml);
    }

    [Fact]
    public void CompileFlagsMissingAcceptanceCriteria()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        var issue = artifact.Milestones[0].Issues[0];
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0
                ? iss with { AcceptanceCriteria = new List<AcceptanceCriterion>() } : iss).ToList() }
            : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.False(result.IsPublishable());
        Assert.Contains(result.ValidationMessages, m => m.Field == "acceptanceCriteria");
    }

    [Fact]
    public void CompileFlagsMissingSubIssueVerificationExpectations()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0
                ? iss with { SubIssues = iss.SubIssues.Select(s => s with { VerificationSteps = new List<string>() }).ToList() } : iss).ToList() }
            : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.False(result.IsPublishable());
        Assert.Contains(result.ValidationMessages, m => m.Field == "verificationExpectations");
    }

    [Fact]
    public void CompileFlagsUnderspecifiedSubIssues()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0
                ? iss with { SubIssues = iss.SubIssues.Select((s, k) => k == 0
                    ? s with { Deliverables = new List<string>(), ScopeIn = new List<string>() } : s).ToList() } : iss).ToList() }
            : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.Contains(result.UnderspecifiedSubIssues, u => u.SubIssueId.Value == "OSYM-733-IMPL");
    }

    [Fact]
    public void CompileManifestReferencesIssueAndSubIssueOnly()
    {
        var result = PlanCompiler.New().Compile(SampleArtifact("rich-client-hosted-mode"));
        Assert.Contains("M9: Collaborative Planning Alpha", result.ManifestYaml);
        Assert.Contains("- id: OSYM-733", result.ManifestYaml);
        Assert.Contains("- id: OSYM-733-IMPL", result.ManifestYaml);
        Assert.Contains("- id: OSYM-733-VAL", result.ManifestYaml);
    }

    [Fact]
    public void CompileDependencyMetadataRecordsParentAndBlocksEdges()
    {
        var result = PlanCompiler.New().Compile(SampleArtifact("rich-client-hosted-mode"));
        Assert.Contains(result.DependencyMetadata.Edges, e =>
            e.Relation == DependencyRelation.ParentOf && e.Source.Value == "OSYM-733" && e.Target.Value == "OSYM-733-IMPL");
        Assert.Contains(result.DependencyMetadata.Edges, e =>
            e.Relation == DependencyRelation.Blocks && e.Source.Value == "OSYM-733-IMPL" && e.Target.Value == "OSYM-733-VAL");
    }

    [Fact]
    public void CompilePublishReceiptCarriesPlanningWaveAndMilestoneEntries()
    {
        var result = PlanCompiler.New().Compile(SampleArtifact("rich-client-hosted-mode"));
        Assert.Contains("planningWave: rich-client-hosted-mode", result.PublishReceiptYaml);
        Assert.Contains("M9: Collaborative Planning Alpha", result.PublishReceiptYaml);
        Assert.Contains("OSYM-733", result.PublishReceiptYaml);
    }

    [Fact]
    public void CompileHandlesInvalidTaxonomyMarker()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Name = "  " } : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.NotEmpty(result.TaxonomyViolations);
        var violation = result.TaxonomyViolations[0];
        Assert.Equal(TaskKind.Milestone, violation.TaskKind);
    }

    [Fact]
    public void CompileEmitsValidationMessageForMissingInScopeSubIssue()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0
                ? iss with { SubIssues = iss.SubIssues.Select((s, k) => k == 0
                    ? s with { ScopeIn = new List<string>(), Deliverables = new List<string>(),
                               VerificationSteps = new List<string>(), AcceptanceCriteria = new List<AcceptanceCriterion>() }
                    : s).ToList() } : iss).ToList() }
            : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        var underspecified = result.UnderspecifiedSubIssues.Find(u => u.SubIssueId.Value == "OSYM-733-IMPL");
        Assert.NotNull(underspecified);
        Assert.NotEmpty(underspecified!.Reasons);
    }

    [Fact]
    public void CompileDependencyMetadataTotalsMatchHierarchy()
    {
        var result = PlanCompiler.New().Compile(SampleArtifact("rich-client-hosted-mode"));
        Assert.Equal(result.AppliedHierarchy.Milestones[0].Issues[0].SubIssues.Count, result.DependencyMetadata.SubIssueCount);
        Assert.Equal(1, result.DependencyMetadata.IssueCount);
        Assert.Equal(1, result.DependencyMetadata.MilestoneCount);
        Assert.Equal(1 + 1 + 2, result.DependencyMetadata.TotalNodes);
    }

    [Fact]
    public void CompileFlagsMissingTaskFileOnIssue()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0 ? iss with { TaskFile = null } : iss).ToList() }
            : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.False(result.IsPublishable());
        Assert.Contains(result.ValidationMessages, m => m.Field == "taskFile"
            && m.TaskId?.Value == "OSYM-733" && m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void CompileFlagsMissingTaskFileOnSubIssue()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0
                ? iss with { SubIssues = iss.SubIssues.Select((s, k) => k == 0 ? s with { TaskFile = null } : s).ToList() }
                : iss).ToList() } : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.False(result.IsPublishable());
        Assert.Contains(result.ValidationMessages, m => m.Field == "taskFile"
            && m.TaskId?.Value == "OSYM-733-IMPL");
    }

    [Fact]
    public void CompileFlagsManifestTasksMismatchWithCompiledHierarchy()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Manifest = artifact.Manifest with
        {
            Tasks = artifact.Manifest.Tasks.Where(t => t.Id.Value != "OSYM-733").ToList(),
        } };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.False(result.IsPublishable());
        Assert.Contains(result.ValidationMessages, m => m.Field == "tasks");
    }

    [Fact]
    public void CompileDoesNotDoubleReportFileMismatchForSameId()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Manifest = artifact.Manifest with
        {
            Tasks = artifact.Manifest.Tasks.Select(t => t.Id.Value == "OSYM-733"
                ? t with { File = "docs/tasks/osym-733-renamed.md" } : t).ToList(),
        } };
        var result = PlanCompiler.New().Compile(artifact);
        var osym733Tasks = result.ValidationMessages.Count(m => m.Field == "tasks" && m.TaskId?.Value == "OSYM-733");
        Assert.Equal(1, osym733Tasks);
    }

    [Fact]
    public void CompileDiagnosesCompiledTaskMissingFromManifestEvenWhenSourceFileEmpty()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with
        {
            Milestones = artifact.Milestones.Select((m, i) => i == 0
                ? m with { Issues = m.Issues.Select((iss, j) => j == 0 ? iss with { TaskFile = null } : iss).ToList() }
                : m).ToList(),
            Manifest = artifact.Manifest with { Tasks = artifact.Manifest.Tasks.Where(t => t.Id.Value != "OSYM-733").ToList() },
        };
        var result = PlanCompiler.New().Compile(artifact);
        Assert.Contains(result.ValidationMessages, m => m.Field == "tasks" && m.TaskId?.Value == "OSYM-733");
    }

    [Fact]
    public void CompileDoesNotEmitDuplicateDiagnosticWhenCompiledIdPresentWithEmptyFile()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0 ? iss with { TaskFile = null } : iss).ToList() }
            : m).ToList() };
        var result = PlanCompiler.New().Compile(artifact);
        var spurious = result.ValidationMessages.Count(m => m.Field == "tasks"
            && m.TaskId?.Value == "OSYM-733" && m.Message.Contains("no matching compiled hierarchy entry"));
        Assert.Equal(0, spurious);
    }

    [Fact]
    public void CompileDoesNotEmitDuplicateDiagnosticWithRealGeneratorOutput()
    {
        var session = new PlanningSession(
            new IntakeContext("rich-client-hosted-mode", "End-to-end consistency test",
                new List<string> { "Compiler emits Linear taxonomy", "Manifest is renderable" },
                new List<string> { "Compile planning artifacts" },
                new List<string> { "Preserve planningWave" }, new List<string>(),
                new List<string> { "docs/hosted-client-PRD.md" }), "docs/tasks");
        var generator = new PlanGenerator(session);
        var artifacts = generator.Generate().Value;
        var result = PlanCompiler.New().Compile(artifacts);
        var counts = new SortedDictionary<string, int>();
        foreach (var m in result.ValidationMessages)
            if (m.Field == "tasks" && m.TaskId is { } t)
            {
                counts.TryGetValue(t.Value, out var c);
                counts[t.Value] = c + 1;
            }
        foreach (var (id, count) in counts)
            Assert.True(count <= 1, $"task {id} emitted {count} diagnostics; expected <=1");
    }

    [Fact]
    public void CompileDropsAppliedHierarchyArgFromPublishReceiptBuilder()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        var compiledMilestones = artifact.Milestones.Select(PlanCompiler.CompileMilestone).ToList();
        var receipt = PlanCompiler.BuildPublishReceipt(artifact.PlanningWave, compiledMilestones, "docs/tasks", artifact.Manifest);
        Assert.NotEmpty(receipt.PlanningWave);
        Assert.Equal(compiledMilestones.Sum(m => m.Issues.Count + m.Issues.Sum(i => i.SubIssues.Count)), receipt.Tasks.Count);
    }

    [Fact]
    public void CompilePublishReceiptSubIssueFallsBackToManifestWhenCompiledSourceEmpty()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        artifact = artifact with { Milestones = artifact.Milestones.Select((m, i) => i == 0
            ? m with { Issues = m.Issues.Select((iss, j) => j == 0
                ? iss with { SubIssues = iss.SubIssues.Select((s, k) => k == 0 ? s with { TaskFile = null } : s).ToList() }
                : iss).ToList() } : m).ToList() };
        var subId = artifact.Milestones[0].Issues[0].SubIssues[0].Id;
        var compiledMilestones = artifact.Milestones.Select(PlanCompiler.CompileMilestone).ToList();
        var receipt = PlanCompiler.BuildPublishReceipt(artifact.PlanningWave, compiledMilestones, "docs/tasks", artifact.Manifest);
        Assert.True(receipt.Tasks.ContainsKey(subId));
        Assert.Equal("docs/tasks/osym-733-impl.md", receipt.Tasks[subId].SourceFile);
    }

    [Fact]
    public void CompileDependencyMetadataEdgesAreSortedByMilestoneThenRelation()
    {
        var artifact = SampleArtifact("rich-client-hosted-mode");
        var secondIssue = artifact.Milestones[0].Issues[0];
        var secondMs = new PlannedMilestone(new TaskId("OSYM-MS-10"), "M10: Follow-up Alpha",
            "Second planning iteration", new List<PlannedIssue> { secondIssue },
            new List<AcceptanceCriterion>(), new List<string>(), null);
        artifact = artifact with
        {
            Milestones = artifact.Milestones.Append(secondMs).ToList(),
            Manifest = artifact.Manifest with
            {
                Milestones = artifact.Manifest.Milestones.Append(secondMs.Name).ToList(),
                Tasks = artifact.Manifest.Tasks.Concat(secondMs.Issues.SelectMany(i =>
                    (i.TaskFile is { } f ? new[] { new ManifestTask(i.Id, f) } : Enumerable.Empty<ManifestTask>())
                    .Concat(i.SubIssues.Where(s => s.TaskFile is not null).Select(s => new ManifestTask(s.Id, s.TaskFile!))))).ToList(),
            },
        };
        var result = PlanCompiler.New().Compile(artifact);
        (string, DependencyRelation)? last = null;
        foreach (var edge in result.DependencyMetadata.Edges)
        {
            var key = (edge.Milestone, edge.Relation);
            if (last is { } prev)
                Assert.True(prev.CompareTo(key) <= 0, $"dependency edges must be sorted: {key} came after {prev}");
            last = key;
        }
    }

    [Fact]
    public void CompileEndToEndRunOnPlanGeneratorOutput()
    {
        var session = new PlanningSession(
            new IntakeContext("rich-client-hosted-mode", "Milestone, issue, and sub-issue compiler end-to-end",
                new List<string> { "Compiler emits Linear taxonomy", "Manifest is renderable", "Publish receipt is renderable" },
                new List<string> { "Compile planning artifacts into Linear hierarchy", "Validate sub-issue readiness fields" },
                new List<string> { "Preserve planningWave through manifest and receipt" },
                new List<string>(), new List<string> { "docs/hosted-client-PRD.md" }), "docs/tasks");
        var generator = new PlanGenerator(session);
        var artifacts = generator.Generate().Value;
        var result = PlanCompiler.New().Compile(artifacts);
        Assert.Equal("rich-client-hosted-mode", result.PlanningWave);
        Assert.Contains("planningWave: rich-client-hosted-mode", result.ManifestYaml);
        Assert.Contains("planningWave: rich-client-hosted-mode", result.PublishReceiptYaml);
        Assert.NotEmpty(result.PublishReceiptYaml);
        Assert.Contains(result.DependencyMetadata.Edges, e => e.Relation == DependencyRelation.ParentOf);
    }
}
