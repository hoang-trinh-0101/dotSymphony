namespace OpenSymphony.Domain;

// ht: Minimal boundary shared by concrete harness adapters. Runtime execution
//   remains owned by the orchestrator and concrete adapter modules. This gives
//   the host and gateway a stable capability discovery surface without leaking
//   private OpenHands, Codex, or future in-process types into client-facing DTOs.
//
// TODO: Capabilities() pending OpenSymphony.GatewaySchema.HarnessCapability
//   (Domain cannot reference GatewaySchema — cycle). Port only HarnessKind here.
public interface IHarnessAdapter
{
    string HarnessKind { get; }
    // Implementations are expected to be thread-safe (Rust trait required Send + Sync).
}
