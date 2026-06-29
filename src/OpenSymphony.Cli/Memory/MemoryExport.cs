using System.CommandLine;
using OpenSymphony.Memory;

namespace OpenSymphony.Cli.Memory;

/// <summary>
/// Memory export-okf subcommand - export an OKF memory bundle.
/// </summary>
public static class MemoryExport
{
    public static Command Create()
    {
        var command = new Command("export-okf", "Export an OKF memory bundle");

        var outputOption = new Option<string?>("--output", "Output directory for the bundle");
        var visibilityOption = new Option<MemoryVisibility?>("--visibility", "Visibility level (public or private)");

        command.Add(outputOption);
        command.Add(visibilityOption);

        command.SetHandler(async (context) =>
        {
            var output = context.ParseResult.GetValueForOption(outputOption);
            var visibility = context.ParseResult.GetValueForOption(visibilityOption) ?? MemoryVisibility.Private;
            var cancellationToken = context.GetCancellationToken();

            var exitCode = await RunAsync(output, visibility, cancellationToken);
            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> RunAsync(string? output, MemoryVisibility visibility, CancellationToken cancellationToken)
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

            var outputDir = output ?? Path.Combine(currentDir, "okf-export");

            if (Directory.Exists(outputDir))
            {
                var files = Directory.GetFiles(outputDir);
                if (files.Length > 0)
                {
                    Console.WriteLine($"Output directory {outputDir} is not empty.");
                    Console.WriteLine("Please specify an empty or non-existent directory.");
                    return 1;
                }
            }
            else
            {
                Directory.CreateDirectory(outputDir);
            }

            // ht: Placeholder for actual export logic
            // In the full implementation, this would:
            // 1. Load all memory capsules
            // 2. Filter by visibility if public
            // 3. Run lint checks
            // 4. Copy files to output directory
            // 5. Generate bundle manifest

            Console.WriteLine($"Exporting OKF bundle to {outputDir}");
            Console.WriteLine($"Visibility: {visibility}");
            Console.WriteLine();
            Console.WriteLine("Export functionality not yet fully implemented in .NET port.");
            Console.WriteLine("OKF bundle structure created at output directory.");

            // Create a placeholder manifest
            var manifestPath = Path.Combine(outputDir, "MANIFEST.md");
            await File.WriteAllTextAsync(manifestPath, $"# OKF Bundle Export\n\nVisibility: {visibility}\nExported: {DateTimeOffset.UtcNow:O}\n", cancellationToken);

            await Task.CompletedTask;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"memory export-okf failed: {ex.Message}");
            return 1;
        }
    }
}