using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Workspace;

// ht: IssueDescriptor is not serialized (no Serialize/Deserialize in Rust).
public sealed record IssueDescriptor(
    string IssueId,
    string Identifier,
    string Title,
    string CurrentState,
    DateTimeOffset? LastSeenTrackerRefreshAt);

public sealed record RunDescriptor(string RunId, uint Attempt)
{
    public static RunDescriptor New(string runId, uint attempt) => new(runId, attempt);
}

public sealed record HookDefinition(string Command, string? Cwd)
{
    public static HookDefinition Shell(string command) => new(command, null);
    public HookDefinition WithCwd(string cwd) => this with { Cwd = cwd };
}

public sealed record HookConfig(
    HookDefinition? AfterCreate,
    HookDefinition? BeforeRun,
    HookDefinition? AfterRun,
    HookDefinition? BeforeRemove,
    TimeSpan Timeout)
{
    public static HookConfig Default() => new(null, null, null, null, TimeSpan.FromSeconds(60));
}

public sealed record CleanupConfig(bool RemoveTerminalWorkspaces)
{
    public static CleanupConfig Default() => new(false);
}

public sealed record WorkspaceManagerConfig(string Root, HookConfig Hooks, CleanupConfig Cleanup);

public sealed class WorkspaceHandle
{
    public string IssueId { get; }
    public string Identifier { get; }
    public string WorkspaceKey { get; }
    public string WorkspacePath { get; }

    internal WorkspaceHandle(string issueId, string identifier, string workspaceKey, string workspacePath)
    {
        IssueId = issueId;
        Identifier = identifier;
        WorkspaceKey = workspaceKey;
        WorkspacePath = workspacePath;
    }

    public string MetadataDir() => Path.Join(WorkspacePath, ".opensymphony");
    internal string AfterCreateReceiptPath() => Path.Join(WorkspacePath, ".opensymphony.after_create.json");
    public string IssueManifestPath() => Path.Join(MetadataDir(), "issue.json");
    public string RunManifestPath() => Path.Join(MetadataDir(), "run.json");
    public string ConversationManifestPath() => Path.Join(MetadataDir(), "conversation.json");
    public string LogsDir() => Path.Join(MetadataDir(), "logs");
    public string GeneratedDir() => Path.Join(MetadataDir(), "generated");
    public string OpenhandsDir() => Path.Join(MetadataDir(), "openhands");
    public string PromptsDir() => Path.Join(MetadataDir(), "prompts");
    public string RunsDir() => Path.Join(MetadataDir(), "runs");
    public string LatestPromptPath(PromptKind kind) => Path.Join(PromptsDir(), $"last-{kind.FileStem()}-prompt.md");
    public string LatestPromptManifestPath(PromptKind kind) => Path.Join(PromptsDir(), $"last-{kind.FileStem()}-prompt.json");
    public string RunArtifactsDir(uint attempt) => Path.Join(RunsDir(), $"attempt-{attempt:0000}");
    public string RunPromptPath(uint attempt, PromptKind kind, uint sequence) => Path.Join(RunArtifactsDir(attempt), $"prompt-{kind.FileStem()}-{sequence:000}.md");
    public string RunPromptManifestPath(uint attempt, PromptKind kind, uint sequence) => Path.Join(RunArtifactsDir(attempt), $"prompt-{kind.FileStem()}-{sequence:000}.json");
    public string IssueContextPath() => Path.Join(GeneratedDir(), "issue-context.md");
    public string MemoryContextPath() => Path.Join(GeneratedDir(), "memory-context.md");
    public string SessionContextPath() => Path.Join(GeneratedDir(), "session-context.json");

    public bool Equals(WorkspaceHandle? other) =>
        other is not null && IssueId == other.IssueId && Identifier == other.Identifier
        && WorkspaceKey == other.WorkspaceKey && WorkspacePath == other.WorkspacePath;
    public override bool Equals(object? obj) => obj is WorkspaceHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IssueId, Identifier, WorkspaceKey, WorkspacePath);
}

// ht: internal receipt — not exported in Rust (pub(crate)).
[JsonConverter(typeof(AfterCreateBootstrapReceiptConverter))]
public sealed record AfterCreateBootstrapReceipt(
    string IssueId,
    string Identifier,
    string SanitizedWorkspaceKey,
    string WorkspacePath,
    DateTimeOffset CompletedAt)
{
    internal static AfterCreateBootstrapReceipt New(WorkspaceHandle workspace, IssueDescriptor issue)
        => new(issue.IssueId, issue.Identifier, workspace.WorkspaceKey, workspace.WorkspacePath, DateTimeOffset.UtcNow);
}

