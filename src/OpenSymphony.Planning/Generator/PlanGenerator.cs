using System.Text;
using OpenSymphony.Domain;

namespace OpenSymphony.Planning.Generator;

public enum GenerationErrorKind { IncompleteSession, CircularDependency }

public sealed record GenerationError(GenerationErrorKind Kind, string Message)
{
    public override string ToString() => Kind switch
    {
        GenerationErrorKind.IncompleteSession => $"planning session is incomplete: missing {Message}",
        GenerationErrorKind.CircularDependency => $"circular dependency detected: {Message}",
        _ => Message,
    };
}

public sealed class PlanGenerator
{
    private readonly PlanningSession _session;
    private int _taskCounter;

    public PlanGenerator(PlanningSession session)
    {
        _session = session;
        _taskCounter = 0;
    }

    public Result<PlanArtifacts, GenerationError> Generate()
    {
        var v = ValidateSession();
        if (v.IsErr) return Result<PlanArtifacts, GenerationError>.Err(v.Error);

        var milestones = GenerateMilestones();
        var manifest = GenerateManifest(milestones);
        var milestoneIndex = RenderMilestoneIndex(milestones);
        var taskFiles = GenerateTaskFiles(milestones);

        return Result<PlanArtifacts, GenerationError>.Ok(new PlanArtifacts(
            DateTime.UtcNow, _session.Intake.PlanningWave, milestones, manifest, milestoneIndex, taskFiles));
    }

    public Result<PlanArtifacts, GenerationError> Regenerate(PlanArtifacts existing, RegenerationScope scope)
    {
        var v = ValidateSession();
        if (v.IsErr) return Result<PlanArtifacts, GenerationError>.Err(v.Error);
        SeedTaskCounterFromExisting(existing);

        List<PlannedMilestone> milestones = scope switch
        {
            RegenerationScope.Issues { MilestoneIds: var ids } =>
                RegenerateIssuesForMilestones(existing.Milestones, ids),
            RegenerationScope.SubIssues { IssueIds: var ids } =>
                RegenerateSubIssuesForIssues(existing.Milestones, ids),
            _ when scope.IncludesMilestones() => GenerateMilestones(),
            _ => existing.Milestones.Select(CloneMilestone).ToList(),
        };

        var manifest = scope.IncludesManifest()
            ? GenerateManifest(milestones)
            : existing.Manifest;

        var milestoneIndex = scope.IncludesMilestoneIndex()
            ? RenderMilestoneIndex(milestones)
            : existing.MilestoneIndex;

        var taskFiles = scope.IncludesTaskFiles()
            ? GenerateTaskFiles(milestones)
            : new SortedDictionary<TaskId, string>(existing.TaskFiles);

        return Result<PlanArtifacts, GenerationError>.Ok(new PlanArtifacts(
            DateTime.UtcNow, _session.Intake.PlanningWave, milestones, manifest, milestoneIndex, taskFiles));
    }

    private static PlannedMilestone CloneMilestone(PlannedMilestone m) => m with { };
    private static PlannedIssue CloneIssue(PlannedIssue i) => i with { };

    private void SeedTaskCounterFromExisting(PlanArtifacts existing)
    {
        foreach (var milestone in existing.Milestones)
        {
            ObserveTaskId(milestone.Id);
            foreach (var issue in milestone.Issues)
            {
                ObserveTaskId(issue.Id);
                foreach (var sub in issue.SubIssues)
                    ObserveTaskId(sub.Id);
            }
        }
    }

    private void ObserveTaskId(TaskId id)
    {
        if (id.Value.StartsWith("TASK-") && int.TryParse(id.Value["TASK-"..], out var number))
            _taskCounter = Math.Max(_taskCounter, number);
    }

