using System.CommandLine;

namespace OpenSymphony.Cli;

/// <summary>
/// Update the installed CLI and refresh template-managed skills.
/// </summary>
public static class UpdateRepoCommand
{
    public static Command Create()
    {
        var command = new Command("update", "Update the installed CLI and refresh template-managed skills");
        command.SetHandler(async (context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            await RunAsync(cancellationToken);
        });
        return command;
    }

    private static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Updating OpenSymphony from {currentDir}");

            // ht: .NET uses dotnet tool update, not cargo install
            // Skip self-update for .NET port

            var targetRepo = DetectTargetRepoMarkers(currentDir);
            if (!targetRepo.looks_like_target_repo)
            {
                var missing = targetRepo.missing_markers;
                Console.WriteLine($"Skipped template skill refresh because this directory is missing {string.Join(", ", missing)}.");
                Console.WriteLine("OpenSymphony update complete.");
                return 0;
            }

            Console.WriteLine("Detected an OpenSymphony target repo; refreshing template-managed skill files.");
            var report = await SyncTemplateSkillsAsync(currentDir, cancellationToken);
            
            // TODO: Call memory initialization when Memory project has ensure_memory_initialized
            // var memoryReport = await Memory.EnsureMemoryInitialized(currentDir, null);

            Console.WriteLine("Skill refresh summary:");
            PrintPaths("Created", report.created);
            PrintPaths("Updated", report.updated);
            Console.WriteLine($"Unchanged: {report.unchanged_count} file(s)");
            // TODO: Print memory init summary
            Console.WriteLine("OpenSymphony update complete.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"opensymphony update failed: {ex.Message}");
            return 1;
        }
    }

    private static (bool looks_like_target_repo, List<string> missing_markers) DetectTargetRepoMarkers(string currentDir)
    {
        var hasWorkflow = File.Exists(Path.Combine(currentDir, "WORKFLOW.md"));
        var hasConfig = File.Exists(Path.Combine(currentDir, "config.yaml"));
        var missing = new List<string>();
        if (!hasWorkflow) missing.Add("WORKFLOW.md");
        if (!hasConfig) missing.Add("config.yaml");
        return (hasWorkflow && hasConfig, missing);
    }

    private static async Task<SkillSyncReport> SyncTemplateSkillsAsync(string currentDir, CancellationToken cancellationToken)
    {
        // ht: Placeholder for template sync - in Rust this fetches from GitHub
        // For .NET port, we'll implement a simple version that checks .agents/skills directory
        var skillsDir = Path.Combine(currentDir, ".agents", "skills");
        var report = new SkillSyncReport();
        
        if (!Directory.Exists(skillsDir))
        {
            Directory.CreateDirectory(skillsDir);
            report.created.Add(skillsDir);
        }
        else
        {
            report.unchanged_count = 1;
        }

        await Task.CompletedTask;
        return report;
    }

    private static void PrintPaths(string label, List<string> paths)
    {
        if (paths.Count == 0) return;
        Console.WriteLine($"{label}:");
        foreach (var path in paths)
        {
            Console.WriteLine($"  {path}");
        }
    }

    private class SkillSyncReport
    {
        public List<string> created = new();
        public List<string> updated = new();
        public int unchanged_count;
    }
}