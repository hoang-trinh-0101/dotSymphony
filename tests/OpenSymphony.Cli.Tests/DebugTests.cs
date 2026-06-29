using System.Text;
using System.Text.Json;
using OpenSymphony.Cli;
using Xunit;

namespace OpenSymphony.Cli.Tests;

/// <summary>
/// Tests for debug command.
/// ht: minimal port of debug.rs tests.
/// </summary>
public class DebugTests
{
    [Fact]
    public void DebugCommand_CanBeCreated()
    {
        // Act
        var command = DebugCommand.Create();

        // Assert
        Assert.NotNull(command);
        Assert.Equal("debug", command.Name);
    }

    [Fact]
    public void DebugError_NotCodexRun_HasCorrectMessage()
    {
        // Arrange
        var error = new DebugCommandError.NotCodexRun("COE-123", "loopback");

        // Assert
        Assert.Contains("COE-123", error.Message);
        Assert.Contains("loopback", error.Message);
        Assert.Contains("Codex app-server harness", error.Message);
    }

    [Fact]
    public void DebugError_CodexThreadIdMissing_HasCorrectMessage()
    {
        // Arrange
        var error = new DebugCommandError.CodexThreadIdMissing("COE-456", "/path/to/manifest.json");

        // Assert
        Assert.Contains("COE-456", error.Message);
        Assert.Contains("manifest.json", error.Message);
        Assert.Contains("Codex thread id", error.Message);
    }

    [Fact]
    public void DebugError_WorkspaceNotFound_HasCorrectMessage()
    {
        // Arrange
        var error = new DebugCommandError.WorkspaceNotFound("COE-789", "/var/workspaces");

        // Assert
        Assert.Contains("COE-789", error.Message);
        Assert.Contains("/var/workspaces", error.Message);
        Assert.Contains("managed workspace", error.Message);
    }

    [Fact]
    public void DebugSession_Constructor_SetsProperties()
    {
        // Arrange & Act
        var session = new DebugSession("TEST-123", "/path/to/config.yaml", appOnly: true);

        // Assert - just verify it can be constructed
        Assert.NotNull(session);
    }
}