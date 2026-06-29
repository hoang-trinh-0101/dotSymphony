using System.CommandLine;
using OpenSymphony.Memory;

namespace OpenSymphony.Cli.Memory;

/// <summary>
/// Memory import-okf subcommand - import an OKF memory bundle.
/// </summary>
public static class MemoryImport
{
    public static Command Create()
    {
        var command = new Command("import-okf", "Import an OKF memory bundle");

        var bundleArgument = new Argument<string>("bundle-root", "Path to the OKF bundle root directory");
        var forceOption = new Option<bool>("--force", "Overwrite existing capsules");

        command.Add(bundleArgument);
        command.Add(forceOption);

        command.SetHandler(async (context) =>
        {
            var bundleRoot = context.ParseResult.GetValueForArgument(bundleArgument);
            var force = context.ParseResult.GetValueForOption(forceOption);
            var cancellationToken = context.GetCancellationToken();

            var exitCode = await RunAsync(bundleRoot, force, cancellationToken);
            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> RunAsync(string bundleRoot, bool force, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(bundleRoot))
            {
                Console.WriteLine($"Bundle directory not found: {bundleRoot}");
                return 1;
            }

            var currentDir = Directory.GetCurrentDirectory();
            var memoryDir = Path.Combine(currentDir, ".opensymphony", "memory");

            if (!Directory.Exists(memoryDir))
            {
                Console.WriteLine("Memory directory not found. Run 'opensymphony memory init' first.");
                return 1;
            }

            // ht: Preflight - check for conflicts
            var bundleFiles = Directory.GetFiles(bundleRoot, "*.md", SearchOption.AllDirectories);
            var conflicts = new List<string>();

            foreach (var bundleFile in bundleFiles)
            {
                var relativePath = Path.GetRelativePath(bundleRoot, bundleFile);
                var targetPath = Path.Combine(memoryDir, relativePath);

                if (File.Exists(targetPath))
                {
                    conflicts.Add(relativePath);
                }
            }

            if (conflicts.Count > 0 && !force)
            {
                Console.WriteLine("The following files would be overwritten:");
                foreach (var conflict in conflicts)
                {
                    Console.WriteLine($"  {conflict}");
                }
                Console.WriteLine();
                Console.WriteLine("Use --force to overwrite existing files.");
                return 1;
            }

            // ht: Placeholder for actual import logic
            // In the full implementation, this would:
            // 1. Parse each OKF file in the bundle
            // 2. Validate frontmatter
            // 3. Preserve unknown concept types and extra fields
            // 4. Copy to memory directory
            // 5. Reindex memory

            Console.WriteLine($"Importing OKF bundle from {bundleRoot}");
            if (force) Console.WriteLine("Force mode: overwriting existing files");
            Console.WriteLine();
            Console.WriteLine($"Found {bundleFiles.Length} files in bundle");
            if (conflicts.Count > 0) Console.WriteLine($"Would overwrite {conflicts.Count} existing files");
            Console.WriteLine();
            Console.WriteLine("Import functionality not yet fully implemented in .NET port.");

            await Task.CompletedTask;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"memory import-okf failed: {ex.Message}");
            return 1;
        }
    }
}