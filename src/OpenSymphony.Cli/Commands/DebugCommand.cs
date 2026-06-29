using System.CommandLine;
using System.Text;
using System.Text.Json;
using OpenSymphony.Domain;
using OpenSymphony.OpenHands;
using OpenSymphony.Workflow;
using OpenSymphony.Workspace;

namespace OpenSymphony.Cli;

/// <summary>
/// Resume an issue conversation for interactive debugging.
/// </summary>
public static class DebugCommand
{
    public static Command Create()
    {
        var issueIdArg = new Argument<string>("issue-id", "Linear issue identifier or persisted issue ID to resume");
        var configOption = new Option<string?>("--config", "Runtime config YAML path; defaults to ./config.yaml when present");
        var appOption = new Option<bool>("--app", "Print the Codex app deep link for a Codex-backed issue");

        var command = new Command("debug", "Resume an issue conversation for interactive debugging")
        {
            issueIdArg,
            configOption,
            appOption
        };

        command.SetHandler(async (context) =>
        {
            var issueId = context.ParseResult.GetValueForArgument(issueIdArg);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var appFlag = context.ParseResult.GetValueForOption(appOption);

            var session = new DebugSession(issueId, configPath, appFlag);
            var result = await session.RunAsync();

            if (result.IsErr)
            {
                Console.Error.WriteLine($"Error: {result.Error.Message}");
                context.ExitCode = 1;
            }
        });

        return command;
    }
}

/// <summary>
/// Debug session: loads config, finds workspace, attaches to conversation.
/// ht: minimal port of debug_session.rs core paths.
/// </summary>
public sealed class DebugSession
{
    private readonly string _issueId;
    private readonly string? _configPath;
    private readonly bool _appOnly;

    private const string DefaultConfigFile = "config.yaml";
    private const string CodexAppServerKind = "codex_app_server";

    public DebugSession(string issueId, string? configPath, bool appOnly)
    {
        _issueId = issueId;
        _configPath = configPath;
        _appOnly = appOnly;
    }

