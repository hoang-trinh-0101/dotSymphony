using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;

namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/error.rs.

public sealed class GraphqlError
{
    public string Message { get; set; } = "";
    public string? Code { get; set; }
}

public sealed class ResponseMetadata
{
    public string? ContentType { get; set; }
    public string? ContentLength { get; set; }
    public string? ContentEncoding { get; set; }

    public override string ToString()
    {
        var contentType = ContentType ?? "<missing>";
        var contentLength = ContentLength ?? "<missing>";
        var contentEncoding = ContentEncoding ?? "<missing>";
        return $"content-type={contentType}, content-length={contentLength}, content-encoding={contentEncoding}";
    }
}

public abstract class LinearError : Exception
{
    protected LinearError(string message) : base(message) { }

    public static LinearError InvalidConfiguration(string message)
        => new InvalidConfigurationError(message);

    public static LinearError Request(string message, bool isTimeout = false)
        => new RequestError(message, isTimeout);

    public static LinearError ResponseBody(string operation, HttpStatusCode status, ResponseMetadata metadata, TimeSpan? retryAfter, string source)
        => new ResponseBodyError(operation, status, metadata, retryAfter, source);

    public static LinearError HttpStatus(HttpStatusCode status, string body, TimeSpan? retryAfter)
        => new HttpStatusError(status, body, retryAfter);

    public static LinearError Graphql(List<GraphqlError> errors, string summary, TimeSpan? retryAfter)
        => new GraphqlErrorVariant(errors, summary, retryAfter);

    public static LinearError MissingIssueIds(List<string> issueIds)
        => new MissingIssueIdsError(issueIds);

    public static LinearError InvalidResponse(string message)
        => new InvalidResponseError(message);

    public static LinearError FromGraphqlErrors(List<GraphqlError> errors)
        => FromGraphqlErrorsWithRetryAfter(errors, null);

    public static LinearError FromGraphqlErrorsWithRetryAfter(List<GraphqlError> errors, TimeSpan? retryAfter)
    {
        var summary = string.Join("; ", errors.Select(e =>
            e.Code is string code ? $"{code}: {e.Message}" : e.Message));
        return Graphql(errors, summary, retryAfter);
    }

    public abstract TrackerErrorCategory Category();

    public bool IsRateLimited() => Category() == TrackerErrorCategory.RateLimited;

    public virtual TimeSpan? RetryAfter() => null;

    // ht: Error variants as nested classes to mirror Rust enum.
    public sealed class InvalidConfigurationError : LinearError
    {
        public InvalidConfigurationError(string message) : base($"invalid Linear client configuration: {message}") { }
        public override TrackerErrorCategory Category() => TrackerErrorCategory.InvalidResponse;
    }

    public sealed class RequestError : LinearError
    {
        public bool IsTimeout { get; }
        public RequestError(string message, bool isTimeout = false) : base($"Linear request failed: {message}")
            => IsTimeout = isTimeout;
        public override TrackerErrorCategory Category() => IsTimeout ? TrackerErrorCategory.Timeout : TrackerErrorCategory.Transport;
    }

    public sealed class ResponseBodyError : LinearError
    {
        public string Operation { get; }
        public HttpStatusCode Status { get; }
        public ResponseMetadata Metadata { get; }
        public bool IsTimeout { get; }
        private readonly TimeSpan? _retryAfter;
        public ResponseBodyError(string operation, HttpStatusCode status, ResponseMetadata metadata, TimeSpan? retryAfter, string source)
            : base($"Linear response body read failed for {operation} after HTTP {(int)status} ({metadata}): {source}")
        {
            Operation = operation;
            Status = status;
            Metadata = metadata;
            _retryAfter = retryAfter;
        }
        public override TrackerErrorCategory Category() => IsTimeout ? TrackerErrorCategory.Timeout : TrackerErrorCategory.Transport;
        public override TimeSpan? RetryAfter() => _retryAfter;
    }