internal sealed class AfterCreateBootstrapReceiptConverter : JsonConverter<AfterCreateBootstrapReceipt>
{
    public override AfterCreateBootstrapReceipt Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = System.Text.Json.JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new AfterCreateBootstrapReceipt(
            root.GetProperty("issue_id").GetString()!,
            root.GetProperty("identifier").GetString()!,
            root.GetProperty("sanitized_workspace_key").GetString()!,
            root.GetProperty("workspace_path").GetString()!,
            root.GetProperty("completed_at").Deserialize<DateTimeOffset>(options));
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, AfterCreateBootstrapReceipt value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("issue_id", value.IssueId);
        writer.WriteString("identifier", value.Identifier);
        writer.WriteString("sanitized_workspace_key", value.SanitizedWorkspaceKey);
        writer.WriteString("workspace_path", value.WorkspacePath);
        writer.WritePropertyName("completed_at");
        JsonSerializer.Serialize(writer, value.CompletedAt, options);
        writer.WriteEndObject();
    }
}

public sealed record EnsureWorkspaceResult(
    WorkspaceHandle Handle,
    IssueManifest IssueManifest,
    bool Created,
    HookExecutionRecord? AfterCreate);

public enum IssueLifecycleState { Active, Inactive, Terminal }

public enum CleanupDecision { Retain, Remove }

public sealed record CleanupOutcome(CleanupDecision Decision, HookExecutionRecord? BeforeRemove);

// ht: snake_case enum via JsonStringEnumConverter(SnakeCaseLower). ToString matches Rust Display.
public enum HookKind { AfterCreate, BeforeRun, AfterRun, BeforeRemove }

public static class HookKindExtensions
{
    public static bool IsRequired(this HookKind kind) => kind is HookKind.AfterCreate or HookKind.BeforeRun;
    public static string ToSnakeCaseString(this HookKind kind) => kind switch
    {
        HookKind.AfterCreate => "after_create",
        HookKind.BeforeRun => "before_run",
        HookKind.AfterRun => "after_run",
        HookKind.BeforeRemove => "before_remove",
        _ => kind.ToString(),
    };
}

public enum HookExecutionStatus { Succeeded, Failed, TimedOut }

// ht: stdout/stderr use [JsonIgnore(WhenWritingDefault)] to match Rust skip_serializing_if = String::is_empty.
public sealed record HookExecutionRecord(
    HookKind Kind,
    string Command,
    string Cwd,
    bool BestEffort,
    HookExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ulong DurationMs,
    int? ExitCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string Stdout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string Stderr);

// ht: last_seen_tracker_refresh_at uses [JsonIgnore(WhenWritingNull)] to match Rust skip_serializing_if = Option::is_none.
public sealed record IssueManifest(
    string IssueId,
    string Identifier,
    string Title,
    string CurrentState,
    string SanitizedWorkspaceKey,
    string WorkspacePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? LastSeenTrackerRefreshAt);

public enum RunStatus { Preparing, Prepared, Running, Paused, Succeeded, Failed, Cancelled, PreparationFailed }

public static class RunStatusExtensions
{
    public static string ToSnakeCaseString(this RunStatus status) => status switch
    {
        RunStatus.Preparing => "preparing",
        RunStatus.Prepared => "prepared",
        RunStatus.Running => "running",
        RunStatus.Paused => "paused",
        RunStatus.Succeeded => "succeeded",
        RunStatus.Failed => "failed",
        RunStatus.Cancelled => "cancelled",
        RunStatus.PreparationFailed => "preparation_failed",
        _ => status.ToString(),
    };
}

