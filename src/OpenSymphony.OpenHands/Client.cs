using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Web;
using OpenSymphony.Domain;
using OpenSymphony.Workflow;

namespace OpenSymphony.OpenHands;

// ht: minimal port of opensymphony-openhands client.rs — transport config, auth, HTTP client, error.

public enum TransportTargetKind
{
    Loopback,
    Remote,
}

public static class TransportTargetKindExtensions
{
    public static string AsStr(this TransportTargetKind kind) => kind switch
    {
        TransportTargetKind.Loopback => "loopback",
        TransportTargetKind.Remote => "remote",
        _ => kind.ToString(),
    };
}

public enum TransportAuthKind
{
    None,
    Header,
    QueryParam,
}

public static class TransportAuthKindExtensions
{
    public static string AsStr(this TransportAuthKind kind) => kind switch
    {
        TransportAuthKind.None => "none",
        TransportAuthKind.Header => "header",
        TransportAuthKind.QueryParam => "query_param",
        _ => kind.ToString(),
    };
}

public sealed record TransportDiagnostics(
    TransportTargetKind TargetKind,
    TransportAuthKind HttpAuthKind,
    TransportAuthKind WebsocketAuthKind,
    string? WebsocketQueryParamName,
    bool ManagedLocalServerCandidate);

public sealed record ApiKeyAuth(string Name, string Value);

public abstract record HttpAuth
{
    public sealed record None : HttpAuth;
    public sealed record QueryParam(ApiKeyAuth Key) : HttpAuth;
    public sealed record Header(ApiKeyAuth Key) : HttpAuth;
}

public abstract record WebSocketAuth
{
    public sealed record None : WebSocketAuth;
    public sealed record QueryParam(ApiKeyAuth Key) : WebSocketAuth;
    public sealed record Header(ApiKeyAuth Key) : WebSocketAuth;
}

public sealed record AuthConfig(HttpAuth Http, WebSocketAuth Websocket)
{
    public static AuthConfig None() => new(new HttpAuth.None(), new WebSocketAuth.None());

    public static AuthConfig QueryParamApiKey(string name, string value)
    {
        var key = new ApiKeyAuth(name, value);
        return new(new HttpAuth.QueryParam(key), new WebSocketAuth.QueryParam(key));
    }

    public static AuthConfig HeaderApiKey(string name, string value)
    {
        var key = new ApiKeyAuth(name, value);
        return new(new HttpAuth.Header(key), new WebSocketAuth.Header(key));
    }

    public static AuthConfig HeaderApiKeyWithWebsocketQueryFallback(
        string headerName, string websocketQueryParam, string value) =>
        new(new HttpAuth.Header(new ApiKeyAuth(headerName, value)),
            new WebSocketAuth.QueryParam(new ApiKeyAuth(websocketQueryParam, value)));

    public TransportAuthKind HttpAuthKind() => Http switch
    {
        HttpAuth.None => TransportAuthKind.None,
        HttpAuth.Header => TransportAuthKind.Header,
        HttpAuth.QueryParam => TransportAuthKind.QueryParam,
        _ => TransportAuthKind.None,
    };

    public TransportAuthKind WebsocketAuthKind() => Websocket switch
    {
        WebSocketAuth.None => TransportAuthKind.None,
        WebSocketAuth.Header => TransportAuthKind.Header,
        WebSocketAuth.QueryParam => TransportAuthKind.QueryParam,
        _ => TransportAuthKind.None,
    };

    public string? WebsocketQueryParamName() => Websocket is WebSocketAuth.QueryParam qp
        ? qp.Key.Name : null;
}

public sealed class TransportConfig
{
    public string BaseUrl { get; }
    public AuthConfig Auth { get; private set; }

    public TransportConfig(string baseUrl)
    {
        BaseUrl = baseUrl;
        Auth = AuthConfig.None();
    }

    public static Result<TransportConfig, OpenHandsError> FromWorkflow(ResolvedWorkflow workflow, IEnvironment env)
    {
        var transport = workflow.Extensions.OpenHands.Transport;
        var websocket = workflow.Extensions.OpenHands.Websocket;
        var auth = BuildWorkflowAuthConfig(transport.SessionApiKeyEnv, websocket.AuthMode, websocket.QueryParamName, env);
        if (auth.IsErr) return Result<TransportConfig, OpenHandsError>.Err(auth.Error);
        var config = new TransportConfig(transport.BaseUrl).WithAuth(auth.Value);
        return Result<TransportConfig, OpenHandsError>.Ok(config);
    }

    public TransportConfig WithAuth(AuthConfig auth) { Auth = auth; return this; }

