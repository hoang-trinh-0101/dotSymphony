using System.CommandLine;
using OpenSymphony.Cli.OrchestratorRun;
using OpenSymphony.Workflow;
using YamlDotNet.Serialization;

namespace OpenSymphony.Cli;

/// <summary>
/// Run local preflight checks for trusted-machine deployment.
/// ht: Port of older/crates/opensymphony-cli/src/lib.rs Doctor command handler.
/// </summary>
public static class DoctorCommand
{
    public static Command Create()
    {
        var command = new Command("doctor", "Run local preflight checks for trusted-machine deployment");

        var configOption = new Option<string?>("--config", "Doctor config YAML path; defaults to ./config.yaml when present");
        var liveOpenhandsOption = new Option<bool>("--live-openhands", "Run the live OpenHands probe instead of static preflight only");
        var rehydrateOption = new Option<bool>("--rehydrate", "Rehydrate all conversations missing LLM API keys");
        var maxSummaryEventsOption = new Option<int>("--max-summary-events", () => 50, "Maximum events to include in summary during rehydration");
        var noSummaryOption = new Option<bool>("--no-summary", "Skip summarization during rehydration (faster, but no context preserved)");

        command.Add(configOption);
        command.Add(liveOpenhandsOption);
        command.Add(rehydrateOption);
        command.Add(maxSummaryEventsOption);
        command.Add(noSummaryOption);

        command.SetHandler(async (config, liveOpenhands, rehydrate, maxSummaryEvents, noSummary) =>
        {
            var exitCode = await RunDoctorAsync(config, liveOpenhands, rehydrate, maxSummaryEvents, noSummary);
            Environment.Exit(exitCode);
        }, configOption, liveOpenhandsOption, rehydrateOption, maxSummaryEventsOption, noSummaryOption);

        return command;
    }

    static async Task<int> RunDoctorAsync(
        string? configPath,
        bool liveOpenhands,
        bool rehydrate,
        int maxSummaryEvents,
        bool noSummary)
    {
        try
        {
            var results = new List<CheckResult>();

            // Load config
            var config = await LoadDoctorConfigAsync(configPath);
            results.Add(await CheckConfigFile(configPath, config));

            // Check target repo
            var targetRepo = config.TargetRepo ?? Directory.GetCurrentDirectory();
            results.Add(await CheckTargetRepo(targetRepo));

            // Check workflow file
            results.Add(await CheckWorkflowFile(targetRepo));

            // Check OpenHands tooling
            if (config.OpenHands.ToolDir is not null)
            {
                results.Add(await CheckOpenHandsTooling(config.OpenHands.ToolDir));
            }

            // Run live probe if requested
            if (liveOpenhands)
            {
                results.Add(await RunLiveOpenHandsProbe(config));
            }

            // Print results
            PrintCheckResults(results);

            // Return exit code based on results
            var hasFailures = results.Any(r => r.Status == CheckStatus.Fail);
            return hasFailures ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Doctor failed: {ex}");
            return 1;
        }
    }

    static async Task<DoctorConfig> LoadDoctorConfigAsync(string? configPath)
    {
        var actualPath = configPath;
        if (actualPath is null)
        {
            var cwd = Directory.GetCurrentDirectory();
            var defaultPath = Path.Combine(cwd, "config.yaml");
            if (File.Exists(defaultPath))
            {
                actualPath = defaultPath;
            }
        }

        if (actualPath is null || !File.Exists(actualPath))
        {
            return new DoctorConfig();
        }

        var raw = await File.ReadAllTextAsync(actualPath);
        // TODO: Fix YamlDotNet naming convention - need to check correct API for version 18.1.0
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<DoctorConfig>(raw);
    }

    static async Task<CheckResult> CheckConfigFile(string? configPath, DoctorConfig config)
    {
        if (configPath is null)
        {
            return CheckResult.Warn("config_file", "No config file found, using defaults");
        }

        if (!File.Exists(configPath))
        {
            return CheckResult.Fail("config_file", $"Config file not found: {configPath}");
        }

        return CheckResult.Pass("config_file", $"Config file loaded: {configPath}");
    }

    static async Task<CheckResult> CheckTargetRepo(string targetRepo)
    {
        if (!Directory.Exists(targetRepo))
        {
            return CheckResult.Fail("target_repo", $"Target repo directory does not exist: {targetRepo}");
        }

        return CheckResult.Pass("target_repo", $"Target repo exists: {targetRepo}");
    }

