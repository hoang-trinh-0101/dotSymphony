using System.CommandLine;

namespace OpenSymphony.Cli;

/// <summary>
/// Main CLI entry point for OpenSymphony.
/// </summary>
public static class Cli
{
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Operate the OpenSymphony local MVP on a trusted machine")
        {
            Description = "Operate the OpenSymphony local MVP on a trusted machine.\n\n" +
                          "Use this CLI to run the orchestrator, local control-plane demos, " +
                          "preflight checks, and GraphQL-backed Linear workflows.\n\n" +
                          "Safety: local OpenSymphony runs agent activity on the host with " +
                          "process-level isolation only. It is not sandboxed."
        };

        // Add subcommands
        rootCommand.Add(InitRepoCommand.Create());
        rootCommand.Add(UpdateRepoCommand.Create());
        rootCommand.Add(InstallCommand.Create());
        rootCommand.Add(RunCommand.Create());
        rootCommand.Add(DebugCommand.Create());
        rootCommand.Add(MemoryCommand.Create());
        rootCommand.Add(LinearCommand.Create());
        rootCommand.Add(DoctorCommand.Create());
        rootCommand.Add(DaemonCommand.Create());
        rootCommand.Add(TuiCommand.Create());
        rootCommand.Add(RehydrateCommand.Create());

        return rootCommand;
    }

    public static int Run(string[] args)
    {
        var rootCommand = CreateRootCommand();
        return rootCommand.Invoke(args);
    }

    public static Task<int> RunAsync(string[] args)
    {
        return Task.FromResult(Run(args));
    }
}