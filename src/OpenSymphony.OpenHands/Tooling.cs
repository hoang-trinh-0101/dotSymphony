using System.Diagnostics;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands tooling.rs.

internal static class ToolingConstants
{
    internal const string PLACEHOLDER_VERSION = "0+bootstrap.placeholder";
    internal const string PLACEHOLDER_REQUIREMENT = "openhands-agent-server-placeholder==0+bootstrap.placeholder";
    internal const string PLACEHOLDER_LOCK_SNIPPET = "Placeholder bootstrap file.";
    internal static readonly string[] REQUIRED_PIN_PACKAGES =
        ["openhands-agent-server", "openhands-sdk", "openhands-tools", "openhands-workspace"];
}

public sealed record LocalToolingLayout(
    string ToolDir,
    string RunLocalScript,
    string Pyproject,
    string Lockfile,
    string VersionFile)
{
    public static LocalToolingLayout FromToolDir(string toolDir) => new(
        toolDir,
        Path.Combine(toolDir, "run-local.sh"),
        Path.Combine(toolDir, "pyproject.toml"),
        Path.Combine(toolDir, "uv.lock"),
        Path.Combine(toolDir, "version.txt"));
}

public sealed record PinStatus(
    bool VersionPinned,
    bool DependencyPinned,
    bool DependencyMatchesVersion,
    bool LockfileResolved,
    bool LockfileMatchesVersion)
{
    public bool IsReady() =>
        VersionPinned && DependencyPinned && DependencyMatchesVersion && LockfileResolved && LockfileMatchesVersion;

    public List<string> BlockingIssues()
    {
        var issues = new List<string>();
        if (!VersionPinned) issues.Add("version.txt still contains the bootstrap placeholder");
        if (!DependencyPinned) issues.Add("pyproject.toml is missing one or more required OpenHands package pins");
        if (VersionPinned && !DependencyMatchesVersion) issues.Add("pyproject.toml OpenHands package pins do not match version.txt");
        if (!LockfileResolved) issues.Add("uv.lock does not contain a verifiable resolved OpenHands package set");
        if (VersionPinned && !LockfileMatchesVersion) issues.Add("uv.lock OpenHands package versions do not match version.txt");
        return issues;
    }
}

public sealed record ToolingMetadata(
    string Module,
    string RuntimeEnv,
    string Runtime,
    string Host,
    ushort DefaultPort,
    string PortEnv,
    string Launcher);

public sealed record LocalServerTooling(
    LocalToolingLayout Layout,
    ToolingMetadata Metadata,
    string Version,
    PinStatus PinStatus)
{
    public string BaseUrl(ushort? portOverride) =>
        $"http://{Metadata.Host}:{portOverride ?? Metadata.DefaultPort}";
}

public sealed record ResolvedLaunch(
    string Program,
    List<string> Args,
    Dictionary<string, string> Env,
    string WorkingDir,
    string BaseUrl,
    string Version,
    string LauncherSummary);

public sealed class LocalToolingError : Exception
{
    public LocalToolingError(string message) : base(message) { }
}