    public Result<TransportDiagnostics, OpenHandsError> Diagnostics()
    {
        var url = ParsedBaseUrl();
        if (url.IsErr) return Result<TransportDiagnostics, OpenHandsError>.Err(url.Error);
        var uri = url.Value;
        var targetKind = IsLoopbackHost(uri.Host) ? TransportTargetKind.Loopback : TransportTargetKind.Remote;
        return Result<TransportDiagnostics, OpenHandsError>.Ok(new TransportDiagnostics(
            targetKind,
            Auth.HttpAuthKind(),
            Auth.WebsocketAuthKind(),
            Auth.WebsocketQueryParamName(),
            ManagedLocalServerBaseUrl().IsOk && ManagedLocalServerBaseUrl().Value is not null));
    }

    public Result<string?, OpenHandsError> ManagedLocalServerBaseUrl()
    {
        var url = ParsedBaseUrl();
        if (url.IsErr) return Result<string?, OpenHandsError>.Err(url.Error);
        var uri = url.Value;
        if (!IsLoopbackHost(uri.Host) || uri.Scheme != "http") return Result<string?, OpenHandsError>.Ok(null);
        if (Auth.HttpAuthKind() != TransportAuthKind.None || Auth.WebsocketAuthKind() != TransportAuthKind.None)
            return Result<string?, OpenHandsError>.Ok(null);
        return Result<string?, OpenHandsError>.Ok($"{uri.Scheme}://{uri.Host}:{uri.Port}");
    }

    public Result<Uri, OpenHandsError> Endpoint(string suffix)
    {
        var url = ParsedBaseUrl();
        if (url.IsErr) return Result<Uri, OpenHandsError>.Err(url.Error);
        var uri = url.Value;
        var basePath = uri.AbsolutePath.TrimEnd('/');
        var path = basePath + suffix;
        var builder = new UriBuilder(uri) { Path = path };
        return Result<Uri, OpenHandsError>.Ok(builder.Uri);
    }

    public Result<Uri, OpenHandsError> ParsedBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
            return Result<Uri, OpenHandsError>.Err(OpenHandsError.InvalidConfiguration("base_url cannot be empty"));
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
            return Result<Uri, OpenHandsError>.Err(OpenHandsError.InvalidConfiguration($"invalid base_url: {BaseUrl}"));
        return Result<Uri, OpenHandsError>.Ok(uri);
    }

    internal void ApplyHttpQuery(UriBuilder builder)
    {
        if (Auth.Http is HttpAuth.QueryParam key)
        {
            var query = HttpUtility.ParseQueryString(builder.Query ?? "");
            query[key.Key.Name] = key.Key.Value;
            builder.Query = query.ToString();
        }
    }

    internal void ApplyHttpHeaders(HttpRequestMessage request)
    {
        if (Auth.Http is HttpAuth.Header header)
            request.Headers.Add(header.Key.Name, header.Key.Value);
    }

    private static bool IsLoopbackHost(string? host) =>
        host is "127.0.0.1" or "localhost" or "::1" or "0.0.0.0";

    private static Result<AuthConfig, OpenHandsError> BuildWorkflowAuthConfig(
        string? sessionApiKeyEnv, string authMode, string queryParamName, IEnvironment env)
    {
        var mode = authMode.Trim().ToLowerInvariant();
        var apiKey = sessionApiKeyEnv is not null ? env.Get(sessionApiKeyEnv) : null;

        if (apiKey is null or "")
            return Result<AuthConfig, OpenHandsError>.Ok(AuthConfig.None());

        return mode switch
        {
            "auto" or "header" => Result<AuthConfig, OpenHandsError>.Ok(
                AuthConfig.HeaderApiKey("x-session-api-key", apiKey)),
            "query_param" => Result<AuthConfig, OpenHandsError>.Ok(
                AuthConfig.QueryParamApiKey(queryParamName, apiKey)),
            _ => Result<AuthConfig, OpenHandsError>.Err(
                OpenHandsError.InvalidConfiguration($"unsupported websocket auth mode `{mode}`")),
        };
    }
}

public sealed class OpenHandsError : Exception
{
    public string ErrorKind { get; }
    public string Operation { get; }
    public ushort StatusCode { get; }
    public string Body { get; }

    private OpenHandsError(string kind, string message, string operation = "", ushort statusCode = 0, string body = "")
        : base(message)
    {
        ErrorKind = kind; Operation = operation; StatusCode = statusCode; Body = body;
    }

