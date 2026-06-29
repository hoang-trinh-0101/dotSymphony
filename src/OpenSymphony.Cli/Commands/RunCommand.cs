using System.CommandLine;
using OpenSymphony.Cli.OrchestratorRun;

namespace OpenSymphony.Cli;

/// <summary>
/// Run the real orchestrator against the current project workflow.
/// ht: Port of older/crates/opensymphony-cli/src/lib.rs Run command handler.
/// </summary>
public static class RunCommand
{
    public static Command Create()
    {
        var command = new Command("run", "Run the real orchestrator against the current project workflow");

        var configOption = new Option<string?>("--config", "Runtime config YAML path; defaults to ./config.yaml when present");
        var dryRunOption = new Option<bool>("--dry-run", "Preview selected harness/model routing without launching model-backed workers");

        command.Add(configOption);
        command.Add(dryRunOption);

        command.SetHandler(async (config, dryRun) =>
        {
            var exitCode = await RunOrchestrator.RunCommandAsync(config, dryRun);
            Environment.Exit(exitCode);
        }, configOption, dryRunOption);

        return command;
    }
}