    private List<PlannedMilestone> RegenerateIssuesForMilestones(List<PlannedMilestone> existing, List<TaskId>? targetIds)
    {
        var targetSet = targetIds is not null ? new HashSet<TaskId>(targetIds) : null;
        var requirementsByMilestone = RequirementsByMilestone(existing.Count);

        return existing.Select((milestone, msIdx) =>
        {
            if (targetSet is null || targetSet.Contains(milestone.Id))
            {
                var intake = new IntakeContext(
                    _session.Intake.PlanningWave,
                    _session.Intake.ProjectDescription,
                    _session.Intake.SuccessCriteria,
                    msIdx < requirementsByMilestone.Count ? requirementsByMilestone[msIdx] : new List<string>(),
                    _session.Intake.Constraints,
                    _session.Intake.OpenQuestions,
                    _session.Intake.ReferenceDocs);
                var issues = GenerateIssuesForMilestone(intake);
                return milestone with { Issues = issues };
            }
            return milestone;
        }).ToList();
    }

    private List<PlannedMilestone> RegenerateSubIssuesForIssues(List<PlannedMilestone> existing, List<TaskId>? targetIds)
    {
        var targetSet = targetIds is not null ? new HashSet<TaskId>(targetIds) : null;

        return existing.Select(milestone =>
        {
            var issues = milestone.Issues.Select(issue =>
            {
                if (targetSet is null || targetSet.Contains(issue.Id))
                {
                    var requirement = issue.Title;
                    var intake = new IntakeContext(
                        _session.Intake.PlanningWave,
                        _session.Intake.ProjectDescription,
                        _session.Intake.SuccessCriteria,
                        new List<string> { requirement },
                        _session.Intake.Constraints,
                        _session.Intake.OpenQuestions,
                        _session.Intake.ReferenceDocs);
                    var subCtx = SubIssueGenerationContext.FromIntake(issue.Id, requirement, intake);
                    var subIssues = GenerateSubIssuesForIssue(subCtx);
                    return issue with { SubIssues = subIssues };
                }
                return issue;
            }).ToList();

            return milestone with { Issues = issues };
        }).ToList();
    }

    private List<List<string>> RequirementsByMilestone(int milestoneCount)
    {
        var effectiveCount = Math.Max(milestoneCount, 1);
        var grouped = Enumerable.Range(0, effectiveCount).Select(_ => new List<string>()).ToList();

        for (var i = 0; i < _session.Intake.Requirements.Count; i++)
            grouped[i % effectiveCount].Add(_session.Intake.Requirements[i]);

        return grouped;
    }

    private Result<Unit, GenerationError> ValidateSession()
    {
        if (string.IsNullOrEmpty(_session.Intake.PlanningWave))
            return Result<Unit, GenerationError>.Err(new GenerationError(GenerationErrorKind.IncompleteSession, "planning_wave"));
        if (_session.Intake.Requirements.Count == 0)
            return Result<Unit, GenerationError>.Err(new GenerationError(GenerationErrorKind.IncompleteSession, "requirements"));
        return Result<Unit, GenerationError>.Ok(Unit.Value);
    }

    private TaskId NextTaskId()
    {
        _taskCounter++;
        return new TaskId($"TASK-{_taskCounter:D3}");
    }

    private List<PlannedMilestone> GenerateMilestones()
    {
        var intake = _session.Intake;
        var linearMilestones = _session.LinearGraphAnalysis?.Milestones ?? new List<MilestoneSummary>();
        var milestones = new List<PlannedMilestone>();

        if (linearMilestones.Count == 0)
        {
            var milestoneId = NextTaskId();
            var firstThree = string.Join(" ", intake.ProjectDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));
            var milestoneName = $"M1: {firstThree}";
            var issues = GenerateIssuesForMilestone(intake);
            milestones.Add(new PlannedMilestone(
                milestoneId, milestoneName, intake.ProjectDescription, issues,
                intake.SuccessCriteria.Select(c => new AcceptanceCriterion(c, null)).ToList(),
                new List<string>(), null));
        }
        else
        {
            var milestoneRequirements = RequirementsByMilestone(linearMilestones.Count);
            for (var msIdx = 0; msIdx < linearMilestones.Count; msIdx++)
            {
                var ms = linearMilestones[msIdx];
                var milestoneId = NextTaskId();
                if (msIdx < milestoneRequirements.Count && milestoneRequirements[msIdx].Count == 0)
                    continue;

                var milestoneIntake = intake with
                {
                    Requirements = msIdx < milestoneRequirements.Count ? milestoneRequirements[msIdx] : new List<string>()
                };
                var issues = GenerateIssuesForMilestone(milestoneIntake);
                milestones.Add(new PlannedMilestone(
                    milestoneId, ms.MilestoneName, $"Deliver {ms.MilestoneName} capabilities", issues,
                    new List<AcceptanceCriterion>(), new List<string>(), null));
            }
        }

