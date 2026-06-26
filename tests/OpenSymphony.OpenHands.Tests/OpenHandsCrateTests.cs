using OpenSymphony.OpenHands;
using OpenSymphony.Domain;

namespace OpenSymphony.OpenHands.Tests;

public class OpenHandsCrateTests
{
    [Fact]
    public void ReportsItsBoundary()
    {
        Assert.Equal("opensymphony-openhands", OpenHandsCrate.CrateName);
        Assert.Contains("event normalization", OpenHandsCrate.CrateSummary());
        Assert.Contains("runtime state mirror", OpenHandsCrate.CrateSummary());
    }
}

public class TransportConfigTests
{
    [Fact]
    public void New_SetsBaseUrlAndDefaultAuth()
    {
        var config = new TransportConfig("http://127.0.0.1:8000");
        Assert.Equal("http://127.0.0.1:8000", config.BaseUrl);
        Assert.Equal(TransportAuthKind.None, config.Auth.HttpAuthKind());
    }

    [Fact]
    public void WithAuth_SetsAuth()
    {
        var config = new TransportConfig("http://127.0.0.1:8000")
            .WithAuth(AuthConfig.HeaderApiKey("x-session-api-key", "secret"));
        Assert.Equal(TransportAuthKind.Header, config.Auth.HttpAuthKind());
    }

    [Fact]
    public void Diagnostics_ReportsLoopbackForLocalhost()
    {
        var config = new TransportConfig("http://127.0.0.1:8000");
        var diag = config.Diagnostics().Value;
        Assert.Equal(TransportTargetKind.Loopback, diag.TargetKind);
        Assert.True(diag.ManagedLocalServerCandidate);
    }

    [Fact]
    public void Diagnostics_ReportsRemoteForExternalHost()
    {
        var config = new TransportConfig("http://example.com:8000");
        var diag = config.Diagnostics().Value;
        Assert.Equal(TransportTargetKind.Remote, diag.TargetKind);
        Assert.False(diag.ManagedLocalServerCandidate);
    }

    [Fact]
    public void ManagedLocalServerBaseUrl_ReturnsUrlForLoopbackHttp()
    {
        var config = new TransportConfig("http://127.0.0.1:8000");
        var url = config.ManagedLocalServerBaseUrl().Value;
        Assert.NotNull(url);
        Assert.Contains("127.0.0.1", url);
    }

    [Fact]
    public void ManagedLocalServerBaseUrl_ReturnsNullForRemote()
    {
        var config = new TransportConfig("http://example.com:8000");
        var url = config.ManagedLocalServerBaseUrl().Value;
        Assert.Null(url);
    }

    [Fact]
    public void ManagedLocalServerBaseUrl_ReturnsNullWhenAuthPresent()
    {
        var config = new TransportConfig("http://127.0.0.1:8000")
            .WithAuth(AuthConfig.HeaderApiKey("x-session-api-key", "secret"));
        var url = config.ManagedLocalServerBaseUrl().Value;
        Assert.Null(url);
    }

    [Fact]
    public void Endpoint_BuildsPathWithBaseUrl()
    {
        var config = new TransportConfig("http://127.0.0.1:8000");
        var endpoint = config.Endpoint("/api/conversations").Value;
        Assert.Contains("/api/conversations", endpoint.ToString());
    }

    [Fact]
    public void EmptyBaseUrl_ReturnsError()
    {
        var config = new TransportConfig("");
        Assert.True(config.ParsedBaseUrl().IsErr);
    }
}
