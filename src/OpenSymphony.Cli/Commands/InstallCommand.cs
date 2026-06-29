using System.CommandLine;

namespace OpenSymphony.Cli;

/// <summary>
/// Install app-managed runtimes and integrations.
/// </summary>
public static class InstallCommand
{
    private const string DefaultManagedOpenhandsToolDir = "~/.opensymphony/openhands-server";
    private const string OpenhandsVersion = "0.0.1-placeholder"; // ht: TODO: read from embedded resource

    public static Command Create()
    {
        var command = new Command("install", "Install app-managed runtimes and integrations");

        var openhandsCommand = new Command("openhands", "Install the pinned app-managed OpenHands agent-server runtime");
        var dirOption = new Option<string?>("--dir", "Tool installation directory")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        openhandsCommand.Add(dirOption);
        openhandsCommand.SetHandler(async (context) =>
        {
            var dir = context.ParseResult.GetValueForOption(dirOption);
            var cancellationToken = context.GetCancellationToken();
            var exitCode = await RunOpenhandsInstallAsync(dir, cancellationToken);
            context.ExitCode = exitCode;
        });
        command.Add(openhandsCommand);

        return command;
    }

    private static async Task<int> RunOpenhandsInstallAsync(string? dir, CancellationToken cancellationToken)
    {
        try
        {
            var toolDir = string.IsNullOrEmpty(dir) ? GetDefaultManagedOpenhandsToolDir() : ExpandPath(dir);
            var report = await EnsureOpenhandsToolingAsync(toolDir, cancellationToken);
            Console.WriteLine(report.summary());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"opensymphony install openhands failed: {ex.Message}");
            return 1;
        }
    }

    private static string GetDefaultManagedOpenhandsToolDir()
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrEmpty(home))
            throw new InvalidOperationException("HOME or USERPROFILE must be set");
        return Path.Combine(home, ".opensymphony", "openhands-server");
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(home))
                throw new InvalidOperationException("HOME or USERPROFILE must be set");
            return Path.Combine(home, path[2..]);
        }
        return Path.GetFullPath(path);
    }

    private static async Task<ToolingInstallReport> EnsureOpenhandsToolingAsync(string toolDir, CancellationToken cancellationToken)
    {
        var action = DetermineInstallAction(toolDir);
        if (action == ToolingInstallAction.Ready)
        {
            return new ToolingInstallReport(action, toolDir, OpenhandsVersion);
        }

        await MaterializeEmbeddedToolingAsync(toolDir, cancellationToken);
        await PrepareOpenhandsToolingAsync(toolDir, cancellationToken);
        return new ToolingInstallReport(action, toolDir, OpenhandsVersion);
    }

    private static ToolingInstallAction DetermineInstallAction(string toolDir)
    {
        // ht: Simplified version - just check if directory exists with version file
        var versionFile = Path.Combine(toolDir, "version.txt");
        if (!Directory.Exists(toolDir))
            return ToolingInstallAction.Installed;
        
        if (!File.Exists(versionFile))
            return ToolingInstallAction.Repaired;

        var existingVersion = File.ReadAllText(versionFile).Trim();
        if (existingVersion == OpenhandsVersion)
            return ToolingInstallAction.Ready;
        
        return ToolingInstallAction.Updated;
    }

    private static async Task MaterializeEmbeddedToolingAsync(string toolDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(toolDir);

        // ht: Create placeholder files - in Rust these are embedded as bytes
        var placeholderFiles = new Dictionary<string, string>
        {
            [".python-version"] = "3.11",
            ["README.md"] = "# OpenHands Tooling\n\nThis directory contains pinned OpenHands tooling.",
            ["install.sh"] = "#!/bin/bash\n# Placeholder installer script\n",
            ["pyproject.toml"] = "[project]\nname = \"openhands-tooling\"\nversion = \"0.1.0\"\n",
            ["run-local.sh"] = "#!/bin/bash\n# Placeholder run script\n",
            ["version.txt"] = OpenhandsVersion
        };

        foreach (var (relativePath, contents) in placeholderFiles)
        {
            var fullPath = Path.Combine(toolDir, relativePath);
            var parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                Directory.CreateDirectory(parentDir);
            
            await File.WriteAllTextAsync(fullPath, contents, cancellationToken);
            
            // ht: Set executable bit on .sh files for Unix
            if (relativePath.EndsWith(".sh") && Environment.OSVersion.Platform == PlatformID.Unix)
            {
                // TODO: Set executable permissions using chmod
            }
        }
    }

    private static async Task PrepareOpenhandsToolingAsync(string toolDir, CancellationToken cancellationToken)
    {
        // ht: Placeholder for running the actual installer
        // In Rust this runs the install.sh script
        await Task.CompletedTask;
    }

    private enum ToolingInstallAction
    {
        Ready,
        Installed,
        Updated,
        Repaired
    }

    private record ToolingInstallReport(ToolingInstallAction Action, string ToolDir, string Version)
    {
        public string summary() => Action switch
        {
            ToolingInstallAction.Ready => $"pinned OpenHands tooling {Version} is already available at {ToolDir}",
            ToolingInstallAction.Installed => $"installed pinned OpenHands tooling {Version} at {ToolDir}",
            ToolingInstallAction.Updated => $"updated pinned OpenHands tooling {Version} at {ToolDir}",
            ToolingInstallAction.Repaired => $"repaired pinned OpenHands tooling {Version} at {ToolDir}",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}