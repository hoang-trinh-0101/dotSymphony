using System.CommandLine;

namespace OpenSymphony.Cli;

/// <summary>
/// Linear operations guarded by OpenSymphony state.
/// </summary>
public static class LinearCommand
{
    public static Command Create()
    {
        var command = new Command("linear", "Linear operations guarded by OpenSymphony state");

        // TODO: Add Linear subcommands

        return command;
    }
}