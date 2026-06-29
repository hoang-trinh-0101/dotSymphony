using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: full HarnessCapability port with all capability structs.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthMode
{
    None,
    BearerToken,
    ApiKey,
    OAuth,
}

public sealed record TransportCapability(
    string Transport,
    List<string> Modes,
    List<string> SupportedEncodings,
    bool Bidirectional
);

public sealed record FeatureCapability(
    string Feature,
    bool Available,
    bool RequiresAuth,
    string? RequiresPlan
);

public sealed record HarnessEventStreamCapability(
    bool RuntimeEvents,
    bool TerminalFrames,
    bool ReplayFromCursor,
    bool RawPayloadRefs,
    List<string> DeliveryModes
);

public sealed record HarnessApprovalCapability(
    bool ToolApproval,
    bool HumanDecision,
    bool PolicyMetadata
);

public sealed record HarnessModelSettingsCapability(
    bool ApiCompatibleSettings,
    bool SubscriptionCredentials,
    bool PerRunOverrides,
    List<string> CredentialReferenceKinds
);

public sealed record HarnessTransportCapability(
    string Protocol,
    List<string> Modes,
    bool Local,
    bool Remote
);

public sealed record HarnessCancellationCapability(
    bool CancelRun,
    bool ForceStop,
    bool AcknowledgesCancel
);

public sealed record HarnessPauseResumeCapability(
    bool Pause,
    bool Resume
);

public sealed record HarnessHistoryCapability(
    bool FetchHistory,
    bool ReconcileAfterReady,
    bool ReconnectAndReplay,
    bool PreserveUnknownEvents
);

public sealed record HarnessCapability(
    string Kind,
    string DisplayName,
    bool Available,
    string AdapterContractVersion,
    string? RuntimeContractVersion,
    HarnessActionCapability Actions,
    HarnessEventStreamCapability EventStreams,
    HarnessApprovalCapability Approvals,
    HarnessModelSettingsCapability ModelSettings,
    HarnessTransportCapability Transport,
    HarnessCancellationCapability Cancellation,
    HarnessPauseResumeCapability PauseResume,
    HarnessHistoryCapability History,
    List<string> Notes,
    List<string> FeatureGaps
);

public sealed record HarnessActionCapability(
    bool StartRun,
    bool SendUserMessage,
    bool Retry,
    bool Cancel,
    bool Pause,
    bool Resume,
    bool Approve,
    bool Reject,
    bool Comment
);

public sealed record GatewayCapabilities(
    SchemaVersion SchemaVersion,
    string GatewayVersion,
    List<string> SupportedApiVersions,
    List<TransportCapability> Transports,
    List<HarnessCapability> Harnesses,
    List<FeatureCapability> Features,
    List<AuthMode> AuthModes,
    uint MaxEventPageSize,
    uint MaxTerminalFrameBatch
);

public static class HarnessKindCapabilityExtensions
{
    public static HarnessCapability Capability(this HarnessKind kind) => kind switch
    {
        HarnessKind.OpenHandsAgentServer => OpenHandsAgentServer(),
        HarnessKind.CodexAppServer => CodexAppServerLocal(),
        HarnessKind.RustNative => RustNativeFuture(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static HarnessCapability OpenHandsAgentServer() => new(
        "openhands_agent_server",
        "OpenHands agent-server",
        true,
        "harness-adapter-v1",
        "openhands-sdk-agent-server-v1",
        new HarnessActionCapability(true, true, true, true, false, false, false, false, true),
        new HarnessEventStreamCapability(true, true, true, true, ["http_history", "websocket"]),
        new HarnessApprovalCapability(false, false, false),
        new HarnessModelSettingsCapability(true, false, true, ["env"]),
        new HarnessTransportCapability("http_websocket", ["rest", "websocket"], true, true),
        new HarnessCancellationCapability(true, true, true),
        new HarnessPauseResumeCapability(false, false),
        new HarnessHistoryCapability(true, true, true, true),
        ["Initial production adapter; reuses one conversation per issue by default."],
        [
            "OpenHands pause/resume is not exposed by the current agent-server contract.",
            "Approval center normalization is reserved for a follow-up harness phase."
        ]
    );

    public static HarnessCapability CodexAppServerLocal() => new(
        "codex_app_server",
        "Codex app-server",
        true,
        "harness-adapter-v1",
        "codex-app-server-json-rpc-v2",
        new HarnessActionCapability(true, true, true, true, false, false, false, false, false),
        new HarnessEventStreamCapability(true, true, false, true, ["stdio"]),
        new HarnessApprovalCapability(false, false, false),
        new HarnessModelSettingsCapability(false, false, false, []),
        new HarnessTransportCapability("stdio", ["stdio"], true, false),
        new HarnessCancellationCapability(true, false, false),
        new HarnessPauseResumeCapability(false, false),
        new HarnessHistoryCapability(false, false, false, false),
        [
            "Supported local adapter path using `codex --dangerously-bypass-hook-trust app-server --stdio` with installed-schema validation.",
            "Requires a compatible Codex CLI with ChatGPT login available to the operator-owned Codex home."
        ],
        [
            "Codex issue execution remains local-stdio alpha and requires explicit harness selection.",
            "Approval decision forwarding to Codex is not wired for the local stdio adapter yet.",
            "Codex history fetch and reconnect replay cursors are not implemented for the local stdio adapter.",
            "Codex stdio reconciliation after readiness is not implemented; events are consumed from the live JSON-RPC stream.",
            "Harness-native comments are not implemented; tracker comments remain orchestrator-owned.",
            "Pause/resume semantics need protocol confirmation before being advertised as available.",
            "Hosted Codex worker pools and remote transport remain out of scope for the local adapter.",
            "Loopback WebSocket mode remains benchmark-only until exposure and auth policy are hardened."
        ]
    );

    public static HarnessCapability RustNativeFuture() => new(
        "rust_native",
        "Rust-native harness",
        false,
        "harness-adapter-v1",
        null,
        new HarnessActionCapability(true, true, true, true, true, true, true, true, true),
        new HarnessEventStreamCapability(true, true, true, true, ["in_process"]),
        new HarnessApprovalCapability(true, true, true),
        new HarnessModelSettingsCapability(true, true, true, ["env", "local_keychain"]),
        new HarnessTransportCapability("in_process", ["in_process"], true, false),
        new HarnessCancellationCapability(true, true, true),
        new HarnessPauseResumeCapability(true, true),
        new HarnessHistoryCapability(true, true, true, true),
        ["Future in-process Rust harness for orchestrator-owned execution."],
        ["Rust-native harness is not implemented; reserved for future orchestrator-owned execution."]
    );
}
