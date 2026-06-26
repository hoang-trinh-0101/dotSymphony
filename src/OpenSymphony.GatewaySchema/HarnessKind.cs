namespace OpenSymphony.GatewaySchema;

// ht: minimal port — only HarnessKind needed by Workflow. Full GatewaySchema ported in Iteration 13.
public enum HarnessKind
{
    OpenHandsAgentServer,
    CodexAppServer,
    RustNative,
}

public static class HarnessKindExtensions
{
    public static readonly HarnessKind[] All =
    {
        HarnessKind.OpenHandsAgentServer,
        HarnessKind.CodexAppServer,
        HarnessKind.RustNative,
    };

    public static HarnessKind? Parse(string value) => value switch
    {
        "openhands_agent_server" => HarnessKind.OpenHandsAgentServer,
        "codex_app_server" => HarnessKind.CodexAppServer,
        "rust_native" => HarnessKind.RustNative,
        _ => null,
    };

    public static string AsStr(this HarnessKind kind) => kind switch
    {
        HarnessKind.OpenHandsAgentServer => "openhands_agent_server",
        HarnessKind.CodexAppServer => "codex_app_server",
        HarnessKind.RustNative => "rust_native",
        _ => kind.ToString(),
    };

    public static string[] SupportedNames() => All.Select(k => k.AsStr()).ToArray();
}
