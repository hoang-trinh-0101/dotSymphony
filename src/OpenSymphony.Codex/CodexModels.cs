using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Codex;

public class JsonRpcRequestEnvelope
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public ulong Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object Params { get; set; } = new();
}

public class CodexThreadStartParams
{
    [JsonPropertyName("approvalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodexApprovalPolicy? ApprovalPolicy { get; set; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonPropertyName("modelProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelProvider { get; set; }

    [JsonPropertyName("baseInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseInstructions { get; set; }

    [JsonPropertyName("developerInstructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeveloperInstructions { get; set; }

    [JsonPropertyName("ephemeral")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ephemeral { get; set; }

    [JsonPropertyName("sandbox")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodexThreadSandboxMode? Sandbox { get; set; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Config { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CodexApprovalPolicy
{
    [JsonPropertyName("never")]
    Never
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CodexThreadSandboxMode
{
    [JsonPropertyName("danger-full-access")]
    DangerFullAccess
}

public class CodexSandboxPolicy
{
    [JsonPropertyName("type")]
    public CodexSandboxPolicyType PolicyType { get; set; }

    public static CodexSandboxPolicy DangerFullAccess() =>
        new() { PolicyType = CodexSandboxPolicyType.DangerFullAccess };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CodexSandboxPolicyType
{
    [JsonPropertyName("dangerFullAccess")]
    DangerFullAccess
}

public class CodexTurnStartParams
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public List<CodexUserInput> Input { get; set; } = new();

    [JsonPropertyName("approvalPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodexApprovalPolicy? ApprovalPolicy { get; set; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonPropertyName("sandboxPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodexSandboxPolicy? SandboxPolicy { get; set; }

    [JsonPropertyName("clientUserMessageId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientUserMessageId { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CodexUserInputText), "text")]
public abstract record CodexUserInput;

public record CodexUserInputText : CodexUserInput
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("textElements")]
    public List<object> TextElements { get; set; } = new();
}

public record CodexModelCredentialReuse
{
    public string ProfileId { get; set; } = string.Empty;
    public ConfiguredValueSource ModelSource { get; set; }
    public string ModelReference { get; set; } = string.Empty;
    public string CredentialReferenceId { get; set; } = string.Empty;
    public CredentialReferenceKind CredentialReferenceKind { get; set; }
    public CredentialStorageMode StorageMode { get; set; }
    public bool CanSupplySubscriptionCredentials { get; set; }
    public SortedDictionary<string, string> ConfigOverrides { get; set; } = new();

    public static CodexModelCredentialReuse? FromProfile(ModelSettingsProfile profile)
    {
        if (!profile.CompatibleHarnesses.Contains(CodexConstants.CodexAppServerKind))
            return null;

        var configOverrides = new SortedDictionary<string, string>();
        if (profile.Model.Source == ConfiguredValueSource.Literal)
            configOverrides["model"] = profile.Model.Reference;

        return new CodexModelCredentialReuse
        {
            ProfileId = profile.Id,
            ModelSource = profile.Model.Source,
            ModelReference = profile.Model.Reference,
            CredentialReferenceId = profile.CredentialReference.Id,
            CredentialReferenceKind = profile.CredentialReference.Kind,
            StorageMode = profile.StorageMode,
            CanSupplySubscriptionCredentials = profile.CredentialMode == CredentialMode.Subscription,
            ConfigOverrides = configOverrides
        };
    }
}

public record CodexTokenUsage
{
    public ulong InputTokens { get; set; }
    public ulong OutputTokens { get; set; }
    public ulong CacheReadTokens { get; set; }
    public ulong TotalTokens { get; set; }
}

public enum NormalizedCodexEventKind
{
    ThreadStarted,
    ThreadStatusChanged,
    TokenUsageUpdated,
    TurnStarted,
    TurnCompleted,
    TurnCancelled,
    TurnDiffUpdated,
    ItemStarted,
    ItemCompleted,
    AgentMessageDelta,
    CommandExecutionOutputDelta,
    PlanDelta,
    ApprovalRequested,
    ApprovalCompleted,
    Error,
    Unknown
}

public record NormalizedCodexEvent
{
    public NormalizedCodexEventKind Kind { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public string? TurnId { get; set; }
    public string? ItemId { get; set; }
    public string? MessageDelta { get; set; }
    public CodexTokenUsage? TokenUsage { get; set; }
    public JsonElement Raw { get; set; }
}