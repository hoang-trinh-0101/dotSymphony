namespace OpenSymphony.Planning.Generator;

/// Intake captures the initial requirements, constraints, and goals gathered from human-AI collaboration sessions.
public sealed record IntakeContext(
    string PlanningWave,
    string ProjectDescription,
    List<string> SuccessCriteria,
    List<string> Requirements,
    List<string> Constraints,
    List<string> OpenQuestions,
    List<string> ReferenceDocs);

/// Complete context for a planning session.
public sealed class PlanningSession
{
    public IntakeContext Intake { get; set; }
    public CodebaseAnalysis? CodebaseAnalysis { get; set; }
    public LinearGraphAnalysis? LinearGraphAnalysis { get; set; }
    public ResearchArtifactStore? Research { get; set; }
    public string TasksDir { get; set; }

    public PlanningSession(IntakeContext intake, string tasksDir)
    {
        Intake = intake;
        TasksDir = tasksDir;
    }

    public PlanningSession WithCodebaseAnalysis(CodebaseAnalysis analysis)
    {
        CodebaseAnalysis = analysis;
        return this;
    }

    public PlanningSession WithLinearGraphAnalysis(LinearGraphAnalysis analysis)
    {
        LinearGraphAnalysis = analysis;
        return this;
    }

    public PlanningSession WithResearch(ResearchArtifactStore research)
    {
        Research = research;
        return this;
    }

    public bool IsComplete() =>
        CodebaseAnalysis is not null && LinearGraphAnalysis is not null && Research is not null;

    public string ContextSummary()
    {
        var summary = $"Planning wave: {Intake.PlanningWave}\n";
        summary += $"Requirements: {Intake.Requirements.Count} items\n";
        summary += $"Constraints: {Intake.Constraints.Count} items\n";
        summary += $"Codebase analysis: {(CodebaseAnalysis is not null ? "available" : "missing")}\n";
        summary += $"Linear graph analysis: {(LinearGraphAnalysis is not null ? "available" : "missing")}\n";
        summary += $"Research artifacts: {Research?.Len() ?? 0}\n";
        return summary;
    }
}
