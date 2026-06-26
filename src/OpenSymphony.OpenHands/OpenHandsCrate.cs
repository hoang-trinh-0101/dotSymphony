namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands lib.rs — re-exports via namespace.
//   C# doesn't need explicit re-exports; all public types are accessible from the namespace.

public static class OpenHandsCrate
{
    public const string CrateName = "opensymphony-openhands";

    public static string CrateSummary() =>
        "REST client, WebSocket event stream, event cache/state mirror, OpenHands → OpenSymphony event normalization, runtime state mirror with progress-based idle detection, local server supervisor, repo-local tooling resolution, conservative readiness probes, doctor diagnostics, issue session runner, and protocol error mapping";

    public static string PlaceholderSummary() => CrateSummary();
}
