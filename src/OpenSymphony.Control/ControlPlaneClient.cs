using System.Net;
using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Control;

public class ControlPlaneClientException : Exception
{
    public ControlPlaneClientException(string message) : base(message) { }
    public ControlPlaneClientException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ControlPlaneClientTimeoutException : ControlPlaneClientException
{
    public TimeSpan Timeout { get; }
    public ControlPlaneClientTimeoutException(TimeSpan timeout)
        : base($"control-plane stream did not attach within {timeout}")
        => Timeout = timeout;
}

public sealed class ControlPlaneClient
{
    private readonly Uri _baseUrl;
    private readonly HttpClient _http;
    private readonly HttpClient _streamHttp;
    private readonly TimeSpan _streamAttachTimeout;

    public static readonly TimeSpan DefaultSnapshotTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultStreamAttachTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultStreamReadTimeout = TimeSpan.FromSeconds(35);

    public ControlPlaneClient(Uri baseUrl)
        : this(baseUrl, DefaultSnapshotTimeout, DefaultStreamAttachTimeout, DefaultStreamReadTimeout) { }

    public ControlPlaneClient(
        Uri baseUrl,
        TimeSpan snapshotTimeout,
        TimeSpan streamAttachTimeout,
        TimeSpan streamReadTimeout)
    {
        _baseUrl = baseUrl;
        _http = new HttpClient { Timeout = snapshotTimeout };
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _streamAttachTimeout = streamAttachTimeout;
    }

    public async Task<SnapshotEnvelope> FetchSnapshotAsync(CancellationToken ct = default)
    {
        var url = JoinPath("api/v1/snapshot");
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ControlPlaneJson.Deserialize<SnapshotEnvelope>(json)
            ?? throw new ControlPlaneClientException("failed to decode snapshot");
    }

    public async Task<ControlPlaneEventStream> StreamUpdatesAsync()
    {
        var url = JoinPath("api/v1/control/events");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _streamHttp.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return new ControlPlaneEventStream(response, _streamAttachTimeout);
    }

    // ht: Rust normalized_base_url ensures trailing slash so .join(path) works.
    //   In C#, Uri constructor already handles path joining correctly.
    public Uri JoinPath(string path)
    {
        var baseStr = _baseUrl.ToString();
        if (!baseStr.EndsWith('/'))
            baseStr += "/";
        return new Uri(new Uri(baseStr), path);
    }
}

public sealed class ControlPlaneEventStream : IDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;
    private readonly TimeSpan _attachTimeout;
    private bool _awaitingFirstSnapshot;
    private bool _disposed;

    public ControlPlaneEventStream(HttpResponseMessage response, TimeSpan attachTimeout)
    {
        _response = response;
        _reader = new StreamReader(response.Content.ReadAsStream());
        _attachTimeout = attachTimeout;
        _awaitingFirstSnapshot = true;
    }

    public async Task<SnapshotEnvelope?> NextAsync(CancellationToken ct = default)
    {
        while (true)
        {
            SnapshotEnvelope? result;
            if (_awaitingFirstSnapshot)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_attachTimeout);
                try
                {
                    result = await ReadNextSnapshotAsync(cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new ControlPlaneClientTimeoutException(_attachTimeout);
                }
                _awaitingFirstSnapshot = false;
            }
            else
            {
                result = await ReadNextSnapshotAsync(ct);
            }

            if (result is not null)
                return result;
            // null = keepalive or comment, continue reading
        }
    }

    private async Task<SnapshotEnvelope?> ReadNextSnapshotAsync(CancellationToken ct)
    {
        string? eventType = null;
        string? data = null;

        while (true)
        {
            var line = await _reader.ReadLineAsync(ct);
            if (line is null)
                return null; // stream ended

            if (line.Length == 0)
            {
                // event delimiter — emit if we have data
                if (data is not null)
                {
                    return ControlPlaneJson.Deserialize<SnapshotEnvelope>(data)
                        ?? throw new ControlPlaneClientException("failed to decode snapshot payload");
                }
                eventType = null;
                data = null;
                continue;
            }

            if (line[0] == ':')
                continue; // comment / keepalive

            if (line.StartsWith("event: "))
                eventType = line["event: ".Length..];
            else if (line.StartsWith("data: "))
                data = line["data: ".Length..];
            else if (line.StartsWith("id: "))
            { /* event ID — not needed for decode */ }
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
        _response.Dispose();
    }
}
