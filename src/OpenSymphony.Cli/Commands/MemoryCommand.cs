using System.CommandLine;
using OpenSymphony.Cli.Memory;

namespace OpenSymphony.Cli;

/// <summary>
/// Capture, query, and sync project memory.
/// </summary>
public static class MemoryCommand
{
    public static Command Create()
    {
        var command = new Command("memory", "Capture, query, and sync project memory");

        var configOption = new Option<string?>("--config", "Memory configuration YAML path");

        command.AddGlobalOption(configOption);

        // Add subcommands
        command.Add(MemoryInit.Create());
        command.Add(MemoryQuery.Create());
        command.Add(MemoryExport.Create());
        command.Add(MemoryImport.Create());

        return command;
    }
}