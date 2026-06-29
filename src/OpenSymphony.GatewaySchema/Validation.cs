using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of validation types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped,
    Error,
}

public sealed record RunValidationSummary(
    SchemaVersion SchemaVersion,
    string RunId,
    DateTimeOffset GeneratedAt,
    ValidationStatus OverallStatus,
    List<ValidationCommand> Commands,
    List<ValidationEvidenceItem> Evidence
);

public sealed record ValidationCommand(
    string CommandId,
    string Command,
    ValidationStatus Status,
    int? ExitCode,
    string? StdoutSummary,
    string? StderrSummary
);

public sealed record ValidationEvidenceItem(
    string EvidenceId,
    string Label,
    ValidationStatus Status,
    string Summary,
    string? FilePath,
    uint? LineNumber,
    RunAction? ActionTriggered
);