    public async Task<Result<Unit, DebugCommandError>> RunAsync()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var config = await LoadConfigAsync(currentDir);
        var workflowPath = FindWorkflowPath(currentDir);
        var workflowResult = WorkflowLoader.LoadWorkflowFromPath(workflowPath);
        if (workflowResult.IsErr)
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.WorkflowLoadError(workflowPath, workflowResult.Error.Message));

        var resolvedResult = WorkflowResolver.ResolveWorkflow(workflowResult.Value, currentDir, new ProcessEnvironment());
        if (resolvedResult.IsErr)
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.WorkflowResolveError(workflowPath, resolvedResult.Error.Message));

        var workflow = resolvedResult.Value;
        var workspaceRoot = workflow.Config.Workspace.Root;
        var manager = new WorkspaceManager(new WorkspaceManagerConfig(
            workspaceRoot,
            HookConfig.Default(),
            CleanupConfig.Default()
        ));

        var workspacePathResult = manager.WorkspacePathFor(_issueId);
        if (workspacePathResult.IsErr)
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.WorkspaceNotFound(_issueId, workspaceRoot));

        var workspacePath = workspacePathResult.Value;
        if (!Directory.Exists(workspacePath))
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.WorkspaceNotFound(_issueId, workspaceRoot));

        var manifestPath = Path.Combine(workspacePath, ".opensymphony", "conversation.json");
        if (!File.Exists(manifestPath))
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.ConversationManifestMissing(manifestPath));

        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<ConversationManifest>(manifestJson, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (manifest == null)
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.InvalidManifest(manifestPath));

        // Check if Codex or OpenHands
        var transportTarget = manifest.TransportTarget ?? "loopback";
        if (transportTarget == CodexAppServerKind || manifest.RuntimeContractVersion == "codex_app_server_v1")
        {
            return await HandleCodexDebugAsync(manifest, workspacePath);
        }
        else
        {
            if (_appOnly)
                return Result<Unit, DebugCommandError>.Err(new DebugCommandError.NotCodexRun(_issueId, transportTarget));
            return await HandleOpenHandsDebugAsync(workflow, manifest, workspacePath);
        }
    }

    private async Task<Result<Unit, DebugCommandError>> HandleCodexDebugAsync(ConversationManifest manifest, string workspacePath)
    {
        var threadId = manifest.CodexThreadId ?? manifest.ConversationId;
        if (string.IsNullOrEmpty(threadId))
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.CodexThreadIdMissing(_issueId, Path.Combine(workspacePath, ".opensymphony", "conversation.json")));

        if (_appOnly)
        {
            Console.WriteLine($"codex://threads/{threadId}");
            return Result<Unit, DebugCommandError>.Ok(Unit.Value);
        }

        var codexBin = Environment.GetEnvironmentVariable("OPENSYMPHONY_CODEX_BIN") ?? "codex";
        if (!await ValidateCodexResumeSupportAsync(codexBin))
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.CodexResumeUnsupported(codexBin, "resume command not supported"));

        var result = await LaunchCodexResumeAsync(codexBin, threadId, workspacePath);
        return result.IsOk
            ? Result<Unit, DebugCommandError>.Ok(Unit.Value)
            : Result<Unit, DebugCommandError>.Err(result.Error);
    }

    private async Task<Result<Unit, DebugCommandError>> HandleOpenHandsDebugAsync(ResolvedWorkflow workflow, ConversationManifest manifest, string workspacePath)
    {
        // ht: minimal OpenHands debug - for now, print message about conversation
        Console.WriteLine($"OpenHands debug for conversation {manifest.ConversationId}");
        Console.WriteLine($"Workspace: {workspacePath}");
        Console.WriteLine($"Server: {manifest.ServerBaseUrl}");
        Console.WriteLine();
        Console.WriteLine("Interactive debug not yet implemented for OpenHands.");
        return Result<Unit, DebugCommandError>.Ok(Unit.Value);
    }

    private async Task<DebugConfig?> LoadConfigAsync(string currentDir)
    {
        var configPath = _configPath is not null
            ? Path.IsPathRooted(_configPath) ? _configPath : Path.Combine(currentDir, _configPath)
            : Path.Combine(currentDir, DefaultConfigFile);

        if (!File.Exists(configPath))
            return null;

        var yaml = await File.ReadAllTextAsync(configPath);
        // ht: minimal YAML parsing - just check for tool_dir
        // TODO: proper YAML deserialization if needed
        return new DebugConfig { ToolDir = null };
    }

    private static string FindWorkflowPath(string currentDir)
    {
        var workflowPath = Path.Combine(currentDir, "WORKFLOW.md");
        if (File.Exists(workflowPath))
            return workflowPath;

        // Check parent directories like Rust's find_cargo_workspace_root
        var dir = new DirectoryInfo(currentDir);
        while (dir.Parent is not null)
        {
            var parentWorkflow = Path.Combine(dir.Parent.FullName, "WORKFLOW.md");
            if (File.Exists(parentWorkflow))
                return parentWorkflow;
            dir = dir.Parent;
        }

        return workflowPath; // Return default even if missing
    }

    private static async Task<bool> ValidateCodexResumeSupportAsync(string program)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = program,
                Arguments = "resume --help",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (proc == null) return false;

            var output = await proc.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            var help = output + error;
            return proc.ExitCode == 0 && help.Contains("resume") && help.Contains("SESSION_ID");
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Result<Unit, DebugCommandError>> LaunchCodexResumeAsync(string program, string threadId, string workspacePath)
    {
        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = program,
                Arguments = $"resume {threadId}",
                WorkingDirectory = workspacePath,
                UseShellExecute = false
            });

            if (proc == null)
                return Result<Unit, DebugCommandError>.Err(new DebugCommandError.CodexLaunchFailed(program, new Exception("process start failed")));

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0
                ? Result<Unit, DebugCommandError>.Ok(Unit.Value)
                : Result<Unit, DebugCommandError>.Err(new DebugCommandError.CodexResumeFailed(proc.ExitCode));
        }
        catch (Exception ex)
        {
            return Result<Unit, DebugCommandError>.Err(new DebugCommandError.CodexLaunchFailed(program, ex));
        }
    }

    private record DebugConfig
    {
        public string? ToolDir { get; init; }
    }
}

