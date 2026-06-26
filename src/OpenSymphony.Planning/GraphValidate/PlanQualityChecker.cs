namespace OpenSymphony.Planning.GraphValidate;

using OpenSymphony.Planning.Generator;

public sealed class PlanQualityChecker
{
    private readonly PlanArtifacts _artifacts;
    private int? _researchFindingCount;
    private int? _codebaseRiskCount;

    public PlanQualityChecker(PlanArtifacts artifacts)
    {
        _artifacts = artifacts;
    }

    public PlanQualityChecker WithResearch(int findingCount)
    {
        _researchFindingCount = findingCount;
        return this;
    }

    public PlanQualityChecker WithCodebase(int riskCount)
    {
        _codebaseRiskCount = riskCount;
        return this;
    }

    public List<PlanCheckFinding> Run()
    {
        var findings = new List<PlanCheckFinding>();
        CheckScopeClarity(findings);
        CheckResearchCoverage(findings);
        CheckCodebaseAnalysis(findings);
        CheckDependencyCycle(findings);
        CheckMissingInverseBlockers(findings);
        CheckAcceptanceCriteria(findings);
        CheckVerificationExpectations(findings);

        findings.Sort((a, b) =>
        {
            var c = a.Category.CompareTo(b.Category);
            if (c != 0) return c;
            c = SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity));
            if (c != 0) return c;
            c = CompareNullable(a.TaskId, b.TaskId);
            if (c != 0) return c;
            return string.Compare(a.Field, b.Field, StringComparison.Ordinal);
        });
        return findings;
    }

    private void CheckScopeClarity(List<PlanCheckFinding> findings)
    {
        if (_artifacts.Milestones.Count == 0)
            findings.Add(PlanCheckFinding.Error(PlanCheckCategory.ScopeClarity, null, "milestones", "Plan contains no milestones; expected at least one milestone"));

        foreach (var milestone in _artifacts.Milestones)
        {
            if (string.IsNullOrWhiteSpace(milestone.Name))
                findings.Add(PlanCheckFinding.Error(PlanCheckCategory.ScopeClarity, milestone.Id, "milestones", $"milestone {milestone.Id} has an empty name"));
            if (milestone.Issues.Count == 0)
                findings.Add(PlanCheckFinding.Warning(PlanCheckCategory.ScopeClarity, milestone.Id, "issues", $"milestone '{milestone.Name}' has no issues; expected at least one issue"));
            foreach (var issue in milestone.Issues)
            {
                if (issue.ScopeIn.Count == 0)
                    findings.Add(PlanCheckFinding.Warning(PlanCheckCategory.ScopeClarity, issue.Id, "scope.in", $"issue '{issue.Title}' has no in-scope items; expected at least one bullet"));
            }
        }
    }

    private void CheckResearchCoverage(List<PlanCheckFinding> findings)
    {
        if (_researchFindingCount == 0)
            findings.Add(PlanCheckFinding.Warning(PlanCheckCategory.ResearchCoverage, null, "research.findings", "Planning session reports zero research findings; downstream consumers may not be able to trace plan decisions back to research citations"));
    }

    private void CheckCodebaseAnalysis(List<PlanCheckFinding> findings)
    {
        if (_codebaseRiskCount == 0)
            findings.Add(PlanCheckFinding.Warning(PlanCheckCategory.CodebaseAnalysis, null, "codebase.risks", "Planning session loaded a CodebaseAnalysis with zero risks; rerun the analyzer to confirm the repository has no ownership or integration risks"));
    }

    private void CheckDependencyCycle(List<PlanCheckFinding> findings)
    {
        var result = DependencyGraphValidator.ValidateDependencyGraph(_artifacts);
        if (result.IsErr)
            findings.Add(PlanCheckFinding.Error(PlanCheckCategory.Dependencies, null, "dependencies", result.Error.Message));
    }

    private void CheckMissingInverseBlockers(List<PlanCheckFinding> findings)
    {
        var inverse = BlockingTaskHelpers.BuildBlockerInverse(_artifacts);
        foreach (var milestone in _artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                CheckTaskBlockerInverse(issue, milestone, inverse, findings);
                foreach (var sub in issue.SubIssues)
                    CheckTaskBlockerInverse(sub, milestone, inverse, findings);
            }
        }
    }

    private void CheckAcceptanceCriteria(List<PlanCheckFinding> findings)
    {
        foreach (var milestone in _artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                if (issue.AcceptanceCriteria.Count == 0)
                    findings.Add(PlanCheckFinding.Error(PlanCheckCategory.AcceptanceCriteria, issue.Id, "acceptance_criteria", $"issue '{issue.Title}' has no acceptance criteria; expected at least one"));
                foreach (var sub in issue.SubIssues)
                {
                    if (sub.AcceptanceCriteria.Count == 0)
                        findings.Add(PlanCheckFinding.Warning(PlanCheckCategory.AcceptanceCriteria, sub.Id, "acceptance_criteria", $"sub-issue '{sub.Title}' has no acceptance criteria; expected at least one"));
                }
            }
        }
    }

    private void CheckVerificationExpectations(List<PlanCheckFinding> findings)
    {
        foreach (var milestone in _artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                foreach (var sub in issue.SubIssues)
                {
                    if (sub.VerificationSteps.Count == 0)
                        findings.Add(PlanCheckFinding.Error(PlanCheckCategory.VerificationExpectations, sub.Id, "verification_steps", $"sub-issue '{sub.Title}' has no verification expectations; expected at least one"));
                }
            }
        }
    }

    private static void CheckTaskBlockerInverse(IBlockingTask task, PlannedMilestone milestone, SortedDictionary<TaskId, SortedSet<TaskId>> inverse, List<PlanCheckFinding> findings)
    {
        foreach (var target in task.GetBlocks())
        {
            var reciprocal = inverse.TryGetValue(target, out var set) ? set : new SortedSet<TaskId>();
            if (!reciprocal.Contains(task.Id))
                findings.Add(PlanCheckFinding.Warning(PlanCheckCategory.Dependencies, task.Id, "blocks", $"task '{task.Id}' in milestone '{milestone.Name}' claims to block '{target}' but the inverse 'blockedBy' arrow is missing on '{target}'"));
        }
    }

    private static int SeverityRank(PlanCheckSeverity severity) => severity switch
    {
        PlanCheckSeverity.Error => 0,
        PlanCheckSeverity.Warning => 1,
        _ => 2,
    };

    private static int CompareNullable(TaskId? a, TaskId? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        return a.CompareTo(b);
    }
}

