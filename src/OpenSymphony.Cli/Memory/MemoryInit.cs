using System.CommandLine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Cli.Memory;

/// <summary>
/// Memory init subcommand - create project memory configuration.
/// </summary>
public static class MemoryInit
{
    public static Command Create()
    {
        var command = new Command("init", "Create project memory configuration");

        var dryRunOption = new Option<bool>("--dry-run", "Only show the proposed memory configuration");
        var forceOption = new Option<bool>("--force", "Overwrite an existing memory configuration");

        command.Add(dryRunOption);
        command.Add(forceOption);

        command.SetHandler(async (context) =>
        {
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var force = context.ParseResult.GetValueForOption(forceOption);
            var cancellationToken = context.GetCancellationToken();

            var exitCode = await RunAsync(dryRun, force, cancellationToken);
            context.ExitCode = exitCode;
        });

        return command;
    }

    private static async Task<int> RunAsync(bool dryRun, bool force, CancellationToken cancellationToken)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var memoryDir = Path.Combine(currentDir, ".opensymphony", "memory");
            var configPath = Path.Combine(memoryDir, "config.yaml");

            if (File.Exists(configPath) && !force)
            {
                Console.WriteLine($"Memory configuration already exists at {configPath}");
                Console.WriteLine("Use --force to overwrite.");
                return 1;
            }

            var config = GenerateDefaultMemoryConfig();

            if (dryRun)
            {
                Console.WriteLine("Proposed memory configuration:");
                Console.WriteLine("---");
                Console.WriteLine(config);
                Console.WriteLine("---");
                return 0;
            }

            Directory.CreateDirectory(memoryDir);
            await File.WriteAllTextAsync(configPath, config, cancellationToken);
            Console.WriteLine($"Created memory configuration at {configPath}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"memory init failed: {ex.Message}");
            return 1;
        }
    }

    private static string GenerateDefaultMemoryConfig()
    {
        var config = new Dictionary<string, object?>
        {
            ["root"] = "./memory",
            ["visibility"] = "private",
            ["index"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["path"] = "./memory/index.db"
            }
        };

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        return serializer.Serialize(config);
    }
}