        return milestones;
    }

    private List<PlannedIssue> GenerateIssuesForMilestone(IntakeContext intake)
    {
        var issues = new List<PlannedIssue>();

        for (var idx = 0; idx < intake.Requirements.Count; idx++)
        {
            var requirement = intake.Requirements[idx];
            var issueId = NextTaskId();
            var subCtx = SubIssueGenerationContext.FromIntake(issueId, requirement, intake);
            var subIssues = GenerateSubIssuesForIssue(subCtx);

            var blockedBy = new List<TaskId>();
            if (idx > 0 && issues.Count > 0)
                blockedBy.Add(issues[^1].Id);

            // Populate blocks symmetrically
            if (blockedBy.Count > 0)
            {
                var prevIssue = issues[^1];
                issues[^1] = prevIssue with { Blocks = [..prevIssue.Blocks, issueId] };
            }

            issues.Add(new PlannedIssue(
                issueId, requirement,
                $"Implement {requirement} as a vertical deliverable for the {intake.PlanningWave} planning wave.",
                new List<string> { requirement },
                new List<string>(),
                new List<string> { $"Working {requirement} implementation" },
                new List<AcceptanceCriterion> { new($"{requirement} meets acceptance standards", null) },
                new List<string> { $"Test {requirement} functionality" },
                new List<string>
                {
                    $"Planning wave: {intake.PlanningWave}",
                    $"Requirement {idx + 1} of {intake.Requirements.Count}",
                },
                new List<string>
                {
                    "Hidden assumptions from prior discussion are written down.",
                    "Required files, docs, and dependencies are explicitly referenced.",
                    "A coding agent could begin execution without additional planning context.",
                },
                null, TaskPriority.Normal, null, blockedBy, new List<TaskId>(),
                subIssues, $"{_session.TasksDir}/{issueId}.md"));
        }

        return issues;
    }

    private List<PlannedSubIssue> GenerateSubIssuesForIssue(SubIssueGenerationContext generation)
    {
        var subIssues = new List<PlannedSubIssue>();
        var implId = NextTaskId();
        var valId = NextTaskId();

        // Implementation sub-issue
        subIssues.Add(new PlannedSubIssue(
            implId,
            $"Implement {generation.Requirement}",
            $"Implementation unit for {generation.Requirement} in the {generation.PlanningWave} planning wave",
            new List<string> { $"Core implementation of {generation.Requirement}" },
            new List<string> { $"Testing and validation of {generation.Requirement}" },
            new List<string> { "Implementation code", "Unit tests" },
            new List<AcceptanceCriterion> { new($"Implementation of {generation.Requirement} compiles and passes tests", "cargo test") },
            new List<string> { "Run unit tests", "Verify code style" },
            generation.ImplementationContext(),
            new List<string> { "Requirements are clear and understood.", "Dependencies are available." },
            null, TaskPriority.Normal, 3,
            new List<TaskId>(), new List<TaskId> { valId },
            $"{_session.TasksDir}/{implId}.md"));

        // Validation sub-issue
        subIssues.Add(new PlannedSubIssue(
            valId,
            $"Validate {generation.Requirement}",
            $"Validation and testing for {generation.Requirement}",
            new List<string> { "Integration testing", "Acceptance criteria verification" },
            new List<string> { "Implementation changes" },
            new List<string> { "Test report", "Validation evidence" },
            new List<AcceptanceCriterion> { new($"All acceptance criteria for {generation.Requirement} are met", "cargo test --all") },
            new List<string> { "Run integration tests", "Verify acceptance criteria", "Generate validation report" },
            generation.ValidationContext(),
            new List<string> { "Implementation is complete.", "Test environment is configured." },
            null, TaskPriority.Normal, 2,
            new List<TaskId> { implId }, new List<TaskId>(),
            $"{_session.TasksDir}/{valId}.md"));

        return subIssues;
    }

    private TaskPackageManifest GenerateManifest(List<PlannedMilestone> milestones)
    {
        var tasks = new List<ManifestTask>();
        var milestoneNames = new List<string>();

        foreach (var milestone in milestones)
        {
            milestoneNames.Add(milestone.Name);
            foreach (var issue in milestone.Issues)
            {
                if (issue.TaskFile is { } taskFile)
                    tasks.Add(new ManifestTask(issue.Id, taskFile));
                foreach (var sub in issue.SubIssues)
                {
                    if (sub.TaskFile is { } subFile)
                        tasks.Add(new ManifestTask(sub.Id, subFile));
                }
            }
        }

        return new TaskPackageManifest(_session.Intake.PlanningWave, _session.TasksDir, milestoneNames, tasks);
    }

    private string RenderMilestoneIndex(List<PlannedMilestone> milestones)
    {
        var md = new StringBuilder("# Project Milestones\n\n");

        foreach (var milestone in milestones)
        {
            md.Append($"## {milestone.Name}\n\n");
            md.Append($"Goal: {milestone.Goal}\n\n");

            if (milestone.Issues.Count > 0)
            {
                md.Append("Tasks:\n\n");
                foreach (var issue in milestone.Issues)
                {
                    md.Append($"- {issue.Id} {issue.Title}\n");
                    foreach (var sub in issue.SubIssues)
                        md.Append($"  - {sub.Id} {sub.Title}\n");
                }
            }
            md.Append('\n');
        }

        return md.ToString();
    }

    private SortedDictionary<TaskId, string> GenerateTaskFiles(List<PlannedMilestone> milestones)
    {
        var taskFiles = new SortedDictionary<TaskId, string>();
        foreach (var milestone in milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                taskFiles[issue.Id] = RenderIssueTaskFile(issue, milestone);
                foreach (var sub in issue.SubIssues)
                    taskFiles[sub.Id] = RenderSubIssueTaskFile(sub, issue, milestone);
            }
        }
        return taskFiles;
    }

    private string RenderIssueTaskFile(PlannedIssue issue, PlannedMilestone milestone)
    {
        var content = $"""
---
id: {issue.Id}
title: "{YamlEscape(issue.Title)}"
milestone: "{YamlEscape(milestone.Name)}"
priority: {issue.Priority.AsLinearPriority()}
estimate: {issue.Estimate?.ToString() ?? "null"}
blockedBy: [{RenderIdList(issue.BlockedBy)}]
blocks: [{RenderIdList(issue.Blocks)}]
parent: null
---

## Summary

{CollapseMarkdownLine(issue.Summary)}

## Scope

### In scope

{RenderBullets(issue.ScopeIn)}

### Out of scope

{RenderOptionalBullets(issue.ScopeOut)}

## Deliverables

{RenderBullets(issue.Deliverables)}

## Acceptance Criteria

{RenderAcceptanceCriteria(issue.AcceptanceCriteria)}

## Test Plan

{RenderBullets(issue.VerificationSteps)}

## Context

{RenderBullets(issue.Context)}

## Definition of Ready

{RenderChecklist(issue.DefinitionOfReady)}

## Notes

{RenderNotes(issue.Notes)}
""";

        if (issue.SubIssues.Count > 0)
        {
            content += "\n## Sub-issues\n\n";
            foreach (var sub in issue.SubIssues)
                content += $"- {sub.Id} {sub.Title}\n";
        }

        return content;
    }

    private string RenderSubIssueTaskFile(PlannedSubIssue sub, PlannedIssue parentIssue, PlannedMilestone milestone)
    {
        return $"""
---
id: {sub.Id}
title: "{YamlEscape(sub.Title)}"
milestone: "{YamlEscape(milestone.Name)}"
priority: {sub.Priority.AsLinearPriority()}
estimate: {sub.Estimate?.ToString() ?? "null"}
blockedBy: [{RenderIdList(sub.BlockedBy)}]
blocks: [{RenderIdList(sub.Blocks)}]
parent: {parentIssue.Id}
---

## Summary

{CollapseMarkdownLine(sub.Summary)}

## Scope

### In scope

{RenderBullets(sub.ScopeIn)}

### Out of scope

{RenderOptionalBullets(sub.ScopeOut)}

## Deliverables

{RenderBullets(sub.Deliverables)}

## Acceptance Criteria

{RenderAcceptanceCriteria(sub.AcceptanceCriteria)}

## Test Plan

{RenderBullets(sub.VerificationSteps)}

## Context

{RenderBullets(sub.Context)}

## Definition of Ready

{RenderChecklist(sub.DefinitionOfReady)}

## Notes

{RenderNotes(sub.Notes)}
""";
    }

    private static string YamlEscape(string s)
    {
        var result = new StringBuilder(s.Length + 16);
        var startsWithComplexKeyMarker = s.StartsWith("? ");
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '\\': result.Append("\\\\"); break;
                case '"': result.Append("\\\""); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                case '*': result.Append("\\u002a"); break;
                case '&': result.Append("\\u0026"); break;
                case '?' when i == 0 && startsWithComplexKeyMarker: result.Append("\\u003f"); break;
                default:
                    if (c < 0x20)
                        result.Append($"\\u{(int)c:D4}");
                    else
                        result.Append(c);
                    break;
            }
        }
        return result.ToString();
    }

    private static string CollapseMarkdownLine(string s) => s.Replace("\n", "\\n").Replace("\r", "");

    private static string RenderIdList(List<TaskId> ids) => string.Join(", ", ids.Select(id => id.ToString()));

    private static string RenderBullets(List<string> items) =>
        string.Join("\n", items.Select(item => $"- {CollapseMarkdownLine(item)}"));

    private static string RenderOptionalBullets(List<string> items) =>
        items.Count == 0 ? "- None" : RenderBullets(items);

    private static string RenderAcceptanceCriteria(List<AcceptanceCriterion> criteria) =>
        string.Join("\n", criteria.Select(c => $"- [ ] {CollapseMarkdownLine(c.Description)}"));

    private static string RenderChecklist(List<string> items) =>
        string.Join("\n", items.Select(item => $"- [ ] {CollapseMarkdownLine(item)}"));

    private static string RenderNotes(string? notes) =>
        notes is not null ? CollapseMarkdownLine(notes) : "None";
}

