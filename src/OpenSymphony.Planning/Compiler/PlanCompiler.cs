using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Planning.Compiler;

using OpenSymphony.Planning.Generator;

public sealed class PlanCompiler
{
    public static PlanCompiler New() => new();

    public CompilationResult Compile(PlanArtifacts artifacts)
    {
        var taxonomyViolations = new List<TaxonomyViolation>();
        var validationMessages = new List<ValidationMessage>();
        var underspecifiedSubIssues = new List<UnderspecifiedSubIssue>();

        ValidateTaxonomy(artifacts.Milestones, taxonomyViolations, validationMessages);

        var milestones = artifacts.Milestones;
        var manifest = artifacts.Manifest;
        var planningWave = artifacts.PlanningWave;
        var tasksDir = artifacts.Manifest.TasksDir;

        var compiledMilestones = new List<CompiledMilestone>(artifacts.Milestones.Count);
        var dependencyEdges = new List<DependencyEdge>();
        var subIssueCount = 0;
        var issueCountTotal = 0;

        foreach (var milestone in milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                issueCountTotal++;
                CollectIssueDependencyEdges(issue, milestone.Name, dependencyEdges);
                ValidateIssue(issue, validationMessages);
                foreach (var sub in issue.SubIssues)
                {
                    subIssueCount++;
                    CollectSubIssueDependencyEdges(sub, issue, milestone.Name, dependencyEdges);
                    ValidateSubIssue(sub, issue, validationMessages, underspecifiedSubIssues);
                }
            }
            compiledMilestones.Add(CompileMilestone(milestone));
        }

        var manifestYaml = RenderManifestYaml(planningWave, tasksDir, milestones);

        var appliedHierarchy = new AppliedHierarchy(planningWave, compiledMilestones.ToList());

        dependencyEdges.Sort((a, b) =>
        {
            var c = string.Compare(a.Milestone, b.Milestone, StringComparison.Ordinal);
            if (c != 0) return c;
            c = a.Relation.CompareTo(b.Relation);
            if (c != 0) return c;
            c = a.Source.CompareTo(b.Source);
            if (c != 0) return c;
            return a.Target.CompareTo(b.Target);
        });

        var milestoneCount = artifacts.Milestones.Count;
        var dependencyMetadata = new DependencyMetadata(
            planningWave,
            milestoneCount + issueCountTotal + subIssueCount,
            milestoneCount, issueCountTotal, subIssueCount,
            dependencyEdges);

        underspecifiedSubIssues.Sort((a, b) => a.SubIssueId.CompareTo(b.SubIssueId));

        // Cross-check manifest milestones against compiled milestones.
        ValidateManifestConsistency(manifest, compiledMilestones, validationMessages);

        // Sort the validation message vectors AFTER the manifest consistency step.
        SortMessages(taxonomyViolations, validationMessages);

        var receiptStruct = BuildPublishReceipt(planningWave, compiledMilestones, tasksDir, manifest);
        var publishReceiptYaml = SerializeReceiptYaml(receiptStruct);