    public static OpenHandsError InvalidConfiguration(string detail) =>
        new("InvalidConfiguration", $"invalid transport configuration: {detail}");
    public static OpenHandsError Transport(string operation, string detail) =>
        new("Transport", $"{operation} transport failed: {detail}", operation);
    public static OpenHandsError HttpStatus(string operation, ushort statusCode, string body) =>
        new("HttpStatus", $"{operation} returned HTTP {statusCode}: {body}", operation, statusCode, body);
    public static OpenHandsError Protocol(string operation, string detail) =>
        new("Protocol", $"{operation} protocol error: {detail}", operation);
    public static OpenHandsError WebSocketTransport(string operation, string detail) =>
        new("WebSocketTransport", $"{operation} websocket failed: {detail}", operation);
    public static OpenHandsError MalformedWebSocketEvent(string detail, string snippet) =>
        new("MalformedWebSocketEvent", $"websocket event decoding failed: {detail}; payload prefix: {snippet}");
    public static OpenHandsError ReadinessTimeout(TimeSpan timeout) =>
        new("ReadinessTimeout", $"websocket readiness timed out after {timeout}");
    public static OpenHandsError ProbeActivityTimeout(TimeSpan timeout) =>
        new("ProbeActivityTimeout", $"probe run activity was not observed after {timeout}");
    public static OpenHandsError ProbeRunUnhealthy(string detail) =>
        new("ProbeRunUnhealthy", $"probe run reported an unhealthy runtime: {detail}");
    public static OpenHandsError WebSocketClosed() =>
        new("WebSocketClosed", "websocket closed before readiness");
    public static OpenHandsError ReconnectExhausted(int attempts, string lastError) =>
        new("ReconnectExhausted", $"runtime stream reconnect exhausted after {attempts} attempt(s): {lastError}");
}

public sealed record RuntimeStreamConfig
{
    public TimeSpan ReadinessTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconnectInitialBackoff { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReconnectMaxBackoff { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxReconnectAttempts { get; init; } = 8;
    public bool ReplayExistingEventsOnAttach { get; init; }
}

public sealed record OpenHandsProbeResult(
    Conversation Conversation,
    EventEnvelope ReadyEvent,
    EventCache EventCache,
    ConversationStateMirror StateMirror);

public sealed class OpenHandsClient
{
    private readonly HttpClient _http;
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> _webSocketFactory;
    internal readonly TransportConfig Transport;

    public OpenHandsClient(TransportConfig transport) : this(transport, new HttpClient(), DefaultWebSocketFactory) { }

    public OpenHandsClient(TransportConfig transport, HttpClient http) : this(transport, http, DefaultWebSocketFactory) { }

    public OpenHandsClient(TransportConfig transport, HttpClient http, Func<Uri, CancellationToken, Task<WebSocket>> webSocketFactory)
    {
        Transport = transport;
        _http = http;
        _webSocketFactory = webSocketFactory;
    }

    public string BaseUrl => Transport.BaseUrl;

    internal Func<Uri, CancellationToken, Task<WebSocket>> WebSocketFactory => _webSocketFactory;

    private static async Task<WebSocket> DefaultWebSocketFactory(Uri uri, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, ct);
        return socket;
    }

    public Result<TransportDiagnostics, OpenHandsError> TransportDiagnostics() => Transport.Diagnostics();

    public async Task<Result<Unit, OpenHandsError>> OpenapiProbeAsync(CancellationToken ct = default)
    {
        var req = BuildGet("/openapi.json");
        if (req.IsErr) return Result<Unit, OpenHandsError>.Err(req.Error);
        var resp = await SendAsync(req.Value, "probe OpenAPI", ct);
        if (resp.IsErr) return Result<Unit, OpenHandsError>.Err(resp.Error);
        var body = await ReadSuccessBodyAsync(resp.Value, "probe OpenAPI", ct);
        return body.IsErr ? Result<Unit, OpenHandsError>.Err(body.Error) : Result<Unit, OpenHandsError>.Ok(Unit.Value);
    }

    public async Task<Result<Conversation, OpenHandsError>> CreateConversationAsync(
        ConversationCreateRequest request, CancellationToken ct = default)
    {
        var req = BuildJson("/api/conversations", request, HttpMethod.Post);
        if (req.IsErr) return Result<Conversation, OpenHandsError>.Err(req.Error);
        var resp = await SendAsync(req.Value, "create conversation", ct);
        if (resp.IsErr) return Result<Conversation, OpenHandsError>.Err(resp.Error);
        return await DecodeJsonAsync<Conversation>(resp.Value, "create conversation", ct);
    }

    public async Task<Result<Conversation, OpenHandsError>> GetConversationAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        var req = BuildGet($"/api/conversations/{conversationId}");
        if (req.IsErr) return Result<Conversation, OpenHandsError>.Err(req.Error);
        var resp = await SendAsync(req.Value, "fetch conversation", ct);
        if (resp.IsErr) return Result<Conversation, OpenHandsError>.Err(resp.Error);
        return await DecodeJsonAsync<Conversation>(resp.Value, "fetch conversation", ct);
    }

