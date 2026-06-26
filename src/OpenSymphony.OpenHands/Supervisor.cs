using System.Diagnostics;
using System.Net.Http.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands supervisor.rs — config types + process lifecycle.

public enum ServerMode
{
    Supervised,
    External,
}

public enum LaunchOwnership
{
    Launched,
    External,
}

public abstract record ServerState
{
    public sealed record Stopped : ServerState;
    public sealed record Ready : ServerState;
    public sealed record Unreachable : ServerState;
    public sealed record Exited(int? Code) : ServerState;
}

public sealed record ProbeConfig
{
    public string Path { get; init; } = "/openapi.json";
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
}

public sealed record SupervisedServerConfig
{
    public LocalServerTooling Tooling { get; init; } = null!;
    public List<string>? Command { get; init; }
    public ushort? PortOverride { get; init; }
    public Dictionary<string, string> ExtraEnv { get; init; } = [];
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public ProbeConfig Probe { get; init; } = new();
    public bool ForwardStderr { get; init; } = true;

    public static SupervisedServerConfig New(LocalServerTooling tooling) => new() { Tooling = tooling };

    public string BaseUrl() => Tooling.BaseUrl(PortOverride);
}

public sealed record ExternalServerConfig
{
    public string BaseUrl { get; init; }
    public ProbeConfig Probe { get; init; } = new();

    public static ExternalServerConfig New(string baseUrl) => new() { BaseUrl = baseUrl };
}

public abstract record SupervisorConfig
{
    public sealed record Supervised(SupervisedServerConfig Config) : SupervisorConfig;
    public sealed record External(ExternalServerConfig Config) : SupervisorConfig;

    public static SupervisorConfig SupervisedCfg(LocalServerTooling tooling) =>
        new Supervised(SupervisedServerConfig.New(tooling));
    public static SupervisorConfig ExternalCfg(string baseUrl) =>
        new External(ExternalServerConfig.New(baseUrl));
}

public sealed class SupervisorError : Exception
{
    public SupervisorError(string message) : base(message) { }
}

public sealed class LocalServerSupervisor
{
    private readonly SupervisorConfig _config;
    private Process? _process;
    private ServerState _state = new ServerState.Stopped();

    public LocalServerSupervisor(SupervisorConfig config) => _config = config;

    public ServerState State => _state;
    public LaunchOwnership Ownership => _config is SupervisorConfig.Supervised
        ? LaunchOwnership.Launched : LaunchOwnership.External;

    public async Task<Result<Unit, SupervisorError>> StartAsync(CancellationToken ct = default)
    {
        if (_config is not SupervisorConfig.Supervised supervised)
            return Result<Unit, SupervisorError>.Ok(Unit.Value); // External — nothing to launch

        var resolved = ResolveLaunch(supervised.Config);
        var psi = new ProcessStartInfo
        {
            FileName = resolved.Program,
            WorkingDirectory = resolved.WorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = supervised.Config.ForwardStderr,
        };
        foreach (var arg in resolved.Args) psi.ArgumentList.Add(arg);
        foreach (var (k, v) in resolved.Env) psi.Environment[k] = v;

        try
        {
            _process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _state = new ServerState.Unreachable();
            return Result<Unit, SupervisorError>.Err(new SupervisorError(ex.Message));
        }

        return Result<Unit, SupervisorError>.Ok(Unit.Value);
    }

    public async Task<Result<Unit, SupervisorError>> ProbeReadyAsync(CancellationToken ct = default)
    {
        var baseUrl = _config switch
        {
            SupervisorConfig.Supervised s => s.Config.BaseUrl(),
            SupervisorConfig.External e => e.Config.BaseUrl,
            _ => "",
        };
        var probe = _config switch
        {
            SupervisorConfig.Supervised s => s.Config.Probe,
            SupervisorConfig.External e => e.Config.Probe,
            _ => new ProbeConfig(),
        };

        using var http = new HttpClient();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await http.GetAsync($"{baseUrl}{probe.Path}", ct);
                if (resp.IsSuccessStatusCode)
                {
                    _state = new ServerState.Ready();
                    return Result<Unit, SupervisorError>.Ok(Unit.Value);
                }
            }
            catch { }
            await Task.Delay(probe.PollInterval, ct);
        }

        _state = new ServerState.Unreachable();
        return Result<Unit, SupervisorError>.Err(new SupervisorError("startup probe timed out"));
    }

    public ServerState CheckState()
    {
        if (_process is null) return _state;
        if (_process.HasExited)
        {
            _state = new ServerState.Exited(_process.ExitCode);
            return _state;
        }
        return _state;
    }

    public void Stop()
    {
        if (_process is { } p && !p.HasExited)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }
        _process = null;
        _state = new ServerState.Stopped();
    }

    private static ResolvedLaunch ResolveLaunch(SupervisedServerConfig config)
    {
        var tooling = config.Tooling;
        var program = config.Command is { } cmd && cmd.Count > 0 ? cmd[0] : tooling.Layout.RunLocalScript;
        var args = config.Command is { } cmd2 && cmd2.Count > 1
            ? cmd2.Skip(1).ToList()
            : new List<string>();
        var env = new Dictionary<string, string>(config.ExtraEnv);
        if (config.PortOverride is { } port)
            env[tooling.Metadata.PortEnv] = port.ToString();
        return new ResolvedLaunch(
            program, args, env, tooling.Layout.ToolDir,
            config.BaseUrl(), tooling.Version, tooling.Metadata.Launcher);
    }
}