    public sealed class HttpStatusError : LinearError
    {
        public HttpStatusCode Status { get; }
        public string Body { get; }
        private readonly TimeSpan? _retryAfter;
        public HttpStatusError(HttpStatusCode status, string body, TimeSpan? retryAfter)
            : base($"Linear API returned HTTP {(int)status}: {body}")
        {
            Status = status;
            Body = body;
            _retryAfter = retryAfter;
        }
        public override TrackerErrorCategory Category() => HttpStatusCategory(Status);
        public override TimeSpan? RetryAfter() => _retryAfter;
    }

    public sealed class GraphqlErrorVariant : LinearError
    {
        public List<GraphqlError> Errors { get; }
        private readonly TimeSpan? _retryAfter;
        public GraphqlErrorVariant(List<GraphqlError> errors, string summary, TimeSpan? retryAfter)
            : base($"Linear GraphQL returned errors: {summary}")
        {
            Errors = errors;
            _retryAfter = retryAfter;
        }
        public override TrackerErrorCategory Category() => GraphqlCategory(Errors);
        public override TimeSpan? RetryAfter() => _retryAfter;
    }

    public sealed class MissingIssueIdsError : LinearError
    {
        public List<string> IssueIds { get; }
        public MissingIssueIdsError(List<string> issueIds)
            : base($"Linear omitted requested issue IDs from state refresh: [{string.Join(", ", issueIds)}]")
            => IssueIds = issueIds;
        public override TrackerErrorCategory Category() => TrackerErrorCategory.NotFound;
    }

    public sealed class InvalidResponseError : LinearError
    {
        public InvalidResponseError(string message) : base($"Linear API returned an invalid response: {message}") { }
        public override TrackerErrorCategory Category() => TrackerErrorCategory.InvalidResponse;
    }

    // ht: Static helpers mirroring Rust free functions.
    public static TrackerErrorCategory HttpStatusCategory(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.Unauthorized => TrackerErrorCategory.Auth,
            HttpStatusCode.Forbidden => TrackerErrorCategory.PermissionDenied,
            HttpStatusCode.NotFound => TrackerErrorCategory.NotFound,
            HttpStatusCode.TooManyRequests => TrackerErrorCategory.RateLimited,
            _ when (int)status >= 500 => TrackerErrorCategory.Transport,
            _ => TrackerErrorCategory.InvalidResponse,
        };
    }

    public static TrackerErrorCategory GraphqlCategory(List<GraphqlError> errors)
    {
        foreach (var error in errors)
        {
            if (error.Code is string code)
            {
                var codeLower = code.ToLowerInvariant();
                if (codeLower.Contains("auth")) return TrackerErrorCategory.Auth;
                if (codeLower.Contains("forbidden") || codeLower.Contains("permission")) return TrackerErrorCategory.PermissionDenied;
                if (codeLower.Contains("rate") || codeLower.Contains("throttle")) return TrackerErrorCategory.RateLimited;
                if (codeLower.Contains("not_found") || codeLower.Contains("notfound")) return TrackerErrorCategory.NotFound;
                if (codeLower.Contains("invalid_state")) return TrackerErrorCategory.InvalidStateTransition;
            }

            var msg = error.Message.ToLowerInvariant();
            if (msg.Contains("permission") || msg.Contains("forbidden")) return TrackerErrorCategory.PermissionDenied;
            if (msg.Contains("rate limit") || msg.Contains("too many requests")) return TrackerErrorCategory.RateLimited;
            if (msg.Contains("authentication") || msg.Contains("unauthorized")) return TrackerErrorCategory.Auth;
            if (msg.Contains("not found")) return TrackerErrorCategory.NotFound;
            if (msg.Contains("invalid state transition")) return TrackerErrorCategory.InvalidStateTransition;
        }

        return TrackerErrorCategory.InvalidResponse;
    }
}
