using OpenSymphony.Cli.Memory;
using OpenSymphony.Memory;
using Xunit;

namespace OpenSymphony.Cli.Tests.Memory;

public class MemoryInitSummaryTests
{
    [Fact]
    public void GetChangeLists_WithCreatedFiles_ReturnsCorrectLists()
    {
        // Arrange
        var report = new MemoryInitApplyReport
        {
            ConfigPath = "/test/repo/.opensymphony/memory/config.yaml",
            Config = MemoryInitFileChange.Created,
            GitignorePath = "/test/repo/.opensymphony/memory/.gitignore",
            Gitignore = MemoryInitFileChange.Created
        };
        var targetRepo = "/test/repo";

        // Act
        var (created, updated, unchanged) = MemoryInitSummary.GetChangeLists(report, targetRepo);

        // Assert
        Assert.Equal(2, created.Count);
        Assert.Contains(".opensymphony/memory/config.yaml", created);
        Assert.Contains(".opensymphony/memory/.gitignore", created);
        Assert.Empty(updated);
        Assert.Empty(unchanged);
    }

    [Fact]
    public void GetChangeLists_WithMixedChanges_ReturnsCorrectLists()
    {
        // Arrange
        var report = new MemoryInitApplyReport
        {
            ConfigPath = "/test/repo/.opensymphony/memory/config.yaml",
            Config = MemoryInitFileChange.Updated,
            GitignorePath = "/test/repo/.opensymphony/memory/.gitignore",
            Gitignore = MemoryInitFileChange.Unchanged
        };
        var targetRepo = "/test/repo";

        // Act
        var (created, updated, unchanged) = MemoryInitSummary.GetChangeLists(report, targetRepo);

        // Assert
        Assert.Empty(created);
        Assert.Single(updated);
        Assert.Contains(".opensymphony/memory/config.yaml", updated);
        Assert.Single(unchanged);
        Assert.Contains(".opensymphony/memory/.gitignore", unchanged);
    }

    [Fact]
    public void RelativePathForSummary_WithAbsolutePath_ReturnsRelativePath()
    {
        // Arrange
        var report = new MemoryInitApplyReport
        {
            ConfigPath = "/test/repo/.opensymphony/memory/config.yaml",
            Config = MemoryInitFileChange.Created,
            GitignorePath = "/test/repo/.opensymphony/memory/.gitignore",
            Gitignore = MemoryInitFileChange.Created
        };
        var targetRepo = "/test/repo";

        // Act
        var (created, _, _) = MemoryInitSummary.GetChangeLists(report, targetRepo);

        // Assert - paths should be relative
        Assert.All(created, path => Assert.DoesNotContain("/test/repo", path));
    }
}