file sealed class SubIssueGenerationContext
{
    public TaskId IssueId { get; }
    public string Requirement { get; }
    public string PlanningWave { get; }
    public List<string> Constraints { get; }
    public List<string> SuccessCriteria { get; }

    private SubIssueGenerationContext(TaskId issueId, string requirement, string planningWave, List<string> constraints, List<string> successCriteria)
    {
        IssueId = issueId;
        Requirement = requirement;
        PlanningWave = planningWave;
        Constraints = constraints;
        SuccessCriteria = successCriteria;
    }

    public static SubIssueGenerationContext FromIntake(TaskId issueId, string requirement, IntakeContext intake)
        => new(issueId, requirement, intake.PlanningWave, intake.Constraints, intake.SuccessCriteria);

    public List<string> ImplementationContext()
    {
        var context = new List<string>
        {
            $"Parent issue: {IssueId}",
            $"Planning wave: {PlanningWave}",
        };
        if (Constraints.Count > 0)
            context.Add($"Technical constraints: {string.Join(", ", Constraints)}");
        return context;
    }

    public List<string> ValidationContext()
    {
        var context = new List<string> { $"Validates implementation of {Requirement}" };
        if (SuccessCriteria.Count > 0)
            context.Add($"Success criteria: {string.Join("; ", SuccessCriteria)}");
        return context;
    }
}

