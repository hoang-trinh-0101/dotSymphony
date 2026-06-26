using System.Net.Http.Headers;
using OpenSymphony.Linear;

namespace OpenSymphony.Linear.Tests;

public class ClientHeaderTests
{
    private static HttpResponseHeaders CreateHeaders(Action<HttpHeaders> addHeaders)
    {
        var response = new HttpResponseMessage();
        addHeaders(response.Headers);
        return response.Headers;
    }

    [Fact]
    public void RateLimitResetHeadersUseLatestResetWindow()
    {
        var headers = CreateHeaders(h =>
        {
            h.Add("x-ratelimit-requests-reset", "1100");
            h.Add("x-ratelimit-endpoint-requests-reset", "1250");
            h.Add("x-ratelimit-complexity-reset", "1200");
        });

        var now = DateTimeOffset.FromUnixTimeMilliseconds(1000);
        var delay = LinearClient.ParseRateLimitReset(headers, null, now);

        Assert.Equal(TimeSpan.FromMilliseconds(250), delay);
    }

    [Fact]
    public void RetryDelayPrefersResetHeadersOverRetryAfter()
    {
        var headers = CreateHeaders(h =>
        {
            h.Add("Retry-After", "30");
            h.Add("x-ratelimit-requests-reset", "0");
        });

        var delay = LinearClient.ParseRetryDelay(headers, null);

        Assert.Equal(TimeSpan.Zero, delay);
    }
}
