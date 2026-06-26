namespace OpenSymphony.Planning.Generator;

/// Unique identifier for a generated task.
public sealed record TaskId(string Value) : IComparable<TaskId>
{
    public static TaskId New(string id) => new(id);

    public int CompareTo(TaskId? other) =>
        other is null ? 1 : string.Compare(Value, other.Value, StringComparison.Ordinal);

    public override string ToString() => Value;
}

/// A criterion that must be satisfied for a task to be considered complete.
public sealed record AcceptanceCriterion(
    string Description,
    string? VerificationCommand);

/// Priority level compatible with Linear's numeric priority system.
public enum TaskPriority
{
    Urgent = 1,
    High = 2,
    Normal = 3,
    Low = 4,
}

public static class TaskPriorityExtensions
{
    public static byte AsLinearPriority(this TaskPriority priority) => (byte)priority;
}

/// A sub-issue represents a bounded implementation unit.
public sealed record PlannedSubIssue(
    TaskId Id,
    string Title,
    string Summary,
    List<string> ScopeIn,
    List<string> ScopeOut,
    List<string> Deliverables,
    List<AcceptanceCriterion> AcceptanceCriteria,
    List<string> VerificationSteps,
    List<string> Context,
    List<string> DefinitionOfReady,
    string? Notes,
    TaskPriority Priority,
    byte? Estimate,
    List<TaskId> BlockedBy,
    List<TaskId> Blocks,
    string? TaskFile);

/// An issue represents a demoable vertical capability or deliverable unit.
public sealed record PlannedIssue(
    TaskId Id,
    string Title,
    string Summary,
    List<string> ScopeIn,
    List<string> ScopeOut,
    List<string> Deliverables,
    List<AcceptanceCriterion> AcceptanceCriteria,
    List<string> VerificationSteps,
    List<string> Context,
    List<string> DefinitionOfReady,
    string? Notes,
    TaskPriority Priority,
    byte? Estimate,
    List<TaskId> BlockedBy,
    List<TaskId> Blocks,
    List<PlannedSubIssue> SubIssues,
    string? TaskFile);

/// A milestone represents a major delivery stage or checkpoint.
public sealed record PlannedMilestone(
    TaskId Id,
    string Name,
    string Goal,
    List<PlannedIssue> Issues,
    List<AcceptanceCriterion> AcceptanceCriteria,
    List<string> VerificationSteps,
    string? Notes);

/// A single task entry in the task package manifest.
public sealed record ManifestTask(TaskId Id, string File);

/// The task package manifest is the canonical machine-readable input for downstream Linear conversion.
public sealed record TaskPackageManifest(
    string PlanningWave,
    string TasksDir,
    List<string> Milestones,
    List<ManifestTask> Tasks);

/// Complete set of generated artifacts from a planning session.
public sealed record PlanArtifacts(
    DateTime GeneratedAt,
    string PlanningWave,
    List<PlannedMilestone> Milestones,
    TaskPackageManifest Manifest,
    string MilestoneIndex,
    SortedDictionary<TaskId, string> TaskFiles);

/// Scopes which artifacts should be regenerated.
public abstract record RegenerationScope
{
    public sealed record Full : RegenerationScope;
    public sealed record Milestones : RegenerationScope;
    public sealed record Issues(List<TaskId>? MilestoneIds) : RegenerationScope;
    public sealed record SubIssues(List<TaskId>? IssueIds) : RegenerationScope;
    public sealed record Manifest : RegenerationScope;
    public sealed record MilestoneIndex : RegenerationScope;

    public bool IncludesMilestones() => this is Full or Milestones;

    public bool IncludesIssues() => this is Full or Milestones or Issues;

    public bool IncludesSubIssues() => this is Full or Milestones or SubIssues;

    public bool IncludesManifest() =>
        this is Full or Manifest || IncludesMilestones() || IncludesIssues() || IncludesSubIssues();

    public bool IncludesMilestoneIndex() =>
        this is Full or MilestoneIndex || IncludesMilestones() || IncludesIssues() || IncludesSubIssues();

    public bool IncludesTaskFiles() =>
        this is Full or Milestones || IncludesIssues() || IncludesSubIssues();
}