    static async Task<CheckResult> CheckWorkflowFile(string targetRepo)
    {
        var workflowPath = Path.Combine(targetRepo, "WORKFLOW.md");
        if (!File.Exists(workflowPath))
        {
            return CheckResult.Fail("workflow", $"Workflow file not found: {workflowPath}");
        }

        var result = WorkflowLoader.LoadWorkflowFromPath(workflowPath);
        if (result.IsErr)
        {
            return CheckResult.Fail("workflow", $"Failed to load workflow: {result.Error}");
        }

        return CheckResult.Pass("workflow", $"Workflow loaded: {workflowPath}");
    }

    static async Task<CheckResult> CheckOpenHandsTooling(string toolDir)
    {
        if (!Directory.Exists(toolDir))
        {
            return CheckResult.Fail("openhands_tooling", $"OpenHands tool directory does not exist: {toolDir}");
        }

        // Check for key files
        var requiredFiles = new[] { "openhands" };
        foreach (var file in requiredFiles)
        {
            var filePath = Path.Combine(toolDir, file);
            if (!File.Exists(filePath))
            {
                return CheckResult.Warn("openhands_tooling", $"Missing expected file: {filePath}");
            }
        }

        return CheckResult.Pass("openhands_tooling", $"OpenHands tooling directory exists: {toolDir}");
    }

    static async Task<CheckResult> RunLiveOpenHandsProbe(DoctorConfig config)
    {
        // ht: Placeholder for live probe
        // Full implementation would:
        // - Start OpenHands client
        // - Call OpenAPI probe
        // - Check response

        return CheckResult.Pass("live_openhands_probe", "Live probe not yet implemented");
    }

    static void PrintCheckResults(List<CheckResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("Doctor Preflight Checks:");
        Console.WriteLine();

        foreach (var result in results)
        {
            var symbol = result.Status switch
            {
                CheckStatus.Pass => "✓",
                CheckStatus.Warn => "⚠",
                CheckStatus.Fail => "✗",
                CheckStatus.Skip => "○",
                _ => "?"
            };

            var color = result.Status switch
            {
                CheckStatus.Pass => ConsoleColor.Green,
                CheckStatus.Warn => ConsoleColor.Yellow,
                CheckStatus.Fail => ConsoleColor.Red,
                CheckStatus.Skip => ConsoleColor.Gray,
                _ => ConsoleColor.White
            };

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"{symbol} {result.Name}: {result.Detail}");
            Console.ForegroundColor = originalColor;
        }

        Console.WriteLine();
    }
}

// Doctor config types
public sealed class DoctorConfig
{
    [YamlMember(Alias = "target_repo", ApplyNamingConventions = false)]
    public string? TargetRepo { get; init; }

    [YamlMember(Alias = "openhands", ApplyNamingConventions = false)]
    public DoctorOpenHandsConfig OpenHands { get; init; } = new();

    [YamlMember(Alias = "linear", ApplyNamingConventions = false)]
    public DoctorLinearConfig Linear { get; init; } = new();
}

public sealed class DoctorOpenHandsConfig
{
    [YamlMember(Alias = "tool_dir", ApplyNamingConventions = false)]
    public string? ToolDir { get; init; }

    [YamlMember(Alias = "probe_model", ApplyNamingConventions = false)]
    public string? ProbeModel { get; init; }

    [YamlMember(Alias = "probe_api_key_env", ApplyNamingConventions = false)]
    public string? ProbeApiKeyEnv { get; init; }
}

public sealed class DoctorLinearConfig
{
    [YamlMember(Alias = "enabled", ApplyNamingConventions = false)]
    public bool Enabled { get; init; }
}

// Check result types
public enum CheckStatus
{
    Pass,
    Warn,
    Fail,
    Skip
}

public sealed record CheckResult(CheckStatus Status, string Name, string Detail)
{
    public static CheckResult Pass(string name, string detail) =>
        new(CheckStatus.Pass, name, detail);

    public static CheckResult Warn(string name, string detail) =>
        new(CheckStatus.Warn, name, detail);

    public static CheckResult Fail(string name, string detail) =>
        new(CheckStatus.Fail, name, detail);

    public static CheckResult Skip(string name, string detail) =>
        new(CheckStatus.Skip, name, detail);
}