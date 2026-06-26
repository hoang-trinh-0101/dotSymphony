using OpenSymphony.Workspace;

namespace OpenSymphony.Workspace.Tests;

public class WorkspacePathsTests
{
    [Fact]
    public void Sanitizes_Documented_Examples()
    {
        Assert.Equal("ABC-123",
            WorkspacePaths.SanitizeWorkspaceKey("ABC-123").Value);
        Assert.Equal("feature_42",
            WorkspacePaths.SanitizeWorkspaceKey("feature/42").Value);
        Assert.Equal("Bug__weird_path",
            WorkspacePaths.SanitizeWorkspaceKey("Bug: weird path").Value);
    }

    [Fact]
    public void Rejects_Empty_And_Reserved_Workspace_Keys()
    {
        Assert.True(WorkspacePaths.SanitizeWorkspaceKey("").IsErr);
        Assert.IsType<EmptyIdentifier>(WorkspacePaths.SanitizeWorkspaceKey("").Error);

        var dot = WorkspacePaths.SanitizeWorkspaceKey(".");
        Assert.True(dot.IsErr);
        Assert.IsType<InvalidWorkspaceKey>(dot.Error);

        var dotDot = WorkspacePaths.SanitizeWorkspaceKey("..");
        Assert.True(dotDot.IsErr);
        Assert.IsType<InvalidWorkspaceKey>(dotDot.Error);
    }

    [Fact]
    public void Containment_Helper_Rejects_Parent_Escape()
    {
        var root = Path.Combine(Path.GetTempPath(), "opensymphony-workspace-root");
        var error = WorkspacePaths.ResolvePathWithinRoot(root, "../escape");

        Assert.True(error.IsErr);
        Assert.IsType<PathEscape>(error.Error);
    }

    [Fact]
    public void Containment_Helper_Allows_Descendants()
    {
        var root = Path.Combine(Path.GetTempPath(), "opensymphony-workspace-root");
        var candidate = WorkspacePaths.ResolvePathWithinRoot(root, "child/.opensymphony");

        Assert.True(candidate.IsOk);
        Assert.True(candidate.Value.EndsWith(Path.Join("child", ".opensymphony")),
            $"expected suffix {Path.Join("child", ".opensymphony")}, got {candidate.Value}");
    }
}