public static class BlockingTaskHelpers
{
    public static SortedDictionary<TaskId, SortedSet<TaskId>> BuildBlockerInverse(PlanArtifacts artifacts)
    {
        var inverse = new SortedDictionary<TaskId, SortedSet<TaskId>>();
        foreach (var milestone in artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                CollectInverse(inverse, issue.Id, issue.BlockedBy);
                foreach (var sub in issue.SubIssues)
                    CollectInverse(inverse, sub.Id, sub.BlockedBy);
            }
        }
        return inverse;
    }

    private static void CollectInverse(SortedDictionary<TaskId, SortedSet<TaskId>> inverse, TaskId taskId, List<TaskId> blockedBy)
    {
        foreach (var blocker in blockedBy)
        {
            if (!inverse.TryGetValue(blocker, out var set))
            {
                set = new SortedSet<TaskId>();
                inverse[blocker] = set;
            }
            set.Add(taskId);
        }
    }

    public static List<List<TaskId>> CreationOrderWaves(PlanArtifacts artifacts)
    {
        var dependencyMap = new SortedDictionary<TaskId, SortedSet<TaskId>>();
        foreach (var milestone in artifacts.Milestones)
        {
            foreach (var issue in milestone.Issues)
            {
                if (!dependencyMap.TryGetValue(issue.Id, out var set))
                {
                    set = new SortedSet<TaskId>();
                    dependencyMap[issue.Id] = set;
                }
                foreach (var b in issue.BlockedBy) set.Add(b);
                foreach (var sub in issue.SubIssues)
                {
                    if (!dependencyMap.TryGetValue(sub.Id, out var subSet))
                    {
                        subSet = new SortedSet<TaskId>();
                        dependencyMap[sub.Id] = subSet;
                    }
                    foreach (var b in sub.BlockedBy) subSet.Add(b);
                }
            }
        }
        return TopoWaves(dependencyMap);
    }

    private static List<List<TaskId>> TopoWaves(SortedDictionary<TaskId, SortedSet<TaskId>> dependencyMap)
    {
        var remaining = new SortedDictionary<TaskId, SortedSet<TaskId>>(dependencyMap, TaskIdComparer.Instance);
        var waves = new List<List<TaskId>>();
        while (remaining.Count > 0)
        {
            var current = remaining
                .Where(kv => kv.Value.Count == 0)
                .Select(kv => kv.Key)
                .ToList();
            if (current.Count == 0) break; // Cycle remains
            foreach (var taskId in current)
                remaining.Remove(taskId);
            foreach (var deps in remaining.Values)
                deps.RemoveWhere(dep => current.Contains(dep));
            waves.Add(current);
        }
        return waves;
    }

    private sealed class TaskIdComparer : IComparer<TaskId>
    {
        public static readonly TaskIdComparer Instance = new();
        public int Compare(TaskId? x, TaskId? y) =>
            (x, y) switch
            {
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                _ => x.CompareTo(y),
            };
    }
}
