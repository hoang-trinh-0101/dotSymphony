using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of model settings types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OwnerScope
{
    LocalUser,
    Workspace,
    Project,
    Organization,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialProvider
{
    OpenAiCompatibleApi,
    OpenAiChatGptCodex,
    HostedCredentialBroker,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialMode
{
    ApiKey,
    Subscription,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialStorageMode
{
    Environment,
    LocalKeychain,
    CodexCliHome,
    OpenHandsAuthDirectory,
    HostedBroker,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConfiguredValueSource
{
    EnvironmentVariable,
    Literal,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialReferenceKind
{
    EnvironmentVariable,
    LocalKeychainServiceAccount,
    CodexCliLogin,
    OpenHandsAuthDirectory,
    HostedBrokerReference,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialStatusKind
{
    Installed,
    LoggedOut,
    Expired,
    Unsupported,
    PermissionDenied,
    Unknown,
}

public sealed record ConfiguredValueReference(
    ConfiguredValueSource Source,
    string Reference
);

public sealed record CredentialReference(
    string Id,
    CredentialReferenceKind Kind,
    CredentialProvider Provider,
    CredentialStorageMode StorageMode,
    string Reference,
    bool Redacted
);

public sealed record CredentialStatus(
    string CredentialReferenceId,
    CredentialProvider Provider,
    CredentialStatusKind Status,
    string CheckedBy,
    string Detail
);

public sealed record ModelSettingsProfile(
    string Id,
    string DisplayName,
    OwnerScope OwnerScope,
    CredentialProvider Provider,
    CredentialMode CredentialMode,
    CredentialStorageMode StorageMode,
    ConfiguredValueReference Model,
    ConfiguredValueReference? BaseUrl,
    CredentialReference CredentialReference,
    List<string> CompatibleHarnesses,
    CredentialStatusKind Status
);

public sealed record CodexLocalReadiness(
    string Command,
    string? Version,
    CredentialStatusKind CliStatus,
    CredentialStatusKind AppServerStatus,
    CredentialStatusKind LoginStatus,
    CredentialStatusKind SubscriptionStatus,
    string CheckedBy,
    string Detail,
    string LoginCommand,
    string StatusCommand,
    string LogoutCommand
)
{
    public static CodexLocalReadiness NotChecked() => new(
        "codex",
        null,
        CredentialStatusKind.Unknown,
        CredentialStatusKind.Unknown,
        CredentialStatusKind.Unknown,
        CredentialStatusKind.Unknown,
        "gateway_static_settings",
        "Codex CLI readiness not checked",
        "codex login --device-auth",
        "codex status",
        "codex logout"
    );
}

public sealed record ModelSettingsResponse(
    SchemaVersion SchemaVersion,
    List<ModelSettingsProfile> Profiles,
    List<CredentialStatus> CredentialStatuses,
    List<CredentialStatusKind> SupportedCredentialStatuses,
    CodexLocalReadiness CodexLocalReadiness,
    List<string> Notes
)
{
    public static ModelSettingsResponse LocalDefault(bool llmApiKeyInstalled) =>
        LocalWithCodexReadiness(llmApiKeyInstalled, CodexLocalReadiness.NotChecked());

    public static ModelSettingsResponse LocalWithCodexReadiness(
        bool llmApiKeyInstalled,
        CodexLocalReadiness codexLocalReadiness
    )
    {
        var apiKeyStatus = llmApiKeyInstalled ? CredentialStatusKind.Installed : CredentialStatusKind.LoggedOut;
        var codexStatus = codexLocalReadiness.SubscriptionStatus;
        var profiles = new List<ModelSettingsProfile>
        {
            OpenHandsApiKeyProfile(apiKeyStatus),
            CodexCliLoginProfile(codexStatus),
            OpenHandsAuthDirectoryProfile(),
            HostedBrokerFutureProfile()
        };
        var credentialStatuses = profiles.Select(profile =>
        {
            var status = CredentialStatusExtensions.FromProfile(profile);
            if (profile.CredentialReference.Kind == CredentialReferenceKind.CodexCliLogin)
            {
                status = status with { CheckedBy = codexLocalReadiness.CheckedBy };
            }
            return status;
        }).ToList();

        return new(
            SchemaVersion.V1(),
            profiles,
            credentialStatuses,
            GetSupportedCredentialStatuses(),
            codexLocalReadiness,
            [
                "API-key profiles preserve OpenHands-compatible LLM_MODEL, LLM_API_KEY, and LLM_BASE_URL environment wiring.",
                "Subscription profiles expose credential references only; raw OAuth and API-key material stays in the selected storage backend.",
                "Codex subscription readiness is detected through supported Codex CLI commands, not by reading private credential files.",
                "Hosted broker references are shape-compatible placeholders for the follow-up hosted secret-store implementation."
            ]
        );
    }

    private static List<CredentialStatusKind> GetSupportedCredentialStatuses() =>
    [
        CredentialStatusKind.Installed,
        CredentialStatusKind.LoggedOut,
        CredentialStatusKind.Expired,
        CredentialStatusKind.Unsupported,
        CredentialStatusKind.PermissionDenied,
        CredentialStatusKind.Unknown
    ];

    private static ModelSettingsProfile OpenHandsApiKeyProfile(CredentialStatusKind status) => new(
        "openhands-env-api-key",
        "OpenHands API Key (Environment)",
        OwnerScope.LocalUser,
        CredentialProvider.OpenAiCompatibleApi,
        CredentialMode.ApiKey,
        CredentialStorageMode.Environment,
        new(ConfiguredValueSource.EnvironmentVariable, "LLM_MODEL"),
        new(ConfiguredValueSource.EnvironmentVariable, "LLM_BASE_URL"),
        new("credential:env:LLM_API_KEY", CredentialReferenceKind.EnvironmentVariable, CredentialProvider.OpenAiCompatibleApi, CredentialStorageMode.Environment, "LLM_API_KEY", false),
        ["openhands_agent_server"],
        status
    );

    private static ModelSettingsProfile CodexCliLoginProfile(CredentialStatusKind status) => new(
        "codex-chatgpt-local-keychain",
        "Codex ChatGPT (Local Keychain)",
        OwnerScope.LocalUser,
        CredentialProvider.OpenAiChatGptCodex,
        CredentialMode.Subscription,
        CredentialStorageMode.CodexCliHome,
        new(ConfiguredValueSource.Literal, "gpt-5.5"),
        null,
        new("credential:codex-cli:chatgpt-login", CredentialReferenceKind.CodexCliLogin, CredentialProvider.OpenAiChatGptCodex, CredentialStorageMode.CodexCliHome, "codex-cli:chatgpt-login", true),
        ["codex_app_server"],
        status
    );

    private static ModelSettingsProfile OpenHandsAuthDirectoryProfile() => new(
        "openhands-chatgpt-auth-directory",
        "OpenHands ChatGPT (Auth Directory)",
        OwnerScope.LocalUser,
        CredentialProvider.OpenAiChatGptCodex,
        CredentialMode.Subscription,
        CredentialStorageMode.OpenHandsAuthDirectory,
        new(ConfiguredValueSource.Literal, "gpt-5.5"),
        null,
        new("credential:openhands-auth:chatgpt", CredentialReferenceKind.OpenHandsAuthDirectory, CredentialProvider.OpenAiChatGptCodex, CredentialStorageMode.OpenHandsAuthDirectory, "openhands-auth:chatgpt", true),
        ["openhands_agent_server"],
        CredentialStatusKind.LoggedOut
    );

    private static ModelSettingsProfile HostedBrokerFutureProfile() => new(
        "hosted-credential-broker",
        "Hosted Credential Broker (Future)",
        OwnerScope.Organization,
        CredentialProvider.HostedCredentialBroker,
        CredentialMode.Subscription,
        CredentialStorageMode.HostedBroker,
        new(ConfiguredValueSource.Literal, "gpt-5.5"),
        null,
        new("credential:hosted:broker", CredentialReferenceKind.HostedBrokerReference, CredentialProvider.HostedCredentialBroker, CredentialStorageMode.HostedBroker, "hosted:broker", true),
        [],
        CredentialStatusKind.Unsupported
    );
}

public sealed record CredentialStatusResponse(
    SchemaVersion SchemaVersion,
    List<CredentialStatus> Statuses,
    List<CredentialStatusKind> SupportedStatuses
)
{
    public static CredentialStatusResponse FromModelSettings(ModelSettingsResponse settings) => new(
        settings.SchemaVersion,
        settings.CredentialStatuses,
        settings.SupportedCredentialStatuses
    );
}

public static class CredentialStatusExtensions
{
    public static CredentialStatus FromProfile(ModelSettingsProfile profile) => new(
        profile.CredentialReference.Id,
        profile.Provider,
        profile.Status,
        "gateway_static_settings",
        StatusDetail(profile)
    );

    private static string StatusDetail(ModelSettingsProfile profile) => profile.Status switch
    {
        CredentialStatusKind.Installed => $"Credential reference {profile.CredentialReference.Reference} is installed and available.",
        CredentialStatusKind.LoggedOut => $"Credential reference {profile.CredentialReference.Reference} is not logged in or configured.",
        CredentialStatusKind.Expired => $"Credential reference {profile.CredentialReference.Reference} has expired.",
        CredentialStatusKind.Unsupported => $"Credential reference {profile.CredentialReference.Reference} is not supported in this environment.",
        CredentialStatusKind.PermissionDenied => $"Permission denied accessing credential reference {profile.CredentialReference.Reference}.",
        CredentialStatusKind.Unknown => $"Status of credential reference {profile.CredentialReference.Reference} is unknown.",
        _ => $"Unknown status for credential reference {profile.CredentialReference.Reference}."
    };
}