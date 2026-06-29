using System.CommandLine;
using OpenSymphony.Workflow;
using YamlDotNet.Serialization;

namespace OpenSymphony.Cli;

/// <summary>
/// Smart rehydration: recreate conversations with history preservation.
/// ht: Port of older/crates/opensymphony-cli/src/lib.rs Rehydrate command handler.
/// </summary>
public static class RehydrateCommand
{
    public static Command Create()
    {
        var command = new Command("rehydrate", "Smart rehydration: recreate conversations with history preservation");

        var issueArgument = new Argument<string>("issue", "Issue identifier to rehydrate (e.g., COE-123)");
        var reasonOption = new Option<string>("--reason", () => "manual rehydration via CLI", "Reason for rehydration");
        var maxSummaryEventsOption = new Option<int>("--max-summary-events", () => 50, "Maximum events to include in summary");
        var noSummaryOption = new Option<bool>("--no-summary", "Skip summarization (faster, but no context preserved)");

        command.Add(issueArgument);
        command.Add(reasonOption);
        command.Add(maxSummaryEventsOption);
        command.Add(noSummaryOption);

        command.SetHandler(async (issue, reason, maxSummaryEvents, noSummary) =>
        {
            var exitCode = await RunRehydrateAsync(issue, reason, maxSummaryEvents, noSummary);
            Environment.Exit(exitCode);
        }, issueArgument, reasonOption, maxSummaryEventsOption, noSummaryOption);

        return command;
    }

    static async Task<int> RunRehydrateAsync(
        string issue,
        string reason,
        int maxSummaryEvents,
        bool noSummary)
    {
        try
        {
            Console.WriteLine($"Rehydrating issue: {issue}");
            Console.WriteLine($"Reason: {reason}");
            Console.WriteLine($"Max summary events: {maxSummaryEvents}");
            Console.WriteLine($"Skip summary: {noSummary}");
            Console.WriteLine();

            // Load workflow
            var cwd = Directory.GetCurrentDirectory();
            var workflowPath = Path.Combine(cwd, "WORKFLOW.md");
            var workflowResult = WorkflowLoader.LoadWorkflowFromPath(workflowPath);

            if (workflowResult.IsErr)
            {
                Console.Error.WriteLine($"Failed to load workflow: {workflowResult.Error}");
                return 1;
            }

            var resolvedResult = WorkflowResolver.ResolveWorkflow(
                workflowResult.Value,
                cwd,
                new ProcessEnvironment());

            if (resolvedResult.IsErr)
            {
                Console.Error.WriteLine($"Failed to resolve workflow: {resolvedResult.Error}");
                return 1;
            }

            // ht: Placeholder for actual rehydration logic
            // Full implementation would:
            // 1. Load conversation store
            // 2. Find conversation for the issue
            // 3. Summarize events (unless no-summary)
            // 4. Create new conversation with summary as context
            // 5. Update conversation store

            Console.WriteLine("Rehydration logic not yet fully implemented");
            Console.WriteLine("This requires conversation store and OpenHands client integration");

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rehydration failed: {ex}");
            return 1;
        }
    }
}

// Rehydrate config types
public sealed class RehydrateConfig
{
    [YamlMember(Alias = "target_repo", ApplyNamingConventions = false)]
    public string? TargetRepo { get; init; }

    [YamlMember(Alias = "openhands", ApplyNamingConventions = false)]
    public RehydrateOpenHandsConfig OpenHands { get; init; } = new();
}

public sealed class RehydrateOpenHandsConfig
{
    [YamlMember(Alias = "tool_dir", ApplyNamingConventions = false)]
    public string? ToolDir { get; init; }
}