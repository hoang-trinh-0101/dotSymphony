using System.Net;
using System.Text.Json;
using OpenSymphony.OpenHands;
using OpenSymphony.Workflow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Cli.OrchestratorRun;

// ht: Port of older/crates/opensymphony-cli/src/orchestrator_run/config.rs
//   Runtime config loading for the `opensymphony run` command.
//   serde_yaml → YamlDotNet. tokio::fs → System.IO async where needed.

public static class RunConfigConstants
{
    public const string DEFAULT_CONFIG_FILE = "config.yaml";
    public const string DEFAULT_CONTROL_PLANE_BIND = "127.0.0.1:2468";
    public const string DEFAULT_MEMORY_SERVER_BIND = "127.0.0.1:0";
    public const string DEFAULT_MEMORY_TOKEN_ENV = "OPENSYMPHONY_MEMORY_TOKEN";
}

public sealed class RunConfigError : Exception
{
    public RunConfigError(string message) : base(message) { }
    public RunConfigError(string message, Exception inner) : base(message, inner) { }
}

// YAML config file structure
public sealed class RunConfigFile
{
    [YamlMember(Alias = "target_repo", ApplyNamingConventions = false)]
    public string? TargetRepo { get; init; }

    [YamlMember(Alias = "control_plane", ApplyNamingConventions = false)]
    public ControlPlaneConfigFile ControlPlane { get; init; } = new();

    [YamlMember(Alias = "openhands", ApplyNamingConventions = false)]
    public RunOpenHandsConfigFile OpenHands { get; init; } = new();

    [YamlMember(Alias = "memory", ApplyNamingConventions = false)]
    public RunMemoryConfigFile Memory { get; init; } = new();
}

public sealed class ControlPlaneConfigFile
{
    [YamlMember(Alias = "bind", ApplyNamingConventions = false)]
    public string? Bind { get; init; }
}

public sealed class RunOpenHandsConfigFile
{
    [YamlMember(Alias = "tool_dir", ApplyNamingConventions = false)]
    public string? ToolDir { get; init; }
}

public sealed class RunMemoryConfigFile
{
    [YamlMember(Alias = "auto_capture", ApplyNamingConventions = false)]
    public bool? AutoCapture { get; init; }

    [YamlMember(Alias = "auto_archive", ApplyNamingConventions = false)]
    public bool? AutoArchive { get; init; }

    [YamlMember(Alias = "serve", ApplyNamingConventions = false)]
    public bool? Serve { get; init; }

    [YamlMember(Alias = "bind", ApplyNamingConventions = false)]
    public string? Bind { get; init; }

    [YamlMember(Alias = "token_env", ApplyNamingConventions = false)]
    public string? TokenEnv { get; init; }
}

public sealed class RunMemoryConfig
{
    public bool AutoCapture { get; init; }
    public bool AutoArchive { get; init; }
    public RunMemoryServerConfig? Server { get; init; }
}

public sealed class RunMemoryServerConfig
{
    public IPEndPoint Bind { get; init; }
    public string? Token { get; init; }
}

public sealed class RunRuntimeConfig
{
    public string? ConfigPath { get; init; }
    public string TargetRepo { get; init; }
    public string WorkflowPath { get; init; }
    public ResolvedWorkflow Workflow { get; init; }
    public IPEndPoint Bind { get; init; }
    public string? ToolDir { get; init; }
    public OpenHandsConversationStorePaths? OpenHandsConversationStore { get; init; }
    public RunMemoryConfig Memory { get; init; }
}