    public async Task<Result<Unit, OpenHandsError>> DeleteConversationAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        var req = BuildDelete($"/api/conversations/{conversationId}");
        if (req.IsErr) return Result<Unit, OpenHandsError>.Err(req.Error);
        var resp = await SendAsync(req.Value, "delete conversation", ct);
        if (resp.IsErr) return Result<Unit, OpenHandsError>.Err(resp.Error);
        var body = await ReadSuccessBodyAsync(resp.Value, "delete conversation", ct);
        return body.IsErr ? Result<Unit, OpenHandsError>.Err(body.Error) : Result<Unit, OpenHandsError>.Ok(Unit.Value);
    }

    public async Task<Result<AcceptedResponse, OpenHandsError>> SendMessageAsync(
        Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
    {
        var req = BuildJson($"/api/conversations/{conversationId}/events", request, HttpMethod.Post);
        if (req.IsErr) return Result<AcceptedResponse, OpenHandsError>.Err(req.Error);
        var resp = await SendAsync(req.Value, "send conversation event", ct);
        if (resp.IsErr) return Result<AcceptedResponse, OpenHandsError>.Err(resp.Error);
        return await DecodeJsonAsync<AcceptedResponse>(resp.Value, "send conversation event", ct);
    }

    public async Task<Result<AcceptedResponse, OpenHandsError>> RunConversationAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        var req = BuildJson($"/api/conversations/{conversationId}/run", new ConversationRunRequest(), HttpMethod.Post);
        if (req.IsErr) return Result<AcceptedResponse, OpenHandsError>.Err(req.Error);
        var resp = await SendAsync(req.Value, "trigger conversation run", ct);
        if (resp.IsErr) return Result<AcceptedResponse, OpenHandsError>.Err(resp.Error);
        return await DecodeJsonAsync<AcceptedResponse>(resp.Value, "trigger conversation run", ct);
    }

    public async Task<Result<RuntimeEventStream, OpenHandsError>> ConnectStreamAsync(
        Guid conversationId, RuntimeStreamConfig config, CancellationToken ct = default)
    {
        var conversation = await GetConversationAsync(conversationId, ct);
        if (conversation.IsErr) return Result<RuntimeEventStream, OpenHandsError>.Err(conversation.Error);
        var stream = new RuntimeEventStream(this, conversationId, config, conversation.Value, _webSocketFactory);
        try { await stream.AttachAsync(ct); }
        catch (OpenHandsError error) { return Result<RuntimeEventStream, OpenHandsError>.Err(error); }
        return Result<RuntimeEventStream, OpenHandsError>.Ok(stream);
    }

    public async Task<Result<SearchConversationEventsResponse, OpenHandsError>> SearchEventsPageAsync(
        Guid conversationId, string? pageId, CancellationToken ct = default) =>
        await SearchEventsPageWithOptionsAsync(conversationId, pageId, null, null, ct);

    private async Task<Result<SearchConversationEventsResponse, OpenHandsError>> SearchEventsPageWithOptionsAsync(
        Guid conversationId, string? pageId, int? limit, string? sortOrder, CancellationToken ct)
    {
        var endpoint = Transport.Endpoint($"/api/conversations/{conversationId}/events/search");
        if (endpoint.IsErr) return Result<SearchConversationEventsResponse, OpenHandsError>.Err(endpoint.Error);
        var builder = new UriBuilder(endpoint.Value);
        var query = HttpUtility.ParseQueryString(builder.Query ?? "");
        if (pageId is not null) query["page_id"] = pageId;
        if (limit is { } l) query["limit"] = Math.Max(1, l).ToString();
        if (sortOrder is not null) query["sort_order"] = sortOrder;
        builder.Query = query.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        Transport.ApplyHttpHeaders(request);
        var resp = await SendAsync(request, "search conversation events", ct);
        if (resp.IsErr) return Result<SearchConversationEventsResponse, OpenHandsError>.Err(resp.Error);
        return await DecodeJsonAsync<SearchConversationEventsResponse>(resp.Value, "search conversation events", ct);
    }

    public async Task<Result<EventCache, OpenHandsError>> SearchAllEventsAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        var cache = new EventCache();
        string? pageId = null;
        while (true)
        {
            var page = await SearchEventsPageAsync(conversationId, pageId, ct);
            if (page.IsErr) return Result<EventCache, OpenHandsError>.Err(page.Error);
            cache.Extend(page.Value.Events);
            if (page.Value.NextPageId is not { } next) return Result<EventCache, OpenHandsError>.Ok(cache);
            pageId = next;
        }
    }

    public async Task<Result<EventCache, OpenHandsError>> SearchRecentEventsAsync(
        Guid conversationId, int limit, CancellationToken ct = default)
    {
        var page = await SearchEventsPageWithOptionsAsync(conversationId, null, limit, "TIMESTAMP_DESC", ct);
        if (page.IsErr) return Result<EventCache, OpenHandsError>.Err(page.Error);
        var cache = new EventCache();
        cache.Extend(page.Value.Events);
        return Result<EventCache, OpenHandsError>.Ok(cache);
    }

    // ── HTTP helpers ────────────────────────────────────────────────────────

    private Result<HttpRequestMessage, OpenHandsError> BuildGet(string suffix)
    {
        var endpoint = Transport.Endpoint(suffix);
        if (endpoint.IsErr) return Result<HttpRequestMessage, OpenHandsError>.Err(endpoint.Error);
        var builder = new UriBuilder(endpoint.Value);
        Transport.ApplyHttpQuery(builder);
        var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        Transport.ApplyHttpHeaders(request);
        return Result<HttpRequestMessage, OpenHandsError>.Ok(request);
    }

    private Result<HttpRequestMessage, OpenHandsError> BuildDelete(string suffix)
    {
        var endpoint = Transport.Endpoint(suffix);
        if (endpoint.IsErr) return Result<HttpRequestMessage, OpenHandsError>.Err(endpoint.Error);
        var builder = new UriBuilder(endpoint.Value);
        Transport.ApplyHttpQuery(builder);
        var request = new HttpRequestMessage(HttpMethod.Delete, builder.Uri);
        Transport.ApplyHttpHeaders(request);
        return Result<HttpRequestMessage, OpenHandsError>.Ok(request);
    }

    private Result<HttpRequestMessage, OpenHandsError> BuildJson<T>(string suffix, T body, HttpMethod method)
    {
        var endpoint = Transport.Endpoint(suffix);
        if (endpoint.IsErr) return Result<HttpRequestMessage, OpenHandsError>.Err(endpoint.Error);
        var builder = new UriBuilder(endpoint.Value);
        Transport.ApplyHttpQuery(builder);
        var request = new HttpRequestMessage(method, builder.Uri)
        {
            Content = JsonContent.Create(body, options: OpenHandsJsonOptions.Default)
        };
        Transport.ApplyHttpHeaders(request);
        return Result<HttpRequestMessage, OpenHandsError>.Ok(request);
    }

    private async Task<Result<HttpResponseMessage, OpenHandsError>> SendAsync(
        HttpRequestMessage request, string operation, CancellationToken ct)
    {
        try
        {
            var resp = await _http.SendAsync(request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                return Result<HttpResponseMessage, OpenHandsError>.Err(
                    OpenHandsError.HttpStatus(operation, (ushort)resp.StatusCode, body));
            }
            return Result<HttpResponseMessage, OpenHandsError>.Ok(resp);
        }
        catch (HttpRequestException ex)
        {
            return Result<HttpResponseMessage, OpenHandsError>.Err(
                OpenHandsError.Transport(operation, ex.Message));
        }
    }

    private static async Task<Result<Unit, OpenHandsError>> ReadSuccessBodyAsync(
        HttpResponseMessage response, string operation, CancellationToken ct)
    {
        try
        {
            await response.Content.ReadAsStringAsync(ct);
            return Result<Unit, OpenHandsError>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, OpenHandsError>.Err(OpenHandsError.Transport(operation, ex.Message));
        }
    }

    private static async Task<Result<T, OpenHandsError>> DecodeJsonAsync<T>(
        HttpResponseMessage response, string operation, CancellationToken ct)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, OpenHandsJsonOptions.Default, ct);
            if (result is null)
                return Result<T, OpenHandsError>.Err(OpenHandsError.Protocol(operation, "response body was null"));
            return Result<T, OpenHandsError>.Ok(result);
        }
        catch (JsonException ex)
        {
            return Result<T, OpenHandsError>.Err(OpenHandsError.Protocol(operation, ex.Message));
        }
        catch (Exception ex)
        {
            return Result<T, OpenHandsError>.Err(OpenHandsError.Transport(operation, ex.Message));
        }
    }
}
