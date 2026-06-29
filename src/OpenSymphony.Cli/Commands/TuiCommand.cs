using System.CommandLine;

namespace OpenSymphony.Cli;

/// <summary>
/// Attach the FrankenTUI operator client to a control plane.
/// ht: Port of older/crates/opensymphony-cli/src/lib.rs Tui command handler.
/// TODO: TUI integration - this command requires the separate TUI project.
/// </summary>
public static class TuiCommand
{
    public static Command Create()
    {
        var command = new Command("tui", "Attach the FrankenTUI operator client to a control plane");

        var urlOption = new Option<string>("--url", () => "http://127.0.0.1:2468/", "Control-plane base URL");
        var exitAfterOption = new Option<int?>("--exit-after-ms", "Exit after the specified number of milliseconds; useful for smoke tests");

        command.Add(urlOption);
        command.Add(exitAfterOption);

        command.SetHandler(async (url, exitAfter) =>
        {
            var exitCode = await RunTuiAsync(url, exitAfter);
            Environment.Exit(exitCode);
        }, urlOption, exitAfterOption);

        return command;
    }

    static async Task<int> RunTuiAsync(string url, int? exitAfterMs)
    {
        try
        {
            Console.WriteLine($"TUI client not yet implemented");
            Console.WriteLine($"Target URL: {url}");
            if (exitAfterMs is not null)
            {
                Console.WriteLine($"Exit after: {exitAfterMs}ms");
            }
            Console.WriteLine();
            Console.WriteLine("The FrankenTUI operator client is a separate project.");
            Console.WriteLine("This command will be implemented once the TUI project is ported.");

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TUI failed: {ex}");
            return 1;
        }
    }
}