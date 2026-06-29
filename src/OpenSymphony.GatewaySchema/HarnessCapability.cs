namespace OpenSymphony.GatewaySchema;

// ht: minimal HarnessCapability port — only fields used by Orchestrator (Available, Actions.StartRun).
//   Full GatewaySchema port is a separate iteration. HarnessKind.Capability() returns the static
//   capability metadata for each known harness kind.
public sealed class HarnessCapability
{
    public string Kind { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Available { get; set; }
    public string AdapterContractVersion { get; set; } = "";
    public string? RuntimeContractVersion { get; set; }
    public HarnessActionCapability Actions { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> FeatureGaps { get; set; } = new();
}

public sealed class HarnessActionCapability
{
    public bool StartRun { get; set; }
    public bool SendUserMessage { get; set; }
    public bool Retry { get; set; }
    public bool Cancel { get; set; }
    public bool Pause { get; set; }
    public bool Resume { get; set; }
    public bool Approve { get; set; }
    public bool Reject { get; set; }
    public bool Comment { get; set; }
}

public static class HarnessKindCapabilityExtensions
{
    public static HarnessCapability Capability(this HarnessKind kind) => kind switch
    {
        HarnessKind.OpenHandsAgentServer => OpenHandsAgentServer(),
        HarnessKind.CodexAppServer => CodexAppServerLocal(),
        HarnessKind.RustNative => RustNativeFuture(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static HarnessCapability OpenHandsAgentServer() => new()
    {
        Kind = "openhands_agent_server",
        DisplayName = "OpenHands agent-server",
        Available = true,
        AdapterContractVersion = "harness-adapter-v1",
        RuntimeContractVersion = "openhands-sdk-agent-server-v1",
        Actions = new HarnessActionCapability
        {
            StartRun = true,
            SendUserMessage = true,
            Retry = true,
            Cancel = true,
            Comment = true,
        },
        Notes = new() { "Initial production adapter; reuses one conversation per issue by default." },
        FeatureGaps = new()
        {
            "OpenHands pause/resume is not exposed by the current agent-server contract.",
            "Approval center normalization is reserved for a follow-up harness phase.",
        },
    };

    public static HarnessCapability CodexAppServerLocal() => new()
    {
        Kind = "codex_app_server",
        DisplayName = "Codex app-server",
        Available = true,
        AdapterContractVersion = "harness-adapter-v1",
        RuntimeContractVersion = "codex-app-server-json-rpc-v2",
        Actions = new HarnessActionCapability
        {
            StartRun = true,
            SendUserMessage = true,
            Retry = true,
            Cancel = true,
        },
        Notes = new()
        {
            "Supported local adapter path using `codex --dangerously-bypass-hook-trust app-server --stdio` with installed-schema validation.",
            "Requires a compatible Codex CLI with ChatGPT login available to the operator-owned Codex home.",
        },
        FeatureGaps = new()
        {
            "Codex issue execution remains local-stdio alpha and requires explicit harness selection.",
            "Approval decision forwarding to Codex is not wired for the local stdio adapter yet.",
            "Codex history fetch and reconnect replay cursors are not implemented for the local stdio adapter.",
            "Codex stdio reconciliation after readiness is not implemented; events are consumed from the live JSON-RPC stream.",
            "Harness-native comments are not implemented; tracker comments remain orchestrator-owned.",
            "Pause/resume semantics need protocol confirmation before being advertised as available.",
            "Hosted Codex worker pools and remote transport remain out of scope for the local adapter.",
            "Loopback WebSocket mode remains benchmark-only until exposure and auth policy are hardened.",
        },
    };

    public static HarnessCapability RustNativeFuture() => new()
    {
        Kind = "rust_native",
        DisplayName = "Rust-native harness",
        Available = false,
        AdapterContractVersion = "harness-adapter-v1",
        Actions = new HarnessActionCapability
        {
            StartRun = true,
            SendUserMessage = true,
            Retry = true,
            Cancel = true,
            Pause = true,
            Resume = true,
            Approve = true,
            Reject = true,
            Comment = true,
        },
        Notes = new() { "Future in-process Rust harness for orchestrator-owned execution." },
        FeatureGaps = new()
        {
            "Rust-native harness is not implemented; reserved for future orchestrator-owned execution.",
        },
    };
}
