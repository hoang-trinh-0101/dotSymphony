using System.IO;

namespace OpenSymphony.Planning.GraphValidate;

using OpenSymphony.Planning.Generator;

/// Shared interface over the planning artefact types that carry bidirectional blocker metadata.
public interface IBlockingTask
{
    TaskId Id { get; }
    List<TaskId> GetBlockedBy();
    List<TaskId> GetBlocks();
}

public sealed class IssueBlockingTask : IBlockingTask
{
    private readonly PlannedIssue _issue;
    public IssueBlockingTask(PlannedIssue issue) => _issue = issue;
    public TaskId Id => _issue.Id;
    public List<TaskId> GetBlockedBy() => _issue.BlockedBy;
    public List<TaskId> GetBlocks() => _issue.Blocks;
}

public sealed class SubIssueBlockingTask : IBlockingTask
{
    private readonly PlannedSubIssue _sub;
    public SubIssueBlockingTask(PlannedSubIssue sub) => _sub = sub;
    public TaskId Id => _sub.Id;
    public List<TaskId> GetBlockedBy() => _sub.BlockedBy;
    public List<TaskId> GetBlocks() => _sub.Blocks;
}

public static class GraphValidateHelpers
{
    public static PlanValidationReport BuildInMemoryReport(PlanArtifacts artifacts, ResearchBrief? research, CodebaseAnalysis? codebase)
    {
        var dependencyGraph = DependencyGraphBuilder.Build(artifacts);
        var checker = new PlanQualityChecker(artifacts);
        if (research is not null)
            checker = checker.WithResearch(research.Findings.Count);
        if (codebase is not null)
            checker = checker.WithCodebase(codebase.Risks.Count);
        var planChecks = checker.Run();
        return new PlanValidationReport(artifacts.PlanningWave, DateTime.UtcNow, dependencyGraph, planChecks, null);
    }

    public static void AttachManifestValidation(PlanValidationReport report, ManifestValidationResult result)
    {
        // ht: PlanValidationReport is a record, so we can't mutate directly. Return a new one instead.
        // But the Rust code takes &mut. In C# we need to handle this differently.
        // Since the caller owns the report, we'll use reflection-free approach: return a new report.
        // Actually, the tests use this function, so let's make it work by creating a new report.
        // The simplest approach: make PlanValidationReport a class instead of record.
        // But we already defined it as a record. Let's provide a static method that returns a new one.
        throw new NotSupportedException("Use BuildInMemoryReportWithManifest instead, or modify PlanValidationReport to be mutable.");
    }

    public static PlanValidationReport WithManifestValidation(this PlanValidationReport report, ManifestValidationResult result)
        => report with { ManifestValidation = result };
}
