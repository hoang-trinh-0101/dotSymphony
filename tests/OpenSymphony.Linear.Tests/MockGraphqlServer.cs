using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenSymphony.Linear.Tests;

// ht: Mock HTTP handler that queues responses and records requests.
//   Replaces the Rust axum-based MockGraphqlServer.

public sealed class QueuedResponse
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Body { get; set; } = "";
    public List<(string Name, string Value)> Headers { get; } = new();

    public static QueuedResponse Json(string body)
    {
        var qr = new QueuedResponse
        {
            Status = HttpStatusCode.OK,
            Body = body,
        };
        qr.Headers.Add(("Content-Type", "application/json"));
        return qr;
    }

    public static QueuedResponse New(HttpStatusCode status, string body)
        => new() { Status = status, Body = body };

    public QueuedResponse WithHeader(string name, string value)
    {
        Headers.Add((name, value));
        return this;
    }
}

public sealed class CapturedRequest
{
    public string? Authorization { get; set; }
    public JsonDocument Body { get; set; } = JsonDocument.Parse("{}");
}

public sealed class MockGraphqlHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<QueuedResponse> _responses = new();
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    public void Enqueue(QueuedResponse response) => _responses.Enqueue(response);

    public void EnqueueRange(IEnumerable<QueuedResponse> responses)
    {
        foreach (var r in responses) _responses.Enqueue(r);
    }

    public List<CapturedRequest> RecordedRequests => _requests.ToList();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Capture request
        string? auth = null;
        if (request.Headers.TryGetValues("Authorization", out var authValues))
            auth = authValues.FirstOrDefault();

        JsonDocument? bodyDoc = null;
        if (request.Content is not null)
        {
            var bodyStr = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            bodyDoc = JsonDocument.Parse(bodyStr);
        }

        _requests.Enqueue(new CapturedRequest
        {
            Authorization = auth,
            Body = bodyDoc ?? JsonDocument.Parse("{}"),
        });

        // Pop response
        if (!_responses.TryDequeue(out var response))
            throw new InvalidOperationException("test did not queue enough responses");

        var httpResponse = new HttpResponseMessage(response.Status)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
        };

        foreach (var (name, value) in response.Headers)
        {
            // ht: content-type goes on Content headers, others on Response headers.
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                httpResponse.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                httpResponse.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return Task.FromResult(httpResponse);
    }
}

public sealed class MockGraphqlServer : IDisposable
{
    public MockGraphqlHandler Handler { get; } = new();
    public string BaseUrl { get; }

    public MockGraphqlServer()
    {
        BaseUrl = "http://localhost/graphql";
    }

    public static MockGraphqlServer Start(IEnumerable<QueuedResponse> responses)
    {
        var server = new MockGraphqlServer();
        server.Handler.EnqueueRange(responses);
        return server;
    }

    public List<CapturedRequest> RecordedRequests => Handler.RecordedRequests;

    public void Dispose()
    {
    }
}