/// <summary>
/// Conversation manifest from workspace.
/// ht: minimal subset of fields needed for debug.
/// </summary>
internal sealed class ConversationManifest
{
    public string IssueId { get; init; } = "";
    public string Identifier { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string? CodexThreadId { get; init; }
    public string ServerBaseUrl { get; init; } = "";
    public string? TransportTarget { get; init; }
    public string? RuntimeContractVersion { get; init; }
    public string PersistenceDir { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset LastAttachedAt { get; init; }
    public bool FreshConversation { get; init; }
    public bool WorkflowPromptSeeded { get; init; }
}

/// <summary>
/// Debug command errors.
/// ht: minimal port of debug_session::DebugCommandError.
/// </summary>
public abstract class DebugCommandError : Exception
{
    protected DebugCommandError(string message) : base(message) { }

    public sealed class WorkflowLoadError : DebugCommandError
    {
        public string Path { get; }
        public string Detail { get; }

        public WorkflowLoadError(string path, string detail)
            : base($"Failed to load workflow {path}: {detail}")
        {
            Path = path;
            Detail = detail;
        }
    }

    public sealed class WorkflowResolveError : DebugCommandError
    {
        public string Path { get; }
        public string Detail { get; }

        public WorkflowResolveError(string path, string detail)
            : base($"Failed to resolve workflow {path}: {detail}")
        {
            Path = path;
            Detail = detail;
        }
    }

    public sealed class WorkspaceNotFound : DebugCommandError
    {
        public WorkspaceNotFound(string issueReference, string workspaceRoot)
            : base($"No managed workspace for issue reference `{issueReference}` exists under {workspaceRoot}")
        {
        }
    }

    public sealed class ConversationManifestMissing : DebugCommandError
    {
        public string Path { get; }

        public ConversationManifestMissing(string path)
            : base($"Conversation manifest is missing: {path}")
        {
            Path = path;
        }
    }

    public sealed class InvalidManifest : DebugCommandError
    {
        public InvalidManifest(string path)
            : base($"Failed to decode conversation manifest {path}")
        {
        }
    }

    public sealed class NotCodexRun : DebugCommandError
    {
        public NotCodexRun(string issueReference, string transportTarget)
            : base($"Issue `{issueReference}` was not run by the Codex app-server harness; recorded transport target is `{transportTarget}`")
        {
        }
    }

    public sealed class CodexThreadIdMissing : DebugCommandError
    {
        public CodexThreadIdMissing(string issueReference, string path)
            : base($"Codex-backed issue `{issueReference}` has no recorded Codex thread id in {path}")
        {
        }
    }

    public sealed class CodexResumeUnsupported : DebugCommandError
    {
        public CodexResumeUnsupported(string program, string detail)
            : base($"Installed Codex CLI `{program}` does not expose the required `codex resume <session-id>` path: {detail}")
        {
        }
    }

    public sealed class CodexLaunchFailed : DebugCommandError
    {
        public CodexLaunchFailed(string program, Exception inner)
            : base($"Failed to launch Codex CLI `{program}`: {inner.Message}")
        {
        }
    }

    public sealed class CodexResumeFailed : DebugCommandError
    {
        public CodexResumeFailed(int exitCode)
            : base($"Codex resume command exited with status {exitCode}")
        {
        }
    }
}