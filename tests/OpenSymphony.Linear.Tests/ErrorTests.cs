using System.Net;
using OpenSymphony.Domain;
using OpenSymphony.Linear;

namespace OpenSymphony.Linear.Tests;

public class ErrorTests
{
    [Fact]
    public void HttpStatusesMapToTrackerCategories()
    {
        var auth = LinearError.HttpStatus(HttpStatusCode.Unauthorized, "unauthorized", null);
        var permissionDenied = LinearError.HttpStatus(HttpStatusCode.Forbidden, "forbidden", null);
        var rateLimited = LinearError.HttpStatus(HttpStatusCode.TooManyRequests, "slow down", TimeSpan.FromSeconds(1));

        Assert.Equal(TrackerErrorCategory.Auth, auth.Category());
        Assert.Equal(TrackerErrorCategory.PermissionDenied, permissionDenied.Category());
        Assert.Equal(TrackerErrorCategory.RateLimited, rateLimited.Category());
    }

    [Fact]
    public void GraphqlErrorsMapToTrackerCategories()
    {
        var forbidden = LinearError.FromGraphqlErrors([new GraphqlError { Message = "viewer does not have permission", Code = "FORBIDDEN" }]);
        var notFound = LinearError.FromGraphqlErrors([new GraphqlError { Message = "issue not found", Code = "NOT_FOUND" }]);
        var rateLimited = LinearError.FromGraphqlErrorsWithRetryAfter(
            [new GraphqlError { Message = "rate limit exceeded", Code = "RATELIMITED" }],
            TimeSpan.FromSeconds(2));

        Assert.Equal(TrackerErrorCategory.PermissionDenied, forbidden.Category());
        Assert.Equal(TrackerErrorCategory.NotFound, notFound.Category());
        Assert.Equal(TrackerErrorCategory.RateLimited, rateLimited.Category());
        Assert.Equal(TimeSpan.FromSeconds(2), rateLimited.RetryAfter());
    }
}
