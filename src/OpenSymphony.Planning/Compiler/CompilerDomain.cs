namespace OpenSymphony.Planning.Compiler;

using OpenSymphony.Planning.Generator;

public enum TaskKind { Milestone, Issue, SubIssue }

public static class TaskKindExtensions
{
    public static bool IsPublishable(this TaskKind kind) => kind == TaskKind.Issue || kind == TaskKind.SubIssue;
}

public enum ValidationSeverity { Error, Warning }

public enum DependencyRelation { Blocks, ParentOf }

public sealed record TaxonomyViolation(
    TaskId? TaskId,
    TaskKind? TaskKind,
    string Reason,
    string Actionable);

public sealed record ValidationMessage(
    ValidationSeverity Severity,
    TaskId? TaskId,
    string Field,
    string Message)
{
    public static ValidationMessage Error(TaskId? taskId, string field, string message)
        => new(ValidationSeverity.Error, taskId, field, message);

    public static ValidationMessage Warning(TaskId? taskId, string field, string message)
        => new(ValidationSeverity.Warning, taskId, field, message);
}

public sealed record UnderspecifiedSubIssue(
    TaskId SubIssueId,
    TaskId ParentIssueId,
    int AcceptanceCriteriaCount,
    int VerificationStepsCount,
    int DeliverablesCount,
    int ScopeInCount,
    List<string> Reasons);

public sealed record LinearPublishEntity(
    TaskId SourceTaskId,
    string SourceFile,
    TaskKind LinearKind,
    string LinearMilestone,
    TaskId? ParentTaskId,
    List<TaskId> BlockedBy,
    List<TaskId> Blocks,
    List<string> ReviewComments,
    string? Issue,
    string? IssueId,
    string? Url);

public sealed record MilestoneReceipt(
    string Name,
    string? MilestoneId,
    List<TaskId> LinkedIssues);

public sealed record LinearPublishReceipt(
    string PlanningWave,
    string? LinearProject,
    DateTime? PublishedAt,
    SortedDictionary<string, MilestoneReceipt> Milestones,
    SortedDictionary<TaskId, LinearPublishEntity> Tasks);

public sealed record AppliedHierarchy(
    string PlanningWave,
    List<CompiledMilestone> Milestones);

public sealed record CompiledMilestone(
    string Name,
    string Goal,
    string? Notes,
    List<CompiledIssue> Issues);

public sealed record CompiledIssue(
    TaskId TaskId,
    string Title,
    string Summary,
    string SourceFile,
    string Milestones,
    byte Priority,
    byte? Estimate,
    List<TaskId> BlockedBy,
    List<TaskId> Blocks,
    int AcceptanceCriteriaCount,
    List<string> AcceptanceCriteriaDescriptions,
    int VerificationCount,
    List<CompiledSubIssue> SubIssues);

public sealed record CompiledSubIssue(
    TaskId TaskId,
    string Title,
    string Summary,
    string SourceFile,
    TaskId ParentTaskId,
    string Milestones,
    byte Priority,
    byte? Estimate,
    List<TaskId> BlockedBy,
    List<TaskId> Blocks,
    int AcceptanceCriteriaCount,
    int VerificationCount,
    List<string> VerificationSteps,
    List<string> UnderspecifiedReasons);

public sealed record DependencyEdge(
    TaskId Source,
    TaskId Target,
    string Milestone,
    DependencyRelation Relation);

public sealed record DependencyMetadata(
    string PlanningWave,
    int TotalNodes,
    int MilestoneCount,
    int IssueCount,
    int SubIssueCount,
    List<DependencyEdge> Edges);

public sealed record CompilationResult(
    string PlanningWave,
    string ManifestYaml,
    string PublishReceiptYaml,
    AppliedHierarchy AppliedHierarchy,
    List<TaxonomyViolation> TaxonomyViolations,
    List<ValidationMessage> ValidationMessages,
    List<UnderspecifiedSubIssue> UnderspecifiedSubIssues,
    DependencyMetadata DependencyMetadata)
{
    public bool IsPublishable() =>
        TaxonomyViolations.Count == 0 &&
        ValidationMessages.All(m => m.Severity != ValidationSeverity.Error);
}

public static class CompilerDomainHelpers
{
    public static CompiledIssue IssueToCompiled(PlannedIssue issue, string milestoneName)
        => new(
            issue.Id,
            issue.Title,
            issue.Summary,
            issue.TaskFile ?? "",
            milestoneName,
            issue.Priority.AsLinearPriority(),
            issue.Estimate,
            new List<TaskId>(issue.BlockedBy),
            new List<TaskId>(issue.Blocks),
            issue.AcceptanceCriteria.Count,
            issue.AcceptanceCriteria.Select(c => c.Description).ToList(),
            issue.VerificationSteps.Count,
            issue.SubIssues.Select(s => SubIssueToCompiled(s, issue.Id, milestoneName)).ToList());

    public static CompiledSubIssue SubIssueToCompiled(PlannedSubIssue sub, TaskId parentTaskId, string milestoneName)
    {
        var underspecifiedReasons = ClassifyUnderspecifiedSubIssue(sub);
        return new CompiledSubIssue(
            sub.Id,
            sub.Title,
            sub.Summary,
            sub.TaskFile ?? "",
            parentTaskId,
            milestoneName,
            sub.Priority.AsLinearPriority(),
            sub.Estimate,
            new List<TaskId>(sub.BlockedBy),
            new List<TaskId>(sub.Blocks),
            sub.AcceptanceCriteria.Count,
            sub.VerificationSteps.Count,
            new List<string>(sub.VerificationSteps),
            underspecifiedReasons);
    }

    public static List<string> ClassifyUnderspecifiedSubIssue(PlannedSubIssue sub)
    {
        var reasons = new List<string>();
        if (sub.AcceptanceCriteria.Count == 0) reasons.Add("missing acceptance criteria");
        if (sub.VerificationSteps.Count == 0) reasons.Add("missing verification expectations");
        if (sub.Deliverables.Count == 0) reasons.Add("missing deliverables");
        if (sub.ScopeIn.Count == 0) reasons.Add("missing in-scope items");
        return reasons;
    }
}
