using System.CommandLine;

namespace OpenSymphony.Cli.Memory;

/// <summary>
/// Memory query subcommand - search captured issue memory.
/// </summary>
public static class MemoryQuery
{
    public static Command Create()
    {
        var command = new Command("query", "Search captured issue memory");

        var queryArgument = new Argument<string>("query", "Search query");
        var limitOption = new Option<int>("--limit", () => 10, "Maximum results");
        var areaOption = new Option<string?>("--area", "Filter by area");
        var milestoneOption = new Option<string?>("--milestone", "Filter by milestone");
        var issueOption = new Option<string?>("--issue", "Filter by issue/work item");

        command.Add(queryArgument);
        command.Add(limitOption);
        command.Add(areaOption);
        command.Add(milestoneOption);
        command.Add(issueOption);

        command.SetHandler(async (context) =>
        {
            var query = context.ParseResult.GetValueForArgument(queryArgument);
            var limit = context.ParseResult.GetValueForOption(limitOption);
            var area = context.ParseResult.GetValueForOption(areaOption);
            var milestone = context.ParseResult.GetValueForOption(milestoneOption);
            var issue = context.ParseResult.GetValueForOption(issueOption);
            var cancellationToken = context.GetCancellationToken();

            var exitCode = await RunAsync(query, limit, area, milestone, issue, cancellationToken);
            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> RunAsync(string query, int limit, string? area, string? milestone, string? issue, CancellationToken cancellationToken)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var memoryDir = Path.Combine(currentDir, ".opensymphony", "memory");

            if (!Directory.Exists(memoryDir))
            {
                Console.WriteLine("Memory directory not found. Run 'opensymphony memory init' first.");
                return 1;
            }

            // ht: Placeholder for actual search logic
            // In the full implementation, this would:
            // 1. Load the memory index
            // 2. Search for matching capsules
            // 3. Return results

            Console.WriteLine($"Searching for: {query}");
            if (!string.IsNullOrEmpty(area)) Console.WriteLine($"Area: {area}");
            if (!string.IsNullOrEmpty(milestone)) Console.WriteLine($"Milestone: {milestone}");
            if (!string.IsNullOrEmpty(issue)) Console.WriteLine($"Issue: {issue}");
            Console.WriteLine($"Limit: {limit}");
            Console.WriteLine();
            Console.WriteLine("Search functionality not yet implemented in .NET port.");

            await Task.CompletedTask;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"memory query failed: {ex.Message}");
            return 1;
        }
    }
}