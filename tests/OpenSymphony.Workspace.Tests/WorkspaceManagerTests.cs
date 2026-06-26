using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Workspace.Tests;

public sealed class WorkspaceManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "os-test-" + Guid.NewGuid().ToString("N")[..8]);

    private string WorkspaceRoot => Path.Combine(_tempDir, "workspaces");

    private WorkspaceManager NewManager(HookConfig? hooks = null, CleanupConfig? cleanup = null)
        => new(new WorkspaceManagerConfig(WorkspaceRoot, hooks ?? HookConfig.Default(), cleanup ?? CleanupConfig.Default()));

    private static IssueDescriptor SampleIssue(string identifier)
        => new($"id-{identifier}", identifier, $"Issue {identifier}", "In Progress", null);

    private static string CurrentDirCommand(string outputPath)
        => OperatingSystem.IsWindows()
            ? $"cd > {outputPath}"
            : $"pwd > {outputPath}";

    private static string TimeoutCommand()
        => OperatingSystem.IsWindows()
            ? "ping 127.0.0.1 -n 2 > NUL"
            : "sleep 1";

    private static string FailingCommand()
        => OperatingSystem.IsWindows()
            ? "echo boom 1>&2 && exit /b 7"
            : "echo boom 1>&2; exit 7";

    private static string BestEffortFailureCommand()
        => OperatingSystem.IsWindows()
            ? "echo after-run 1>&2 && exit /b 9"
            : "echo after-run 1>&2; exit 9";

    private static string AfterCreateRequiresEmptyWorkspaceCommand()
        => OperatingSystem.IsWindows()
            ? "if exist .opensymphony\\NUL (echo metadata-present 1>&2 && exit /b 17) else (echo after_create> after_create.txt)"
            : "if [ -e .opensymphony ]; then echo metadata-present 1>&2; exit 17; fi; echo after_create > after_create.txt";

    private static string AfterCreateRetryCommand()
        => OperatingSystem.IsWindows()
            ? "if not exist after_create_attempt.txt (echo first> after_create_attempt.txt && echo retry 1>&2 && exit /b 23) else (echo success> after_create_success.txt)"
            : "if [ ! -f after_create_attempt.txt ]; then echo first > after_create_attempt.txt; echo retry 1>&2; exit 23; fi; echo success > after_create_success.txt";

    private static string ForeignIssueManifestJson(string workspacePath, string key)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["issue_id"] = "foreign-id",
            ["identifier"] = "foreign-issue",
            ["title"] = "Foreign issue",
            ["current_state"] = "In Progress",
            ["sanitized_workspace_key"] = key,
            ["workspace_path"] = workspacePath,
            ["created_at"] = "2026-03-21T00:00:00Z",
            ["updated_at"] = "2026-03-21T00:00:00Z",
        }, WorkspaceJsonOptions.Default);

    private static T Must<T, E>(Result<T, E> r) where E : notnull
        => r.IsOk ? r.Value : throw new Exception($"Expected Ok, got Err: {r.Error}");

    private static E MustErr<T, E>(Result<T, E> r) where E : notnull
        => r.IsErr ? r.Error : throw new Exception("Expected Err, got Ok");

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task EnsureCreatesReusesWorkspaceAndRunsAfterCreateOnce()
    {
        var manager = NewManager(new HookConfig(
            HookDefinition.Shell(AfterCreateRequiresEmptyWorkspaceCommand()), null, null, null,
            TimeSpan.FromSeconds(60)));
        var issue = SampleIssue("COE-263");

        var first = Must(await manager.Ensure(issue));
        var second = Must(await manager.Ensure(issue));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Handle.WorkspacePath, second.Handle.WorkspacePath);

        var manifestText = File.ReadAllText(first.Handle.IssueManifestPath());
        Assert.Contains("\"sanitized_workspace_key\": \"COE-263\"", manifestText);

        var afterCreateText = File.ReadAllText(Path.Join(first.Handle.WorkspacePath, "after_create.txt")).Trim();
        Assert.Equal("after_create", afterCreateText);

        Assert.False(File.Exists(Path.Join(second.Handle.WorkspacePath, "after_create_attempt.txt")));
    }

    [Fact]
    public async Task EnsureRetriesAfterCreateAfterFailedFirstBootstrap()
    {
        var manager = NewManager(new HookConfig(
            HookDefinition.Shell(AfterCreateRetryCommand()), null, null, null,
            TimeSpan.FromSeconds(60)));
        var issue = SampleIssue("COE-263-retry");

        var firstError = MustErr(await manager.Ensure(issue));
        Assert.IsType<HookFailed>(firstError);
        Assert.Equal(HookKind.AfterCreate, ((HookFailed)firstError).Hook);

        var ensured = Must(await manager.Ensure(issue));
        Assert.True(ensured.Created);
        Assert.Equal("success", File.ReadAllText(Path.Join(ensured.Handle.WorkspacePath, "after_create_success.txt")).Trim());
        Assert.True(File.Exists(ensured.Handle.IssueManifestPath()));
    }

    [Fact]
    public async Task EnsureRetriesAfterCreateWhenForeignIssueManifestPreexists()
    {
        var manager = NewManager(new HookConfig(
            HookDefinition.Shell(AfterCreateRetryCommand()), null, null, null,
            TimeSpan.FromSeconds(60)));
        var issue = SampleIssue("COE-263-foreign-manifest");

        MustErr(await manager.Ensure(issue));

        var workspacePath = Must(manager.WorkspacePathFor(issue.Identifier));
        var metadataDir = Path.Join(workspacePath, ".opensymphony");
        Directory.CreateDirectory(metadataDir);
        File.WriteAllText(Path.Join(metadataDir, "issue.json"),
            ForeignIssueManifestJson(Path.Join(_tempDir, "elsewhere"), "COE-263-foreign-manifest"));

        var ensured = Must(await manager.Ensure(issue));
        Assert.True(ensured.Created);
        Assert.Equal("success", File.ReadAllText(Path.Join(ensured.Handle.WorkspacePath, "after_create_success.txt")).Trim());

        var loaded = Must(await manager.LoadIssueManifest(ensured.Handle));
        Assert.NotNull(loaded);
        Assert.Equal(issue.IssueId, loaded!.IssueId);
    }

    [Fact]
    public async Task FindWorkspaceByIssueReferenceReturnsIdentifierMatch()
    {
        var manager = NewManager();
        var ensured = Must(await manager.Ensure(SampleIssue("COE-287")));

        var found = Must(await manager.FindWorkspaceByIssueReference("COE-287"));
        Assert.NotNull(found);
        Assert.Equal(ensured.Handle.IssueId, found!.IssueId);
        Assert.Equal(ensured.Handle.Identifier, found.Identifier);
        Assert.Equal(ensured.Handle.WorkspacePath, found.WorkspacePath);
    }

    [Fact]
    public async Task FindWorkspaceByIssueReferenceScansIssueIds()
    {
        var manager = NewManager();
        var issue = SampleIssue("COE-288");
        var ensured = Must(await manager.Ensure(issue));

        var found = Must(await manager.FindWorkspaceByIssueReference(issue.IssueId));
        Assert.NotNull(found);
        Assert.Equal(ensured.Handle.IssueId, found!.IssueId);
        Assert.Equal(ensured.Handle.Identifier, found.Identifier);
    }

    [Fact]
    public async Task EnsureRetriesAfterCreateWhenCopiedMalformedIssueManifestPreexists()
    {
        var manager = NewManager(new HookConfig(
            HookDefinition.Shell(AfterCreateRetryCommand()), null, null, null,
            TimeSpan.FromSeconds(60)));
        var issue = SampleIssue("COE-263-malformed");

        MustErr(await manager.Ensure(issue));

        var workspacePath = Must(manager.WorkspacePathFor(issue.Identifier));
        var metadataDir = Path.Join(workspacePath, ".opensymphony");
        Directory.CreateDirectory(metadataDir);
        File.WriteAllText(Path.Join(metadataDir, "issue.json"), "{not valid json}");

        var ensured = Must(await manager.Ensure(issue));
        Assert.True(ensured.Created);
        Assert.Equal("success", File.ReadAllText(Path.Join(ensured.Handle.WorkspacePath, "after_create_success.txt")).Trim());
    }

    [Fact(Skip = "Unix-only: symlink bootstrap failure (#[cfg(unix)] in Rust)")]
    public async Task EnsureDoesNotRerunAfterCreateAfterPostHookBootstrapFailure()
    {
        // Unix-only: requires symlink creation for .opensymphony -> outside dir
    }

    [Fact]
    public async Task EnsureRejectsWorkspaceReuseForCollidingSanitizedKey()
    {
        var manager = NewManager();
        var firstIssue = SampleIssue("feature/42");
        var secondIssue = SampleIssue("feature:42");

        Must(await manager.Ensure(firstIssue));

        var error = MustErr(await manager.Ensure(secondIssue));
        var conflict = Assert.IsType<WorkspaceOwnershipConflict>(error);
        Assert.Equal(firstIssue.IssueId, conflict.Details.ExistingIssueId);
        Assert.Equal(secondIssue.IssueId, conflict.Details.RequestedIssueId);
    }

    [Fact]
    public async Task StartRunExecutesBeforeRunInWorkspaceAndPersistsManifest()
    {
        var manager = NewManager(new HookConfig(
            null,
            HookDefinition.Shell(CurrentDirCommand(".opensymphony/logs/before_run_cwd.txt")),
            null, null, TimeSpan.FromSeconds(60)));
        var issue = SampleIssue("feature/42");
        var ensured = Must(await manager.Ensure(issue));

        var runManifest = Must(await manager.StartRun(ensured.Handle, RunDescriptor.New("run-1", 1)));

        Assert.Equal(RunStatus.Prepared, runManifest.Status);
        Assert.Single(runManifest.Hooks);
        Assert.Equal(HookKind.BeforeRun, runManifest.Hooks[0].Kind);
        Assert.Equal(HookExecutionStatus.Succeeded, runManifest.Hooks[0].Status);

        var cwd = File.ReadAllText(Path.Join(ensured.Handle.LogsDir(), "before_run_cwd.txt")).Trim();
        Assert.Equal(ensured.Handle.WorkspacePath, cwd);

        var persisted = Must(await manager.LoadRunManifest(ensured.Handle));
        Assert.NotNull(persisted);
        Assert.Equal(RunStatus.Prepared, persisted!.Status);
        Assert.Equal("feature_42", persisted.SanitizedWorkspaceKey);
    }

    [Fact]
    public async Task BeforeRunTimeoutIsRecordedAndReturned()
    {
        var manager = NewManager(new HookConfig(
            null,
            HookDefinition.Shell(TimeoutCommand()),
            null, null, TimeSpan.FromMilliseconds(50)));
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-timeout")));

        var error = MustErr(await manager.StartRun(ensured.Handle, RunDescriptor.New("run-timeout", 1)));
        Assert.IsType<HookTimedOut>(error);
        Assert.Equal(HookKind.BeforeRun, ((HookTimedOut)error).Hook);

        var persisted = Must(await manager.LoadRunManifest(ensured.Handle));
        Assert.NotNull(persisted);
        Assert.Equal(RunStatus.PreparationFailed, persisted!.Status);
        Assert.Single(persisted.Hooks);
        Assert.Equal(HookExecutionStatus.TimedOut, persisted.Hooks[0].Status);
    }

    [Fact(Skip = "Unix-only: background child kill (#[cfg(unix)] in Rust)")]
    public async Task BeforeRunTimeoutKillsSpawnedDescendants()
    {
        // Unix-only: requires shell backgrounding and process group kill
    }

    [Fact]
    public async Task BeforeRunFailureCapturesStderr()
    {
        var manager = NewManager(new HookConfig(
            null,
            HookDefinition.Shell(FailingCommand()),
            null, null, TimeSpan.FromSeconds(60)));
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-fail")));

        var error = MustErr(await manager.StartRun(ensured.Handle, RunDescriptor.New("run-fail", 1)));
        var hookFailed = Assert.IsType<HookFailed>(error);
        Assert.Equal(HookKind.BeforeRun, hookFailed.Hook);
        Assert.Equal(7, hookFailed.ExitCode);
        Assert.Contains("boom", hookFailed.Stderr);

        var persisted = Must(await manager.LoadRunManifest(ensured.Handle));
        Assert.NotNull(persisted);
        Assert.Equal(RunStatus.PreparationFailed, persisted!.Status);
        Assert.Single(persisted.Hooks);
        Assert.Equal(HookExecutionStatus.Failed, persisted.Hooks[0].Status);
        Assert.Equal(7, persisted.Hooks[0].ExitCode);
        Assert.Contains("boom", persisted.Hooks[0].Stderr);
    }

    [Fact]
    public async Task AfterRunFailureIsBestEffortAndPersisted()
    {
        var manager = NewManager(new HookConfig(
            null, null,
            HookDefinition.Shell(BestEffortFailureCommand()),
            null, TimeSpan.FromSeconds(60)));
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-after-run")));
        var runManifest = Must(await manager.StartRun(ensured.Handle, RunDescriptor.New("run-after", 1)));

        var finishResult = await manager.FinishRun(ensured.Handle, runManifest, RunStatus.Succeeded);
        Assert.True(finishResult.IsOk);

        var persisted = Must(await manager.LoadRunManifest(ensured.Handle));
        Assert.NotNull(persisted);
        Assert.Equal(RunStatus.Succeeded, persisted!.Status);
        Assert.Contains(persisted.Hooks, h => h.Kind == HookKind.AfterRun && h.Status == HookExecutionStatus.Failed);
    }

    [Fact]
    public async Task ConversationManifestArtifactsRoundTripInsideWorkspace()
    {
        var manager = NewManager();
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-conv")));

        var conv = ConversationManifest.New(ensured.Handle, "conv-1", "http://localhost:3000",
            ensured.Handle.GeneratedDir(), "1.0");
        Must(await manager.WriteConversationManifest(ensured.Handle, conv));

        var loaded = Must(await manager.LoadConversationManifest(ensured.Handle));
        Assert.NotNull(loaded);
        Assert.Equal(conv.ConversationId, loaded!.ConversationId);
        Assert.Equal(conv.ServerBaseUrl, loaded.ServerBaseUrl);
    }

    [Fact]
    public async Task ConversationManifestAndGeneratedContextArtifactsArePersisted()
    {
        var manager = NewManager();
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-context")));

        var conv = ConversationManifest.New(ensured.Handle, "conv-ctx", "http://localhost:3000",
            ensured.Handle.GeneratedDir(), "1.0");
        Must(await manager.WriteConversationManifest(ensured.Handle, conv));

        var issueContext = new IssueContextArtifact(
            ensured.Handle.IssueId, ensured.Handle.Identifier, "Test issue", "In Progress",
            "WORKFLOW.md", null, null, null, new(), new());
        Must(await manager.WriteIssueContext(ensured.Handle, issueContext));

        var sessionContext = SessionContextArtifact.New(ensured.Handle);
        Must(await manager.WriteSessionContext(ensured.Handle, sessionContext));

        Assert.True(File.Exists(ensured.Handle.ConversationManifestPath()));
        Assert.True(File.Exists(ensured.Handle.IssueContextPath()));
        Assert.True(File.Exists(ensured.Handle.SessionContextPath()));
    }

    [Fact]
    public async Task PromptCaptureWritesLatestAndPerRunArtifacts()
    {
        var manager = NewManager();
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-prompt")));
        var run = RunDescriptor.New("run-prompt", 1);
        var descriptor = PromptCaptureDescriptor.New(PromptKind.Full, 0);
        var prompt = "You are a helpful assistant.";

        var manifest = Must(await manager.WritePromptCapture(ensured.Handle, run, descriptor, prompt));

        Assert.True(File.Exists(manifest.ArchivedPromptPath));
        Assert.True(File.Exists(manifest.StablePromptPath));
        Assert.Equal(prompt, File.ReadAllText(manifest.StablePromptPath).TrimEnd());
    }

    [Fact(Skip = "Unix-only: symlink in generated dir (#[cfg(unix)] in Rust)")]
    public async Task GeneratedIssueContextRejectsSymlinkedOutputPaths()
    {
        // Unix-only: requires symlink creation
    }

    [Fact]
    public async Task CleanupRetainsNonTerminalWorkspaces()
    {
        var manager = NewManager();
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-retain")));

        var outcome = Must(await manager.Cleanup(ensured.Handle, IssueLifecycleState.Inactive));
        Assert.Equal(CleanupDecision.Retain, outcome.Decision);
        Assert.True(Directory.Exists(ensured.Handle.WorkspacePath));
    }

    [Fact]
    public async Task TerminalCleanupCanRunBeforeRemoveWithoutDeletingWorkspace()
    {
        var manager = NewManager(new HookConfig(
            null, null, null,
            HookDefinition.Shell(CurrentDirCommand("before_remove.txt")),
            TimeSpan.FromSeconds(60)),
            new CleanupConfig(RemoveTerminalWorkspaces: false));
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-cleanup-keep")));

        var outcome = Must(await manager.Cleanup(ensured.Handle, IssueLifecycleState.Terminal));
        Assert.Equal(CleanupDecision.Retain, outcome.Decision);
        Assert.True(Directory.Exists(ensured.Handle.WorkspacePath));
        Assert.True(File.Exists(Path.Join(ensured.Handle.WorkspacePath, "before_remove.txt")));
    }

    [Fact]
    public async Task TerminalCleanupCanDeleteWorkspace()
    {
        var manager = NewManager(null, new CleanupConfig(RemoveTerminalWorkspaces: true));
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-terminal-remove")));

        var outcome = Must(await manager.Cleanup(ensured.Handle, IssueLifecycleState.Terminal));
        Assert.Equal(CleanupDecision.Remove, outcome.Decision);
        Assert.False(Directory.Exists(ensured.Handle.WorkspacePath));
    }

    [Fact]
    public async Task HookCwdOverrideCannotEscapeWorkspace()
    {
        var manager = NewManager(new HookConfig(
            null,
            HookDefinition.Shell("echo nope").WithCwd("../outside"),
            null, null, TimeSpan.FromSeconds(60)));
        var ensured = Must(await manager.Ensure(SampleIssue("COE-263-cwd")));

        var error = MustErr(await manager.StartRun(ensured.Handle, RunDescriptor.New("run-cwd", 1)));
        var escape = Assert.IsType<HookPathEscape>(error);
        Assert.Equal(HookKind.BeforeRun, escape.Hook);
    }

    [Fact(Skip = "Unix-only: symlinked workspace root (#[cfg(unix)] in Rust)")]
    public async Task WorkspaceHandleValidationRejectsSymlinkedWorkspaceRoots()
    {
        // Unix-only: requires symlink creation
    }

    [Fact(Skip = "Unix-only: symlink cwd escape (#[cfg(unix)] in Rust)")]
    public async Task HookCwdOverrideCannotEscapeWorkspaceThroughSymlink()
    {
        // Unix-only: requires symlink creation
    }

    [Fact(Skip = "Unix-only: symlinked manifest paths (#[cfg(unix)] in Rust)")]
    public async Task ManagedManifestPathsRejectSymlinkedReadsAndWrites()
    {
        // Unix-only: requires symlink creation
    }
}
