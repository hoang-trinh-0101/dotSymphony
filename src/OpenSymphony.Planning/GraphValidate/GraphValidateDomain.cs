namespace OpenSymphony.Planning.GraphValidate;

using OpenSymphony.Planning.Generator;

public enum GraphNodeKind { Milestone, Issue, SubIssue }

public enum GraphEdgeReason { ParentOf, BlockedBy, BlocksInvariant, MissingInverse, UnknownTarget }

public sealed record GraphNode(
    TaskId Id,
    GraphNodeKind Kind,
    string Title,
    string Milestone,
    int AcceptanceCriteriaCount,
    int VerificationCount,
    string? SourceArtifactRef);

public sealed record GraphEdge(
    TaskId From,
    TaskId To,
    GraphEdgeReason Relation,
    string Milestone,
    string? SourceArtifactRef);

public sealed record DependencyGraph(
    string PlanningWave,
    DateTime GeneratedAt,
    List<GraphNode> Nodes,
    List<GraphEdge> Edges,
    List<List<TaskId>> ParallelizableWaves);

public enum PlanCheckSeverity { Error, Warning }

public enum PlanCheckCategory { ScopeClarity, ResearchCoverage, CodebaseAnalysis, Dependencies, AcceptanceCriteria, VerificationExpectations }

public sealed record PlanCheckFinding(
    PlanCheckSeverity Severity,
    PlanCheckCategory Category,
    TaskId? TaskId,
    string Field,
    string Message)
{
    public static PlanCheckFinding Error(PlanCheckCategory category, TaskId? taskId, string field, string message)
        => new(PlanCheckSeverity.Error, category, taskId, field, message);

    public static PlanCheckFinding Warning(PlanCheckCategory category, TaskId? taskId, string field, string message)
        => new(PlanCheckSeverity.Warning, category, taskId, field, message);
}

public sealed record MissingTaskFile(TaskId TaskId, string FilePath);

public sealed record InvalidTaskFile(TaskId TaskId, string FilePath, string Reason);

public sealed record UnknownMilestone(TaskId TaskId, string DeclaredMilestone);

public sealed record UnknownDependency(TaskId FromTaskId, TaskId UnknownDependency);

public sealed record SelfBlock(TaskId TaskId);

public sealed record ManifestValidationResult(
    string PlanningWave,
    List<TaskId> DeclaredTaskIds,
    List<MissingTaskFile> MissingTaskFiles,
    List<InvalidTaskFile> InvalidTaskFiles,
    List<UnknownMilestone> UnknownMilestones,
    List<UnknownDependency> UnknownDependencies,
    List<List<TaskId>> CreationOrderCycles,
    List<SelfBlock> SelfBlocks,
    List<TaskId> DuplicateTaskIds)
{
    public bool IsOk() =>
        MissingTaskFiles.Count == 0 && InvalidTaskFiles.Count == 0 &&
        UnknownMilestones.Count == 0 && UnknownDependencies.Count == 0 &&
        CreationOrderCycles.Count == 0 && SelfBlocks.Count == 0 &&
        DuplicateTaskIds.Count == 0;

    public int ErrorCount() =>
        MissingTaskFiles.Count + InvalidTaskFiles.Count +
        UnknownMilestones.Count + UnknownDependencies.Count +
        CreationOrderCycles.Sum(c => c.Count) +
        SelfBlocks.Count + DuplicateTaskIds.Count;
}

public sealed record PlanValidationReport(
    string PlanningWave,
    DateTime GeneratedAt,
    DependencyGraph? DependencyGraph,
    List<PlanCheckFinding> PlanChecks,
    ManifestValidationResult? ManifestValidation)
{
    public bool HasErrors(Func<PlanCheckSeverity, bool> severityFilter) =>
        PlanChecks.Any(c => severityFilter(c.Severity)) ||
        (ManifestValidation is { } m && !m.IsOk());

    public SortedDictionary<PlanCheckCategory, int> CategoryCounts()
    {
        var counts = new SortedDictionary<PlanCheckCategory, int>();
        foreach (var finding in PlanChecks)
        {
            counts.TryGetValue(finding.Category, out var c);
            counts[finding.Category] = c + 1;
        }
        return counts;
    }
}
