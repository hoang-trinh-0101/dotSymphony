using OpenSymphony.Domain;

namespace OpenSymphony.Domain.Tests;

public class IdentifierTests
{
    public static IEnumerable<object[]> AllTags => new[]
    {
        new object[] { typeof(ConversationId) },
        new object[] { typeof(IssueId) },
        new object[] { typeof(IssueIdentifier) },
        new object[] { typeof(TrackerStateId) },
        new object[] { typeof(WorkerId) },
    };

    [Theory]
    [MemberData(nameof(AllTags))]
    public void StringIdentifier_New_RejectsEmptyAndWhitespace(Type tagType)
    {
        dynamic empty = InvokeNew(tagType, "");
        Assert.True(empty.IsErr);
        Assert.Equal(tagType.Name, (string)empty.Error.Kind);

        dynamic whitespace = InvokeNew(tagType, "   ");
        Assert.True(whitespace.IsErr);
        Assert.Equal(tagType.Name, (string)whitespace.Error.Kind);
    }

    [Theory]
    [MemberData(nameof(AllTags))]
    public void StringIdentifier_New_AcceptsValidValue(Type tagType)
    {
        dynamic result = InvokeNew(tagType, "COE-260");
        Assert.True(result.IsOk);
        Assert.Equal("COE-260", (string)result.Value.Value);
        Assert.Equal("COE-260", result.Value.ToString());
    }

    private static dynamic InvokeNew(Type tagType, string value)
    {
        var idType = typeof(StringIdentifier<>).MakeGenericType(tagType);
        var newMethod = idType.GetMethod("New")!;
        return newMethod.Invoke(null, [value])!;
    }

    [Fact]
    public void WorkspaceKey_New_RejectsRawEmptyOnly()
    {
        var empty = WorkspaceKey.New("");
        Assert.True(empty.IsErr);
        Assert.Equal("WorkspaceKey", empty.Error.Kind);

        // ht: whitespace is accepted (raw empty check only) then sanitized to "___".
        var whitespace = WorkspaceKey.New("   ");
        Assert.True(whitespace.IsOk);
        Assert.Equal("___", whitespace.Value.Value);
    }

    [Theory]
    [InlineData("feature/42", "feature_42")]
    [InlineData("Bug: weird path", "Bug__weird_path")]
    [InlineData("ABC-123", "ABC-123")]
    [InlineData("a.b-c_d", "a.b-c_d")]
    public void WorkspaceKey_New_SanitizesNonAlphanumeric(string input, string expected)
    {
        var result = WorkspaceKey.New(input);
        Assert.True(result.IsOk);
        Assert.Equal(expected, result.Value.Value);
    }

    [Fact]
    public void WorkspaceKey_Sanitize_KeepsAllowedChars()
    {
        Assert.Equal("ABC-123", WorkspaceKey.Sanitize("ABC-123"));
        Assert.Equal("a.b-c_d", WorkspaceKey.Sanitize("a.b-c_d"));
        Assert.Equal("_", WorkspaceKey.Sanitize("/"));
        Assert.Equal("___", WorkspaceKey.Sanitize("   "));
    }
}
