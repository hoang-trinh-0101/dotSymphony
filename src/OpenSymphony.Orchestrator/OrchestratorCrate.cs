namespace OpenSymphony.Orchestrator;

// ht: Port of older/crates/opensymphony-orchestrator/src/lib.rs.
//   Rust `pub use` re-exports are implicit via `using` in C# consumers.
//   Only CRATE_NAME and boundary_summary need explicit definitions.
public static class OrchestratorCrate
{
    public const string CrateName = "opensymphony-orchestrator";

    public const string BoundarySummaryValue =
        "poll tick, runtime state machine, worker supervision, retry queue, cancellation/reconciliation, and snapshot derivation inputs";

    public static string BoundarySummary() => BoundarySummaryValue;
}