        return new CompilationResult(
            planningWave, manifestYaml, publishReceiptYaml, appliedHierarchy,
            taxonomyViolations, validationMessages, underspecifiedSubIssues, dependencyMetadata);
    }

    private static CompiledMilestone CompileMilestone(PlannedMilestone milestone)
        => new(milestone.Name, milestone.Goal, milestone.Notes,
            milestone.Issues.Select(i => CompilerDomainHelpers.IssueToCompiled(i, milestone.Name)).ToList());

    private static void CollectIssueDependencyEdges(PlannedIssue issue, string milestoneName, List<DependencyEdge> edges)
    {
        foreach (var blocker in issue.BlockedBy)
            edges.Add(new DependencyEdge(blocker, issue.Id, milestoneName, DependencyRelation.Blocks));
        foreach (var blocked in issue.Blocks)
            edges.Add(new DependencyEdge(issue.Id, blocked, milestoneName, DependencyRelation.Blocks));
    }

    private static void CollectSubIssueDependencyEdges(PlannedSubIssue sub, PlannedIssue parent, string milestoneName, List<DependencyEdge> edges)
    {
        edges.Add(new DependencyEdge(parent.Id, sub.Id, milestoneName, DependencyRelation.ParentOf));
        foreach (var blocker in sub.BlockedBy)
            edges.Add(new DependencyEdge(blocker, sub.Id, milestoneName, DependencyRelation.Blocks));
        foreach (var blocked in sub.Blocks)
            edges.Add(new DependencyEdge(sub.Id, blocked, milestoneName, DependencyRelation.Blocks));
    }

    private static void ValidateTaxonomy(List<PlannedMilestone> milestones, List<TaxonomyViolation> taxonomyViolations, List<ValidationMessage> validationMessages)
    {
        if (milestones.Count == 0)
        {
            taxonomyViolations.Add(new TaxonomyViolation(null, null, "no milestones produced", "Generator must produce at least one Linear milestone"));
            validationMessages.Add(ValidationMessage.Error(null, "milestones", "Plan contains no milestones; expected at least one Linear milestone"));
            return;
        }

        foreach (var milestone in milestones)
        {
            if (string.IsNullOrWhiteSpace(milestone.Name))
            {
                taxonomyViolations.Add(new TaxonomyViolation(milestone.Id, TaskKind.Milestone, "milestone has empty name", $"Provide a non-empty Linear milestone name for task {milestone.Id}"));
                validationMessages.Add(ValidationMessage.Error(milestone.Id, "name", "Linear milestone name is required"));
            }
        }
    }

    private static void ValidateIssue(PlannedIssue issue, List<ValidationMessage> validationMessages)
    {
        if (issue.AcceptanceCriteria.Count == 0)
            validationMessages.Add(ValidationMessage.Error(issue.Id, "acceptanceCriteria", "Linear issue requires at least one acceptance criterion"));
        for (var i = 0; i < issue.AcceptanceCriteria.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(issue.AcceptanceCriteria[i].Description))
                validationMessages.Add(ValidationMessage.Error(issue.Id, "acceptanceCriteria", $"Acceptance criterion {i + 1} on issue {issue.Id} has empty description"));
        }
        if (string.IsNullOrWhiteSpace(issue.Title))
            validationMessages.Add(ValidationMessage.Error(issue.Id, "title", "Linear issue requires a non-empty title"));
        if (issue.TaskFile is null)
            validationMessages.Add(ValidationMessage.Error(issue.Id, "taskFile", $"Linear issue {issue.Id} is missing its task file reference; assign a relative path under tasksDir"));
    }

    private static void ValidateSubIssue(PlannedSubIssue sub, PlannedIssue parent, List<ValidationMessage> validationMessages, List<UnderspecifiedSubIssue> underspecified)
    {
        if (sub.VerificationSteps.Count == 0)
            validationMessages.Add(ValidationMessage.Error(sub.Id, "verificationExpectations", $"Linear sub-issue {sub.Id} requires at least one verification expectation"));
        for (var i = 0; i < sub.VerificationSteps.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(sub.VerificationSteps[i]))
                validationMessages.Add(ValidationMessage.Error(sub.Id, "verificationExpectations", $"Verification step {i + 1} on sub-issue {sub.Id} is empty"));
        }
        if (string.IsNullOrWhiteSpace(sub.Title))
            validationMessages.Add(ValidationMessage.Error(sub.Id, "title", "Linear sub-issue requires a non-empty title"));
        if (sub.TaskFile is null)
            validationMessages.Add(ValidationMessage.Error(sub.Id, "taskFile", $"Linear sub-issue {sub.Id} is missing its task file reference; assign a relative path under tasksDir"));

        var reasons = CompilerDomainHelpers.ClassifyUnderspecifiedSubIssue(sub);
        if (reasons.Count > 0)
        {
            underspecified.Add(new UnderspecifiedSubIssue(
                sub.Id, parent.Id,
                sub.AcceptanceCriteria.Count, sub.VerificationSteps.Count,
                sub.Deliverables.Count, sub.ScopeIn.Count, reasons));
            validationMessages.Add(ValidationMessage.Warning(sub.Id, "readiness", $"Sub-issue {sub.Id} is underspecified: must add deliverables, scope, acceptance criteria, or verification expectations before publish"));
        }
    }

    private static void ValidateManifestConsistency(TaskPackageManifest manifest, List<CompiledMilestone> compiledMilestones, List<ValidationMessage> validationMessages)
    {
        var compiledMilestoneNames = new HashSet<string>(compiledMilestones.Select(m => m.Name));
        foreach (var name in manifest.Milestones)
        {
            if (!compiledMilestoneNames.Contains(name))
                validationMessages.Add(ValidationMessage.Error(null, "milestones", $"Manifest milestone '{name}' is not present in compiled hierarchy"));
        }
        foreach (var milestone in compiledMilestones)
        {
            if (!manifest.Milestones.Contains(milestone.Name))
                validationMessages.Add(ValidationMessage.Error(null, "milestones", $"Compiled milestone '{milestone.Name}' is missing from manifest milestone list"));
        }

        var compiledTaskIds = new SortedDictionary<string, string>();
        var compiledPresentIds = new HashSet<string>();
        foreach (var milestone in compiledMilestones)
        {
            foreach (var issue in milestone.Issues)
            {
                compiledPresentIds.Add(issue.TaskId.Value);
                if (!string.IsNullOrEmpty(issue.SourceFile))
                    compiledTaskIds[issue.TaskId.Value] = issue.SourceFile;
                foreach (var sub in issue.SubIssues)
                {
                    compiledPresentIds.Add(sub.TaskId.Value);
                    if (!string.IsNullOrEmpty(sub.SourceFile))
                        compiledTaskIds[sub.TaskId.Value] = sub.SourceFile;
                }
            }
        }

        var mismatchedIds = new HashSet<string>();
        foreach (var task in manifest.Tasks)
        {
            var idKey = task.Id.Value;
            if (compiledTaskIds.TryGetValue(idKey, out var compiledFile))
            {
                if (compiledFile == task.File) { /* match */ }
                else
                {
                    mismatchedIds.Add(idKey);
                    validationMessages.Add(ValidationMessage.Error(task.Id, "tasks", $"Manifest task '{task.Id.Value}' file '{task.File}' disagrees with compiled hierarchy file '{compiledFile}'"));
                }
            }
            else if (compiledPresentIds.Contains(idKey))
            {
                // Compiled side has the id but we couldn't derive its task file; do not emit duplicate.
            }
            else
            {
                validationMessages.Add(ValidationMessage.Error(task.Id, "tasks", $"Manifest task '{task.Id.Value}' has no matching compiled hierarchy entry"));
            }
        }

        foreach (var taskId in compiledPresentIds)
        {
            if (mismatchedIds.Contains(taskId)) continue;
            compiledTaskIds.TryGetValue(taskId, out var compiledFile);
            compiledFile ??= "";
            bool inManifest = manifest.Tasks.Any(t =>
            {
                if (!string.IsNullOrEmpty(compiledFile))
                    return t.Id.Value == taskId && t.File == compiledFile;
                return t.Id.Value == taskId;
            });
            if (!inManifest)
            {
                var message = string.IsNullOrEmpty(compiledFile)
                    ? $"Compiled task '{taskId}' is missing from manifest tasks list (compiled source file is empty)"
                    : $"Compiled task '{taskId}' (file '{compiledFile}') is missing from manifest tasks list";
                validationMessages.Add(ValidationMessage.Error(new TaskId(taskId), "tasks", message));
            }
        }
    }

    private static string RenderManifestYaml(string planningWave, string tasksDir, List<PlannedMilestone> milestones)
    {
        var milestoneRefs = milestones.Select(m => m.Name).ToList();
        var tasks = new List<ManifestTaskYaml>();
        foreach (var milestone in milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                if (issue.TaskFile is { } file)
                    tasks.Add(new ManifestTaskYaml(issue.Id.Value, file));
                foreach (var sub in issue.SubIssues)
                {
                    if (sub.TaskFile is { } subFile)
                        tasks.Add(new ManifestTaskYaml(sub.Id.Value, subFile));
                }
            }
        }

        var yamlStruct = new ManifestYaml(planningWave, tasksDir, milestoneRefs, tasks);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();
        return serializer.Serialize(yamlStruct);
    }

    private sealed record ManifestYaml(string PlanningWave, string TasksDir, List<string> Milestones, List<ManifestTaskYaml> Tasks);
    private sealed record ManifestTaskYaml(string Id, string File);

    private static LinearPublishReceipt BuildPublishReceipt(string planningWave, List<CompiledMilestone> compiledMilestones, string _tasksDir, TaskPackageManifest manifest)
    {
        var milestones = new SortedDictionary<string, MilestoneReceipt>();
        var tasks = new SortedDictionary<TaskId, LinearPublishEntity>();
        var manifestLookup = new SortedDictionary<string, string>();
        foreach (var task in manifest.Tasks)
            manifestLookup[task.Id.Value] = task.File;

        foreach (var milestone in compiledMilestones)
        {
            var linkedIssues = new List<TaskId>();
            foreach (var issue in milestone.Issues)
            {
                linkedIssues.Add(issue.TaskId);
                var file = string.IsNullOrEmpty(issue.SourceFile)
                    ? (manifestLookup.TryGetValue(issue.TaskId.Value, out var f) ? f : "")
                    : issue.SourceFile;
                tasks[issue.TaskId] = new LinearPublishEntity(
                    issue.TaskId, file, TaskKind.Issue, milestone.Name, null,
                    new List<TaskId>(issue.BlockedBy), new List<TaskId>(issue.Blocks),
                    new List<string>(), null, null, null);
                foreach (var sub in issue.SubIssues)
                {
                    var subFile = string.IsNullOrEmpty(sub.SourceFile)
                        ? (manifestLookup.TryGetValue(sub.TaskId.Value, out var sf) ? sf : "")
                        : sub.SourceFile;
                    tasks[sub.TaskId] = new LinearPublishEntity(
                        sub.TaskId, subFile, TaskKind.SubIssue, milestone.Name, issue.TaskId,
                        new List<TaskId>(sub.BlockedBy), new List<TaskId>(sub.Blocks),
                        new List<string>(), null, null, null);
                }
            }
            milestones[milestone.Name] = new MilestoneReceipt(milestone.Name, null, linkedIssues);
        }

        return new LinearPublishReceipt(planningWave, null, null, milestones, tasks);
    }

    private static string SerializeReceiptYaml(LinearPublishReceipt receipt)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();
        return serializer.Serialize(receipt);
    }

    private static void SortMessages(List<TaxonomyViolation> taxonomy, List<ValidationMessage> messages)
    {
        taxonomy.Sort((a, b) =>
        {
            var c = CompareNullable(a.TaskKind, b.TaskKind);
            if (c != 0) return c;
            c = string.Compare(a.TaskId?.Value ?? "", b.TaskId?.Value ?? "", StringComparison.Ordinal);
            if (c != 0) return c;
            return string.Compare(a.Reason, b.Reason, StringComparison.Ordinal);
        });
        messages.Sort((a, b) =>
        {
            var c = a.Severity.CompareTo(b.Severity);
            if (c != 0) return c;
            c = string.Compare(a.TaskId?.Value ?? "", b.TaskId?.Value ?? "", StringComparison.Ordinal);
            if (c != 0) return c;
            return string.Compare(a.Field, b.Field, StringComparison.Ordinal);
        });
    }

    private static int CompareNullable<T>(T? a, T? b) where T : struct, Enum
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        return a.Value.CompareTo(b.Value);
    }
}