// ht: status_detail WhenWritingNull; hooks [JsonIgnore(Never)] to always serialize (even empty).
public sealed class RunManifest
{
    public string RunId { get; set; }
    public string IssueId { get; set; }
    public string Identifier { get; set; }
    public string SanitizedWorkspaceKey { get; set; }
    public string WorkspacePath { get; set; }
    public uint Attempt { get; set; }
    public RunStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusDetail { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<HookExecutionRecord> Hooks { get; set; } = new();

    public RunManifest(string runId, string issueId, string identifier, string sanitizedWorkspaceKey,
        string workspacePath, uint attempt, RunStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        string? statusDetail, List<HookExecutionRecord> hooks)
    {
        RunId = runId; IssueId = issueId; Identifier = identifier; SanitizedWorkspaceKey = sanitizedWorkspaceKey;
        WorkspacePath = workspacePath; Attempt = attempt; Status = status; CreatedAt = createdAt; UpdatedAt = updatedAt;
        StatusDetail = statusDetail; Hooks = hooks;
    }

    public static RunManifest New(WorkspaceHandle workspace, RunDescriptor run)
    {
        var now = DateTimeOffset.UtcNow;
        return new RunManifest(
            run.RunId, workspace.IssueId, workspace.Identifier, workspace.WorkspaceKey,
            workspace.WorkspacePath, run.Attempt, RunStatus.Preparing, now, now, null, new());
    }

    public bool Equals(RunManifest? other) =>
        other is not null && RunId == other.RunId && IssueId == other.IssueId && Identifier == other.Identifier
        && SanitizedWorkspaceKey == other.SanitizedWorkspaceKey && WorkspacePath == other.WorkspacePath
        && Attempt == other.Attempt && Status == other.Status && CreatedAt == other.CreatedAt
        && UpdatedAt == other.UpdatedAt && StatusDetail == other.StatusDetail
        && Hooks.SequenceEqual(other.Hooks);
    public override bool Equals(object? obj) => obj is RunManifest other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(RunId, IssueId, Attempt, Status);
}

public sealed class ConversationManifest
{
    public string IssueId { get; set; }
    public string Identifier { get; set; }
    public string ConversationId { get; set; }
    public string ServerBaseUrl { get; set; }
    public string PersistenceDir { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastAttachedAt { get; set; }
    public bool FreshConversation { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResetReason { get; set; }
    public string RuntimeContractVersion { get; set; }

    public ConversationManifest(string issueId, string identifier, string conversationId, string serverBaseUrl,
        string persistenceDir, DateTimeOffset createdAt, DateTimeOffset? lastAttachedAt, bool freshConversation,
        string? resetReason, string runtimeContractVersion)
    {
        IssueId = issueId; Identifier = identifier; ConversationId = conversationId; ServerBaseUrl = serverBaseUrl;
        PersistenceDir = persistenceDir; CreatedAt = createdAt; LastAttachedAt = lastAttachedAt;
        FreshConversation = freshConversation; ResetReason = resetReason; RuntimeContractVersion = runtimeContractVersion;
    }

    public static ConversationManifest New(WorkspaceHandle workspace, string conversationId, string serverBaseUrl,
        string persistenceDir, string runtimeContractVersion)
        => new(workspace.IssueId, workspace.Identifier, conversationId, serverBaseUrl, persistenceDir,
            DateTimeOffset.UtcNow, null, true, null, runtimeContractVersion);

    public bool Equals(ConversationManifest? other) =>
        other is not null && IssueId == other.IssueId && Identifier == other.Identifier
        && ConversationId == other.ConversationId && ServerBaseUrl == other.ServerBaseUrl
        && PersistenceDir == other.PersistenceDir && CreatedAt == other.CreatedAt
        && LastAttachedAt == other.LastAttachedAt && FreshConversation == other.FreshConversation
        && ResetReason == other.ResetReason && RuntimeContractVersion == other.RuntimeContractVersion;
    public override bool Equals(object? obj) => obj is ConversationManifest other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IssueId, ConversationId, CreatedAt);
}

public enum PromptKind { Full, Continuation }

public static class PromptKindExtensions
{
    public static string FileStem(this PromptKind kind) => kind switch
    {
        PromptKind.Full => "full",
        PromptKind.Continuation => "continuation",
        _ => kind.ToString(),
    };
    public static string ToSnakeCaseString(this PromptKind kind) => kind.FileStem();
}

public sealed record PromptCaptureDescriptor(PromptKind Kind, uint Sequence)
{
    public static PromptCaptureDescriptor New(PromptKind kind, uint sequence) => new(kind, sequence);
}

public sealed record PromptCaptureManifest(
    string IssueId,
    string Identifier,
    string RunId,
    uint Attempt,
    PromptKind PromptKind,
    uint Sequence,
    string WorkspacePath,
    string ArchivedPromptPath,
    string StablePromptPath,
    DateTimeOffset CapturedAt,
    ulong PromptLengthBytes)
{
    public static PromptCaptureManifest New(WorkspaceHandle workspace, RunDescriptor run, PromptCaptureDescriptor descriptor, string prompt)
        => new(
            workspace.IssueId, workspace.Identifier, run.RunId, run.Attempt,
            descriptor.Kind, descriptor.Sequence, workspace.WorkspacePath,
            workspace.RunPromptPath(run.Attempt, descriptor.Kind, descriptor.Sequence),
            workspace.LatestPromptPath(descriptor.Kind),
            DateTimeOffset.UtcNow, (ulong)prompt.Length);
}

// ht: IssueContextArtifact is not serialized (no Serialize/Deserialize in Rust) — only rendered as markdown.
public sealed record IssueContextArtifact(
    string IssueId,
    string Identifier,
    string Title,
    string CurrentState,
    string RepoWorkflowPath,
    string? RepoAgentsPath,
    string? RepoSkillsDir,
    RunStatus? LastRunStatus,
    List<string> ImportantConstraints,
    List<string> KnownBlockers)
{
    public string RenderMarkdown(WorkspaceHandle workspace)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# OpenSymphony Issue Context");
        sb.AppendLine();
        sb.AppendLine("Repository-owned policy remains authoritative.");
        sb.AppendLine("These generated notes reference repo-owned files without overwriting them.");
        sb.AppendLine();
        sb.AppendLine($"- issue: {Identifier}");
        sb.AppendLine($"- issue id: {IssueId}");
        sb.AppendLine($"- title: {Title}");
        sb.AppendLine($"- current state: {CurrentState}");
        sb.AppendLine($"- last run status: {(LastRunStatus is RunStatus s ? s.ToSnakeCaseString() : "unknown")}");
        sb.AppendLine();
        sb.AppendLine("## Repository Context");
        sb.AppendLine();
        sb.AppendLine($"- WORKFLOW.md: {RepoWorkflowPath}");
        sb.AppendLine($"- AGENTS.md: {RepoAgentsPath ?? "absent"}");
        sb.AppendLine($"- .agents/skills/: {RepoSkillsDir ?? "absent"}");
        sb.AppendLine();
        sb.AppendLine("## OpenSymphony Artifacts");
        sb.AppendLine();
        sb.AppendLine($"- issue manifest: {workspace.IssueManifestPath()}");
        sb.AppendLine($"- run manifest: {workspace.RunManifestPath()}");
        sb.AppendLine($"- conversation manifest: {workspace.ConversationManifestPath()}");
        sb.AppendLine($"- latest full prompt: {workspace.LatestPromptPath(PromptKind.Full)}");
        sb.AppendLine($"- latest continuation prompt: {workspace.LatestPromptPath(PromptKind.Continuation)}");
        sb.AppendLine($"- session context: {workspace.SessionContextPath()}");
        sb.AppendLine($"- memory context: {workspace.MemoryContextPath()}");
        if (ImportantConstraints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Important Constraints");
            sb.AppendLine();
            foreach (var constraint in ImportantConstraints)
                sb.AppendLine($"- {constraint}");
        }
        if (KnownBlockers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Known Blockers");
            sb.AppendLine();
            foreach (var blocker in KnownBlockers)
                sb.AppendLine($"- {blocker}");
        }
        return sb.ToString();
    }
}

// ht: all Option fields WhenWritingNull; recent_validation_commands [JsonIgnore(Never)].
public sealed class SessionContextArtifact
{
    public string IssueId { get; set; }
    public string Identifier { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Attempt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastRunId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunStatus? LastRunStatus { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PromptKind? LastPromptKind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastPromptPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<string> RecentValidationCommands { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastRetryReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SessionContextArtifact(string issueId, string identifier, string? conversationId, uint? attempt,
        string? lastRunId, RunStatus? lastRunStatus, PromptKind? lastPromptKind, string? lastPromptPath,
        List<string> recentValidationCommands, string? lastRetryReason, DateTimeOffset updatedAt)
    {
        IssueId = issueId; Identifier = identifier; ConversationId = conversationId; Attempt = attempt;
        LastRunId = lastRunId; LastRunStatus = lastRunStatus; LastPromptKind = lastPromptKind;
        LastPromptPath = lastPromptPath; RecentValidationCommands = recentValidationCommands;
        LastRetryReason = lastRetryReason; UpdatedAt = updatedAt;
    }

    public static SessionContextArtifact New(WorkspaceHandle workspace)
        => new(workspace.IssueId, workspace.Identifier, null, null, null, null, null, null,
            new(), null, DateTimeOffset.UtcNow);

    public bool Equals(SessionContextArtifact? other) =>
        other is not null && IssueId == other.IssueId && Identifier == other.Identifier
        && ConversationId == other.ConversationId && Attempt == other.Attempt
        && LastRunId == other.LastRunId && LastRunStatus == other.LastRunStatus
        && LastPromptKind == other.LastPromptKind && LastPromptPath == other.LastPromptPath
        && RecentValidationCommands.SequenceEqual(other.RecentValidationCommands)
        && LastRetryReason == other.LastRetryReason && UpdatedAt == other.UpdatedAt;
    public override bool Equals(object? obj) => obj is SessionContextArtifact other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IssueId, Identifier, UpdatedAt);
}