/// Validates that a dependency graph has no cycles.
public static class DependencyGraphValidator
{
    public static Result<Unit, GenerationError> ValidateDependencyGraph(PlanArtifacts artifacts)
    {
        var depMap = BuildDependencyMap(artifacts);
        var visited = new SortedDictionary<TaskId, bool>();

        foreach (var milestone in artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                var r = ValidateTaskDependenciesWithMap(issue.Id, depMap, visited);
                if (r.IsErr) return r;
                foreach (var sub in issue.SubIssues)
                {
                    var sr = ValidateTaskDependenciesWithMap(sub.Id, depMap, visited);
                    if (sr.IsErr) return sr;
                }
            }
        }

        return Result<Unit, GenerationError>.Ok(Unit.Value);
    }

    private static SortedDictionary<TaskId, List<TaskId>> BuildDependencyMap(PlanArtifacts artifacts)
    {
        var map = new SortedDictionary<TaskId, SortedSet<TaskId>>();
        foreach (var milestone in artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                AddDependencyEdges(map, issue.Id, issue.BlockedBy, issue.Blocks);
                foreach (var sub in issue.SubIssues)
                    AddDependencyEdges(map, sub.Id, sub.BlockedBy, sub.Blocks);
            }
        }
        return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()).ToSortedDictionary();
    }

    private static void AddDependencyEdges(SortedDictionary<TaskId, SortedSet<TaskId>> map, TaskId taskId, List<TaskId> blockedBy, List<TaskId> blocks)
    {
        if (!map.TryGetValue(taskId, out var set))
        {
            set = new SortedSet<TaskId>();
            map[taskId] = set;
        }
        foreach (var b in blockedBy) set.Add(b);

        foreach (var blockedTask in blocks)
        {
            if (!map.TryGetValue(blockedTask, out var blockedSet))
            {
                blockedSet = new SortedSet<TaskId>();
                map[blockedTask] = blockedSet;
            }
            blockedSet.Add(taskId);
        }
    }

    private static Result<Unit, GenerationError> ValidateTaskDependenciesWithMap(TaskId taskId, SortedDictionary<TaskId, List<TaskId>> depMap, SortedDictionary<TaskId, bool> visited)
    {
        if (visited.TryGetValue(taskId, out var inProgress))
        {
            if (inProgress)
                return Result<Unit, GenerationError>.Err(new GenerationError(GenerationErrorKind.CircularDependency, $"Cycle detected involving task {taskId}"));
            return Result<Unit, GenerationError>.Ok(Unit.Value);
        }

        visited[taskId] = true;

        if (depMap.TryGetValue(taskId, out var deps))
        {
            foreach (var dep in deps)
            {
                var r = ValidateTaskDependenciesWithMap(dep, depMap, visited);
                if (r.IsErr) return r;
            }
        }

        visited[taskId] = false;
        return Result<Unit, GenerationError>.Ok(Unit.Value);
    }
}

file static class SortedDictionaryExtensions
{
    public static SortedDictionary<K, V> ToSortedDictionary<K, V>(this IEnumerable<KeyValuePair<K, V>> source)
        where K : notnull
    {
        var result = new SortedDictionary<K, V>();
        foreach (var kv in source) result[kv.Key] = kv.Value;
        return result;
    }
}
