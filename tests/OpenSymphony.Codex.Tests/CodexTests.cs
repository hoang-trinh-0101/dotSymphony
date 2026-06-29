using System.Text.Json;
using OpenSymphony.Codex;
using OpenSymphony.GatewaySchema;
using Xunit;

namespace OpenSymphony.Codex.Tests;

public class CodexTests
{
    [Fact]
    public void Constants_AreCorrect()
    {
        Assert.Equal("codex_app_server", CodexConstants.CodexAppServerKind);
        Assert.Equal("codex-app-server-json-rpc-v2", CodexConstants.CodexAppServerContract);
    }

    [Fact]
    public void JsonRpcSession_AllocatesMonotonicIds()
    {
        var session = new CodexJsonRpcSession("test", "1.0");
        var req1 = session.Initialize();
        var req2 = session.ThreadStart(new CodexThreadStartParams());
        var req3 = session.TurnStart(new CodexTurnStartParams { ThreadId = "test" });

        Assert.Equal(1u, req1.Id);
        Assert.Equal(2u, req2.Id);
        Assert.Equal(3u, req3.Id);
        Assert.Equal("2.0", req1.JsonRpc);
        Assert.Equal("initialize", req1.Method);
    }

    [Fact]
    public void EventNormalization_ClassifiesKnownMethods()
    {
        var threadStart = JsonSerializer.Deserialize<JsonElement>(@"{""method"":""thread/start"",""params"":{""threadId"":""t1""}}");
        var normalized = CodexEventNormalization.NormalizeServerNotification(threadStart);

        Assert.NotNull(normalized);
        Assert.Equal(NormalizedCodexEventKind.ThreadStarted, normalized!.Kind);
        Assert.Equal("thread/start", normalized.Method);
        Assert.Equal("t1", normalized.ThreadId);
    }

    [Fact]
    public void EventNormalization_GeneratesSummary()
    {
        var evt = new NormalizedCodexEvent
        {
            Kind = NormalizedCodexEventKind.ThreadStarted,
            Method = "thread/start",
            ThreadId = "t1"
        };
        var summary = CodexEventNormalization.EventSummary(evt);

        Assert.Contains("thread started", summary);
        Assert.Contains("t1", summary);
    }

    [Fact]
    public void ModelCredentialReuse_FromProfile_ReturnsNullForIncompatibleHarness()
    {
        var profile = new ModelSettingsProfile(
            "test",
            "Test",
            OwnerScope.LocalUser,
            CredentialProvider.OpenAiCompatibleApi,
            CredentialMode.ApiKey,
            CredentialStorageMode.Environment,
            new ConfiguredValueReference(ConfiguredValueSource.EnvironmentVariable, "test"),
            null,
            new CredentialReference("test", CredentialReferenceKind.EnvironmentVariable, CredentialProvider.OpenAiCompatibleApi, CredentialStorageMode.Environment, "test", false),
            new List<string> { "openhands_agent_server" },
            CredentialStatusKind.Installed
        );

        var reuse = CodexModelCredentialReuse.FromProfile(profile);

        Assert.Null(reuse);
    }

    [Fact]
    public void ModelCredentialReuse_FromProfile_ReturnsValueForCompatibleHarness()
    {
        var profile = new ModelSettingsProfile(
            "test",
            "Test",
            OwnerScope.LocalUser,
            CredentialProvider.OpenAiChatGptCodex,
            CredentialMode.Subscription,
            CredentialStorageMode.CodexCliHome,
            new ConfiguredValueReference(ConfiguredValueSource.Literal, "gpt-5.5"),
            null,
            new CredentialReference("test", CredentialReferenceKind.CodexCliLogin, CredentialProvider.OpenAiChatGptCodex, CredentialStorageMode.CodexCliHome, "test", true),
            new List<string> { "codex_app_server" },
            CredentialStatusKind.Installed
        );

        var reuse = CodexModelCredentialReuse.FromProfile(profile);

        Assert.NotNull(reuse);
        Assert.Equal("test", reuse!.ProfileId);
        Assert.True(reuse.CanSupplySubscriptionCredentials);
    }

    [Fact]
    public void ContractGeneration_ToCommand_ReturnsCorrectArgs()
    {
        var gen = CodexContractGeneration.JsonSchema("/tmp/schema");
        var (program, args) = gen.ToCommand();

        Assert.Equal("codex", program);
        Assert.Contains("app-server", args);
        Assert.Contains("generate-json-schema", args);
        Assert.Contains("/tmp/schema", args);
    }

    [Fact]
    public void CodexAppServerAdapter_ImplementsHarnessAdapter()
    {
        var adapter = CodexAppServerAdapter.LocalStdio("codex", "test", "1.0");
        Assert.Equal("codex_app_server", adapter.HarnessKind);
        Assert.NotNull(adapter.Launch);
        Assert.NotNull(adapter.Session());
    }
}