public static class RunConfigResolver
{
    public static async Task<RunRuntimeConfig> ResolveRuntimeConfig(
        string? configPath,
        bool dryRun,
        CancellationToken ct = default)
    {
        var cwd = Directory.GetCurrentDirectory();
        var actualConfigPath = configPath is not null
            ? ResolveRelativeTo(cwd, configPath)
            : null;

        string? configRoot = null;
        RunConfigFile config;

        if (actualConfigPath is not null && File.Exists(actualConfigPath))
        {
            var raw = await File.ReadAllTextAsync(actualConfigPath, ct);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            try
            {
                config = deserializer.Deserialize<RunConfigFile>(raw);
            }
            catch (Exception ex)
            {
                throw new RunConfigError($"Failed to parse config file {actualConfigPath}", ex);
            }

            configRoot = Path.GetDirectoryName(Path.GetFullPath(actualConfigPath));
            config = await ResolveRunConfigAsync(actualConfigPath, config, ct);
        }
        else
        {
            config = new RunConfigFile();
            configRoot = cwd;
        }

        configRoot ??= cwd;

        var targetRepo = config.TargetRepo is not null
            ? ResolvePath(configRoot, config.TargetRepo)
            : cwd;

        var workflowPath = Path.Combine(targetRepo, "WORKFLOW.md");
        var workflowResult = WorkflowLoader.LoadWorkflowFromPath(workflowPath);
        if (workflowResult.IsErr)
        {
            throw new RunConfigError($"Failed to load workflow {workflowPath}: {workflowResult.Error}");
        }

        var workflow = workflowResult.Value;
        var resolvedResult = WorkflowResolver.ResolveWorkflow(
            workflow,
            targetRepo,
            new ProcessEnvironment());
        if (resolvedResult.IsErr)
        {
            throw new RunConfigError($"Failed to resolve workflow {workflowPath}: {resolvedResult.Error}");
        }

        var resolvedWorkflow = resolvedResult.Value with
        {
            Config = resolvedResult.Value.Config with
            {
                Routing = resolvedResult.Value.Config.Routing with
                {
                    DryRun = dryRun
                }
            }
        };

        var bindValue = config.ControlPlane.Bind ?? RunConfigConstants.DEFAULT_CONTROL_PLANE_BIND;
        if (!IPEndPoint.TryParse(bindValue, out var bind))
        {
            throw new RunConfigError($"Invalid control-plane bind address `{bindValue}`");
        }

        var toolDir = config.OpenHands.ToolDir is not null
            ? ResolvePath(configRoot, config.OpenHands.ToolDir)
            : null;

        OpenHandsConversationStorePaths? openhandsConversationStore = null;
        if (toolDir is not null)
        {
            try
            {
                openhandsConversationStore = OpenHandsConversationStorePaths.ForToolDir(toolDir, targetRepo);
            }
            catch (Exception ex)
            {
                throw new RunConfigError($"Failed to create OpenHands conversation store paths: {ex.Message}", ex);
            }
        }

        // Memory config
        var memoryConfigPath = Path.Combine(targetRepo, ".opensymphony", "memory", "config.yaml");
        var memoryConfigExists = File.Exists(memoryConfigPath);
        var autoCapture = config.Memory.AutoCapture ?? true;
        var serveMemory = config.Memory.Serve ?? memoryConfigExists;

        RunMemoryServerConfig? memoryServer = null;
        if (serveMemory)
        {
            var memoryBindValue = config.Memory.Bind ?? RunConfigConstants.DEFAULT_MEMORY_SERVER_BIND;
            if (!IPEndPoint.TryParse(memoryBindValue, out var memoryBind))
            {
                throw new RunConfigError($"Invalid memory server bind address `{memoryBindValue}`");
            }

            var memoryTokenEnv = config.Memory.TokenEnv ?? RunConfigConstants.DEFAULT_MEMORY_TOKEN_ENV;
            var memoryToken = Environment.GetEnvironmentVariable(memoryTokenEnv);
            if (!string.IsNullOrWhiteSpace(memoryToken))
            {
                memoryServer = new RunMemoryServerConfig
                {
                    Bind = memoryBind,
                    Token = memoryToken
                };
            }
            else
            {
                memoryServer = new RunMemoryServerConfig
                {
                    Bind = memoryBind,
                    Token = null
                };
            }
        }

        var memoryConfig = new RunMemoryConfig
        {
            AutoCapture = autoCapture,
            AutoArchive = config.Memory.AutoArchive ?? false,
            Server = memoryServer
        };

        ValidateMemoryBootstrap(targetRepo, memoryConfig);

        return new RunRuntimeConfig
        {
            ConfigPath = actualConfigPath,
            TargetRepo = targetRepo,
            WorkflowPath = workflowPath,
            Workflow = resolvedWorkflow,
            Bind = bind,
            ToolDir = toolDir,
            OpenHandsConversationStore = openhandsConversationStore,
            Memory = memoryConfig
        };
    }

    private static void ValidateMemoryBootstrap(string targetRepo, RunMemoryConfig memory)
    {
        if (!memory.AutoCapture && memory.Server is null)
        {
            return;
        }

        var memoryConfigPath = Path.Combine(targetRepo, ".opensymphony", "memory", "config.yaml");
        if (!File.Exists(memoryConfigPath))
        {
            throw new RunConfigError(
                $"Memory auto-capture is enabled but {memoryConfigPath} is missing; " +
                $"run `opensymphony memory init` or `opensymphony update` from the target repo before `opensymphony run`");
        }
    }

    private static async Task<RunConfigFile> ResolveRunConfigAsync(
        string configPath,
        RunConfigFile config,
        CancellationToken ct)
    {
        return new RunConfigFile
        {
            TargetRepo = await ExpandEnvAsync(config.TargetRepo, ct),
            ControlPlane = new ControlPlaneConfigFile
            {
                Bind = await ExpandEnvAsync(config.ControlPlane.Bind, ct)
            },
            OpenHands = new RunOpenHandsConfigFile
            {
                ToolDir = await ExpandEnvAsync(config.OpenHands.ToolDir, ct)
            },
            Memory = new RunMemoryConfigFile
            {
                AutoCapture = config.Memory.AutoCapture,
                AutoArchive = config.Memory.AutoArchive,
                Serve = config.Memory.Serve,
                Bind = await ExpandEnvAsync(config.Memory.Bind, ct),
                TokenEnv = await ExpandEnvAsync(config.Memory.TokenEnv, ct)
            }
        };
    }

    private static async Task<string?> ExpandEnvAsync(string? value, CancellationToken ct)
        => value is null ? null : await ExpandEnvTokensAsync(value, ct);

    private static async Task<string> ExpandEnvTokensAsync(string value, CancellationToken ct)
    {
        // ht: Simple ${VAR} expansion. Matches Rust expand_env_tokens behavior.
        var result = new System.Text.StringBuilder();
        var i = 0;

        while (i < value.Length)
        {
            if (i + 1 < value.Length && value[i] == '$' && value[i + 1] == '{')
            {
                var end = value.IndexOf('}', i + 2);
                if (end == -1)
                {
                    throw new RunConfigError($"Unterminated environment token `${{{value.Substring(i + 2)}}}`");
                }

                var varName = value.Substring(i + 2, end - i - 2);
                var envValue = Environment.GetEnvironmentVariable(varName);
                if (envValue is null)
                {
                    throw new RunConfigError($"Missing environment variable `{varName}`");
                }

                result.Append(envValue);
                i = end + 1;
            }
            else
            {
                result.Append(value[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static string ResolveRelativeTo(string baseDir, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    private static string ResolvePath(string baseDir, string path) =>
        ResolveRelativeTo(baseDir, path);
}

// ht: ProcessEnvironment - implements IEnvironment for workflow resolution
internal class ProcessEnvironment : Workflow.IEnvironment
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}