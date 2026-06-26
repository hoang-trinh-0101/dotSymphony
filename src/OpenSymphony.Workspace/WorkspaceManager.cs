using System.Diagnostics;
using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Workspace;

public sealed class WorkspaceManager
{
    private readonly WorkspaceManagerConfig _config;

    public WorkspaceManager(WorkspaceManagerConfig config)
    {
        // ht: normalize root via NormalizeAbsolutePath (lexical, Path.GetFullPath).
        var normalized = WorkspacePaths.NormalizeAbsolutePath(config.Root);
        if (normalized.IsErr) throw new ArgumentException(normalized.Error.Message);
        _config = config with { Root = normalized.Value };
    }

    public WorkspaceManagerConfig Config => _config;

    public Result<string, WorkspaceError> WorkspacePathFor(string issueIdentifier)
        => WorkspacePaths.WorkspacePathForRoot(_config.Root, issueIdentifier);

    // ht: Canonicalize = Path.GetFullPath (lexical). Symlink safety comes from the
    //   explicit symlink-rejection walks, matching observable test behavior.
    private static string Canonicalize(string path) => Path.GetFullPath(path);

    private static void EnsureDescendant(string root, string candidate)
    {
        // ht: Rust uses Path::starts_with (component-aware). On Windows, Path.GetFullPath
        //   normalizes separators so string StartsWith with Ordinal is equivalent here.
        if (candidate != root && !candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new WorkspaceErrorException(new PathEscape(root, candidate));
    }

    public async Task<Result<EnsureWorkspaceResult, WorkspaceError>> Ensure(IssueDescriptor issue)
    {
        try
        {
            await CreateDirectory(_config.Root);
            var canonicalRoot = Canonicalize(_config.Root);
            var workspaceKeyResult = WorkspacePaths.SanitizeWorkspaceKey(issue.Identifier);
            if (workspaceKeyResult.IsErr) return Result<EnsureWorkspaceResult, WorkspaceError>.Err(workspaceKeyResult.Error);
            var workspaceKey = workspaceKeyResult.Value;

            var workspacePathResult = WorkspacePaths.WorkspacePathForRoot(canonicalRoot, issue.Identifier);
            if (workspacePathResult.IsErr) return Result<EnsureWorkspaceResult, WorkspaceError>.Err(workspacePathResult.Error);
            var workspacePath = workspacePathResult.Value;

            RejectSymlinkedWorkspaceRoot(workspacePath);
            await CreateDirectory(workspacePath);
            RejectSymlinkedWorkspaceRoot(workspacePath);
            var canonicalWorkspace = Canonicalize(workspacePath);
            EnsureDescendant(canonicalRoot, canonicalWorkspace);

            var handle = new WorkspaceHandle(issue.IssueId, issue.Identifier, workspaceKey, canonicalWorkspace);
            var existingState = await InspectWorkspaceState(issue, handle);
            if (existingState is ExistingWorkspaceState.Conflict claim)
            {
                return Result<EnsureWorkspaceResult, WorkspaceError>.Err(
                    new WorkspaceOwnershipConflict(new WorkspaceOwnershipConflictDetails(
                        handle.WorkspacePath, handle.WorkspaceKey,
                        claim.Claim.IssueId, claim.Claim.Identifier,
                        issue.IssueId, issue.Identifier)));
            }

            var created = existingState is ExistingWorkspaceState.Missing or ExistingWorkspaceState.ForeignArtifact;
            HookExecutionRecord? afterCreate = null;
            if (created)
            {
                var hookResult = await ExecuteHook(HookKind.AfterCreate, handle);
                if (hookResult is HookFailure af)
                {
                    return Result<EnsureWorkspaceResult, WorkspaceError>.Err(af.Error);
                }
                if (hookResult is HookSuccess { Record: { } record })
                {
                    await WriteAfterCreateReceipt(issue, handle);
                    afterCreate = record;
                }
            }

            await BootstrapWorkspaceLayout(handle);
            var issueManifest = await UpsertIssueManifest(issue, handle);

            return Result<EnsureWorkspaceResult, WorkspaceError>.Ok(
                new EnsureWorkspaceResult(handle, issueManifest, created, afterCreate));
        }
        catch (WorkspaceErrorException ex)
        {
            return Result<EnsureWorkspaceResult, WorkspaceError>.Err(ex.Error);
        }
    }

    public async Task<Result<RunManifest, WorkspaceError>> StartRun(WorkspaceHandle workspace, RunDescriptor run)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            var manifest = RunManifest.New(workspace, run);
            await WriteRunManifest(workspace, manifest);

            var hookResult = await ExecuteHook(HookKind.BeforeRun, workspace);
            if (hookResult is HookFailure failure)
            {
                manifest.Status = RunStatus.PreparationFailed;
                manifest.StatusDetail = failure.Error.Message;
                manifest.UpdatedAt = DateTimeOffset.UtcNow;
                manifest.Hooks.Add(failure.Record);
                await WriteRunManifest(workspace, manifest);
                return Result<RunManifest, WorkspaceError>.Err(failure.Error);
            }
            if (hookResult is HookSuccess { Record: { } record })
                manifest.Hooks.Add(record);

            manifest.Status = RunStatus.Prepared;
            manifest.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteRunManifest(workspace, manifest);
            return Result<RunManifest, WorkspaceError>.Ok(manifest);
        }
        catch (WorkspaceErrorException ex)
        {
            return Result<RunManifest, WorkspaceError>.Err(ex.Error);
        }
    }

    public async Task<Result<Unit, WorkspaceError>> FinishRun(WorkspaceHandle workspace, RunManifest runManifest, RunStatus status)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            runManifest.Status = status;
            runManifest.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteRunManifest(workspace, runManifest);

            var hookResult = await ExecuteHook(HookKind.AfterRun, workspace);
            if (hookResult is HookFailure failure)
                runManifest.Hooks.Add(failure.Record);
            else if (hookResult is HookSuccess { Record: { } record })
                runManifest.Hooks.Add(record);

            runManifest.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteRunManifest(workspace, runManifest);
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex)
        {
            return Result<Unit, WorkspaceError>.Err(ex.Error);
        }
    }

    public CleanupDecision CleanupDecision(IssueLifecycleState state)
        => state == IssueLifecycleState.Terminal && _config.Cleanup.RemoveTerminalWorkspaces
            ? Workspace.CleanupDecision.Remove : Workspace.CleanupDecision.Retain;

    public async Task<Result<CleanupOutcome, WorkspaceError>> Cleanup(WorkspaceHandle workspace, IssueLifecycleState state)
    {
        try
        {
            if (!PathExists(workspace.WorkspacePath))
                return Result<CleanupOutcome, WorkspaceError>.Ok(
                    new CleanupOutcome(CleanupDecision(state), null));

            ValidateWorkspaceHandle(workspace);
            if (state != IssueLifecycleState.Terminal)
                return Result<CleanupOutcome, WorkspaceError>.Ok(
                    new CleanupOutcome(Workspace.CleanupDecision.Retain, null));

            HookExecutionRecord? beforeRemove = null;
            var hookResult = await ExecuteHook(HookKind.BeforeRemove, workspace);
            if (hookResult is HookFailure failure)
                beforeRemove = failure.Record;
            else if (hookResult is HookSuccess { Record: { } record })
                beforeRemove = record;

            var decision = CleanupDecision(state);
            if (decision == Workspace.CleanupDecision.Remove)
            {
                try { Directory.Delete(workspace.WorkspacePath, recursive: true); }
                catch (DirectoryNotFoundException) { /* ok */ }
                catch (Exception ex) { throw new WorkspaceErrorException(new RemoveWorkspace(workspace.WorkspacePath, ex)); }
            }

            return Result<CleanupOutcome, WorkspaceError>.Ok(new CleanupOutcome(decision, beforeRemove));
        }
        catch (WorkspaceErrorException ex)
        {
            return Result<CleanupOutcome, WorkspaceError>.Err(ex.Error);
        }
    }

    public async Task<Result<IssueManifest?, WorkspaceError>> LoadIssueManifest(WorkspaceHandle workspace)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            return Result<IssueManifest?, WorkspaceError>.Ok(await LoadManifest<IssueManifest>(workspace, workspace.IssueManifestPath()));
        }
        catch (WorkspaceErrorException ex) { return Result<IssueManifest?, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<WorkspaceHandle?, WorkspaceError>> FindWorkspaceByIssueReference(string issueReference)
    {
        try
        {
            await CreateDirectory(_config.Root);
            // ht: try direct identifier → workspace path first.
            var directResult = WorkspacePaths.WorkspacePathForRoot(_config.Root, issueReference);
            if (directResult.IsOk)
            {
                var loaded = await LoadWorkspaceFromDirectory(directResult.Value);
                if (loaded is { } tuple && WorkspaceMatchesIssueReference(tuple.Manifest, issueReference))
                    return Result<WorkspaceHandle?, WorkspaceError>.Ok(tuple.Handle);
            }
            // ht: scan all dirs for issue_id match.
            foreach (var entry in Directory.EnumerateDirectories(_config.Root))
            {
                var loaded = await LoadWorkspaceFromDirectory(entry);
                if (loaded is { } tuple && WorkspaceMatchesIssueReference(tuple.Manifest, issueReference))
                    return Result<WorkspaceHandle?, WorkspaceError>.Ok(tuple.Handle);
            }
            return Result<WorkspaceHandle?, WorkspaceError>.Ok(null);
        }
        catch (WorkspaceErrorException ex) { return Result<WorkspaceHandle?, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<List<(WorkspaceHandle Handle, IssueManifest Manifest)>, WorkspaceError>> ListAllWorkspaces()
    {
        try
        {
            await CreateDirectory(_config.Root);
            var workspaces = new List<(WorkspaceHandle, IssueManifest)>();
            foreach (var entry in Directory.EnumerateDirectories(_config.Root))
            {
                var loaded = await LoadWorkspaceFromDirectory(entry);
                if (loaded is { } tuple)
                    workspaces.Add((tuple.Handle, tuple.Manifest));
            }
            return Result<List<(WorkspaceHandle, IssueManifest)>, WorkspaceError>.Ok(workspaces);
        }
        catch (WorkspaceErrorException ex) { return Result<List<(WorkspaceHandle, IssueManifest)>, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteIssueManifest(WorkspaceHandle workspace, IssueManifest manifest)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            await WriteManifest(workspace, workspace.IssueManifestPath(), manifest);
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<RunManifest?, WorkspaceError>> LoadRunManifest(WorkspaceHandle workspace)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            return Result<RunManifest?, WorkspaceError>.Ok(await LoadManifest<RunManifest>(workspace, workspace.RunManifestPath()));
        }
        catch (WorkspaceErrorException ex) { return Result<RunManifest?, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteRunManifest(WorkspaceHandle workspace, RunManifest manifest)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            await WriteManifest(workspace, workspace.RunManifestPath(), manifest);
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<string?, WorkspaceError>> ReadTextArtifact(WorkspaceHandle workspace, string path)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            var validated = ValidateWorkspaceOwnedPath(workspace, path);
            try
            {
                return Result<string?, WorkspaceError>.Ok(File.ReadAllText(validated));
            }
            catch (FileNotFoundException) { return Result<string?, WorkspaceError>.Ok(null); }
            catch (DirectoryNotFoundException) { return Result<string?, WorkspaceError>.Ok(null); }
        }
        catch (WorkspaceErrorException ex) { return Result<string?, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteTextArtifact(WorkspaceHandle workspace, string path, string contents)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            await WriteBytesArtifact(workspace, path, System.Text.Encoding.UTF8.GetBytes(contents));
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteJsonArtifact<T>(WorkspaceHandle workspace, string path, T artifact)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            var normalized = WorkspacePaths.NormalizeAbsolutePath(path);
            if (normalized.IsErr) return Result<Unit, WorkspaceError>.Err(normalized.Error);
            var validatedPath = normalized.Value;
            byte[] payload;
            try { payload = JsonSerializer.SerializeToUtf8Bytes(artifact, WorkspaceJsonOptions.Default); }
            catch (Exception ex) { throw new WorkspaceErrorException(new EncodeJsonArtifact(validatedPath, ex)); }
            await WriteBytesArtifact(workspace, validatedPath, payload);
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<ConversationManifest?, WorkspaceError>> LoadConversationManifest(WorkspaceHandle workspace)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            return Result<ConversationManifest?, WorkspaceError>.Ok(
                await LoadManifest<ConversationManifest>(workspace, workspace.ConversationManifestPath()));
        }
        catch (WorkspaceErrorException ex) { return Result<ConversationManifest?, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteConversationManifest(WorkspaceHandle workspace, ConversationManifest manifest)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            await WriteManifest(workspace, workspace.ConversationManifestPath(), manifest);
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<PromptCaptureManifest, WorkspaceError>> WritePromptCapture(
        WorkspaceHandle workspace, RunDescriptor run, PromptCaptureDescriptor descriptor, string prompt)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            var manifest = PromptCaptureManifest.New(workspace, run, descriptor, prompt);
            var archivedManifestPath = workspace.RunPromptManifestPath(run.Attempt, descriptor.Kind, descriptor.Sequence);
            var stableManifestPath = workspace.LatestPromptManifestPath(descriptor.Kind);

            await WriteTextArtifact(workspace, manifest.ArchivedPromptPath, prompt);
            await WriteTextArtifact(workspace, manifest.StablePromptPath, prompt);
            await WriteManifest(workspace, archivedManifestPath, manifest);
            await WriteManifest(workspace, stableManifestPath, manifest);
            return Result<PromptCaptureManifest, WorkspaceError>.Ok(manifest);
        }
        catch (WorkspaceErrorException ex) { return Result<PromptCaptureManifest, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteIssueContext(WorkspaceHandle workspace, IssueContextArtifact artifact)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            await WriteTextArtifact(workspace, workspace.IssueContextPath(), artifact.RenderMarkdown(workspace));
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<SessionContextArtifact?, WorkspaceError>> LoadSessionContext(WorkspaceHandle workspace)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            return Result<SessionContextArtifact?, WorkspaceError>.Ok(
                await LoadManifest<SessionContextArtifact>(workspace, workspace.SessionContextPath()));
        }
        catch (WorkspaceErrorException ex) { return Result<SessionContextArtifact?, WorkspaceError>.Err(ex.Error); }
    }

    public async Task<Result<Unit, WorkspaceError>> WriteSessionContext(WorkspaceHandle workspace, SessionContextArtifact artifact)
    {
        try
        {
            ValidateWorkspaceHandle(workspace);
            await WriteManifest(workspace, workspace.SessionContextPath(), artifact);
            return Result<Unit, WorkspaceError>.Ok(Unit.Value);
        }
        catch (WorkspaceErrorException ex) { return Result<Unit, WorkspaceError>.Err(ex.Error); }
    }

    // --- internal helpers ---

    private async Task<IssueManifest> UpsertIssueManifest(IssueDescriptor issue, WorkspaceHandle workspace)
    {
        IssueManifest? existing = null;
        var state = await InspectIssueManifestState(issue, workspace);
        if (state is ExistingIssueManifestState.Owned owned)
            existing = owned.Manifest;
        else if (state is ExistingIssueManifestState.Conflict conflict)
            throw new WorkspaceErrorException(new WorkspaceOwnershipConflict(new WorkspaceOwnershipConflictDetails(
                workspace.WorkspacePath, workspace.WorkspaceKey,
                conflict.Manifest.IssueId, conflict.Manifest.Identifier,
                issue.IssueId, issue.Identifier)));

        var now = DateTimeOffset.UtcNow;
        var manifest = new IssueManifest(
            issue.IssueId, issue.Identifier, issue.Title, issue.CurrentState,
            workspace.WorkspaceKey, workspace.WorkspacePath,
            existing?.CreatedAt ?? now, now, issue.LastSeenTrackerRefreshAt);
        await WriteManifest(workspace, workspace.IssueManifestPath(), manifest);
        return manifest;
    }

    private async Task WriteAfterCreateReceipt(IssueDescriptor issue, WorkspaceHandle workspace)
    {
        var receipt = AfterCreateBootstrapReceipt.New(workspace, issue);
        await WriteManifest(workspace, workspace.AfterCreateReceiptPath(), receipt);
    }

    private async Task BootstrapWorkspaceLayout(WorkspaceHandle workspace)
    {
        foreach (var dir in new[]
        {
            workspace.MetadataDir(), workspace.LogsDir(), workspace.GeneratedDir(),
            workspace.OpenhandsDir(), workspace.PromptsDir(), workspace.RunsDir(),
        })
        {
            await CreateManagedDirectory(workspace, dir);
        }
    }

    private async Task<ExistingIssueManifestState> InspectIssueManifestState(IssueDescriptor issue, WorkspaceHandle workspace)
    {
        var path = ValidateWorkspaceOwnedPath(workspace, workspace.IssueManifestPath());
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (FileNotFoundException) { return ExistingIssueManifestState.Missing.Instance; }
        catch (DirectoryNotFoundException) { return ExistingIssueManifestState.Missing.Instance; }
        catch (Exception ex) { throw new WorkspaceErrorException(new ReadManifest(path, ex)); }

        try
        {
            var manifest = JsonSerializer.Deserialize<IssueManifest>(raw, WorkspaceJsonOptions.Default)!;
            return ClassifyIssueManifestOwnership(issue, workspace, manifest);
        }
        catch (JsonException) { return ExistingIssueManifestState.ForeignArtifact.Instance; }
    }

    private async Task<ExistingReceiptState> InspectAfterCreateReceiptState(IssueDescriptor issue, WorkspaceHandle workspace)
    {
        var path = ValidateWorkspaceOwnedPath(workspace, workspace.AfterCreateReceiptPath());
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (FileNotFoundException) { return ExistingReceiptState.Missing.Instance; }
        catch (DirectoryNotFoundException) { return ExistingReceiptState.Missing.Instance; }
        catch (Exception ex) { throw new WorkspaceErrorException(new ReadManifest(path, ex)); }

        try
        {
            var receipt = JsonSerializer.Deserialize<AfterCreateBootstrapReceipt>(raw, WorkspaceJsonOptions.Default)!;
            return ClassifyAfterCreateReceiptOwnership(issue, workspace, receipt);
        }
        catch (JsonException) { return ExistingReceiptState.ForeignArtifact.Instance; }
    }

    private async Task<ExistingWorkspaceState> InspectWorkspaceState(IssueDescriptor issue, WorkspaceHandle workspace)
    {
        var issueManifestState = await InspectIssueManifestState(issue, workspace);
        var issueManifestIsForeign = issueManifestState is ExistingIssueManifestState.ForeignArtifact;

        if (issueManifestState is ExistingIssueManifestState.Owned)
            return ExistingWorkspaceState.Owned.Instance;
        if (issueManifestState is ExistingIssueManifestState.Conflict c)
            return new ExistingWorkspaceState.Conflict(new WorkspaceOwnershipClaim(c.Manifest.IssueId, c.Manifest.Identifier));

        var receiptState = await InspectAfterCreateReceiptState(issue, workspace);
        return receiptState switch
        {
            ExistingReceiptState.Owned => ExistingWorkspaceState.AfterCreateCompleted.Instance,
            ExistingReceiptState.Conflict rc => new ExistingWorkspaceState.Conflict(
                new WorkspaceOwnershipClaim(rc.Receipt.IssueId, rc.Receipt.Identifier)),
            ExistingReceiptState.ForeignArtifact => ExistingWorkspaceState.ForeignArtifact.Instance,
            ExistingReceiptState.Missing => issueManifestIsForeign
                ? ExistingWorkspaceState.ForeignArtifact.Instance
                : ExistingWorkspaceState.Missing.Instance,
            _ => ExistingWorkspaceState.Missing.Instance,
        };
    }

    private async Task<(WorkspaceHandle Handle, IssueManifest Manifest)?> LoadWorkspaceFromDirectory(string workspacePath)
    {
        RejectSymlinkedWorkspaceRoot(workspacePath);
        if (!PathExists(workspacePath)) return null;

        var canonicalRoot = Canonicalize(_config.Root);
        var canonicalWorkspace = Canonicalize(workspacePath);
        EnsureDescendant(canonicalRoot, canonicalWorkspace);

        var issueManifestPath = Path.Join(canonicalWorkspace, ".opensymphony", "issue.json");
        string raw;
        try { raw = File.ReadAllText(issueManifestPath); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) { throw new WorkspaceErrorException(new ReadManifest(issueManifestPath, ex)); }

        IssueManifest manifest;
        try { manifest = JsonSerializer.Deserialize<IssueManifest>(raw, WorkspaceJsonOptions.Default)!; }
        catch (JsonException) { return null; }

        var handle = new WorkspaceHandle(manifest.IssueId, manifest.Identifier, manifest.SanitizedWorkspaceKey, canonicalWorkspace);
        if (!IssueManifestClaimsWorkspace(handle, manifest)) return null;
        return (handle, manifest);
    }

    // --- hook execution ---

    private async Task<HookResult> ExecuteHook(HookKind kind, WorkspaceHandle workspace)
    {
        var hook = HookDefinition(kind);
        if (hook is null) return new HookSuccess(null);

        var cwdResult = await ResolveHookCwd(workspace, kind, hook);
        if (cwdResult is HookFailure cwdFailure) return cwdFailure;
        var cwd = ((HookCwdResolved)cwdResult).Path;

        var command = BuildShellCommand(hook.Command);
        command.WorkingDirectory = cwd;
        command.RedirectStandardOutput = true;
        command.RedirectStandardError = true;
        command.UseShellExecute = false;

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        HookCommandOutput output;
        try { output = await RunHookCommand(command, _config.Hooks.Timeout); }
        catch (Exception ex)
        {
            var failedRecord = new HookExecutionRecord(kind, hook.Command, cwd, !kind.IsRequired(),
                HookExecutionStatus.Failed, startedAt, DateTimeOffset.UtcNow, (ulong)stopwatch.Elapsed.TotalMilliseconds,
                null, "", "");
            return new HookFailure(new LaunchHook(kind, cwd, ex), failedRecord);
        }

        var finishedAt = DateTimeOffset.UtcNow;
        var durationMs = (ulong)stopwatch.Elapsed.TotalMilliseconds;

        if (output is HookCommandCompleted completed)
        {
            var exitCode = completed.ExitCode;
            if (completed.Success)
            {
                return new HookSuccess(new HookExecutionRecord(kind, hook.Command, cwd, !kind.IsRequired(),
                    HookExecutionStatus.Succeeded, startedAt, finishedAt, durationMs, exitCode,
                    completed.Stdout, completed.Stderr));
            }
            var failedRecord = new HookExecutionRecord(kind, hook.Command, cwd, !kind.IsRequired(),
                HookExecutionStatus.Failed, startedAt, finishedAt, durationMs, exitCode,
                completed.Stdout, completed.Stderr);
            return new HookFailure(new HookFailed(kind, hook.Command, exitCode, completed.Stdout, completed.Stderr), failedRecord);
        }
        else if (output is HookCommandTimedOut timedOut)
        {
            var timedOutRecord = new HookExecutionRecord(kind, hook.Command, cwd, !kind.IsRequired(),
                HookExecutionStatus.TimedOut, startedAt, finishedAt, durationMs, null,
                timedOut.Stdout, timedOut.Stderr);
            return new HookFailure(new HookTimedOut(kind, hook.Command, _config.Hooks.Timeout), timedOutRecord);
        }
        return new HookSuccess(null);
    }

    private HookDefinition? HookDefinition(HookKind kind) => kind switch
    {
        HookKind.AfterCreate => _config.Hooks.AfterCreate,
        HookKind.BeforeRun => _config.Hooks.BeforeRun,
        HookKind.AfterRun => _config.Hooks.AfterRun,
        HookKind.BeforeRemove => _config.Hooks.BeforeRemove,
        _ => null,
    };

    private Task<HookResult> ResolveHookCwd(WorkspaceHandle workspace, HookKind kind, HookDefinition hook)
    {
        var workspacePath = workspace.WorkspacePath;
        if (hook.Cwd is null)
            return Task.FromResult<HookResult>(new HookCwdResolved(workspacePath));

        var resolveResult = WorkspacePaths.ResolvePathWithinRoot(workspacePath, hook.Cwd);
        if (resolveResult.IsErr)
        {
            var escaped = resolveResult.Error is PathEscape pe ? pe.Path : hook.Cwd;
            var failedRecord = new HookExecutionRecord(kind, hook.Command, escaped, !kind.IsRequired(),
                HookExecutionStatus.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, "", "");
            return Task.FromResult<HookResult>(new HookFailure(
                new HookPathEscape(kind, workspacePath, escaped), failedRecord));
        }

        var lexicalCwd = resolveResult.Value;
        string canonicalCwd;
        try { canonicalCwd = Path.GetFullPath(lexicalCwd); }
        catch (Exception ex)
        {
            var failedRecord = new HookExecutionRecord(kind, hook.Command, lexicalCwd, !kind.IsRequired(),
                HookExecutionStatus.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, "", "");
            return Task.FromResult<HookResult>(new HookFailure(new LaunchHook(kind, lexicalCwd, ex), failedRecord));
        }

        try { EnsureDescendant(workspacePath, canonicalCwd); }
        catch (WorkspaceErrorException)
        {
            var failedRecord = new HookExecutionRecord(kind, hook.Command, canonicalCwd, !kind.IsRequired(),
                HookExecutionStatus.Failed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, "", "");
            return Task.FromResult<HookResult>(new HookFailure(new HookPathEscape(kind, workspacePath, canonicalCwd), failedRecord));
        }

        return Task.FromResult<HookResult>(new HookCwdResolved(canonicalCwd));
    }

    private void ValidateWorkspaceHandle(WorkspaceHandle workspace)
    {
        RejectSymlinkedWorkspaceRoot(workspace.WorkspacePath);
        var canonicalRoot = Canonicalize(_config.Root);
        var canonicalWorkspace = Canonicalize(workspace.WorkspacePath);
        EnsureDescendant(canonicalRoot, canonicalWorkspace);
    }

    private async Task CreateDirectory(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) { throw new WorkspaceErrorException(new CreateDirectoryError(path, ex)); }
        await Task.CompletedTask;
    }

    private async Task CreateManagedDirectory(WorkspaceHandle workspace, string path)
    {
        var validated = ValidateWorkspaceOwnedPath(workspace, path);
        await CreateDirectory(validated);
        ValidateWorkspaceOwnedPath(workspace, validated);
    }

    private void RejectSymlinkedWorkspaceRoot(string path)
    {
        var linkTarget = GetSymlinkTarget(path);
        if (linkTarget is not null)
            throw new WorkspaceErrorException(new WorkspacePathSymlink(path));
    }

    private static string? GetSymlinkTarget(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && (fi.Attributes & FileAttributes.ReparsePoint) != 0)
                return fi.LinkTarget;
            var di = new DirectoryInfo(path);
            if (di.Exists && (di.Attributes & FileAttributes.ReparsePoint) != 0)
                return di.LinkTarget;
            return null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private async Task<T?> LoadManifest<T>(WorkspaceHandle workspace, string path)
    {
        var validated = ValidateWorkspaceOwnedPath(workspace, path);
        string raw;
        try { raw = File.ReadAllText(validated); }
        catch (FileNotFoundException) { return default; }
        catch (DirectoryNotFoundException) { return default; }
        catch (Exception ex) { throw new WorkspaceErrorException(new ReadManifest(validated, ex)); }

        try { return JsonSerializer.Deserialize<T>(raw, WorkspaceJsonOptions.Default); }
        catch (JsonException ex) { throw new WorkspaceErrorException(new DecodeManifest(validated, ex)); }
    }

    private async Task WriteManifest<T>(WorkspaceHandle workspace, string path, T manifest)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
            await CreateManagedDirectory(workspace, parent);
        var validated = ValidateWorkspaceOwnedPath(workspace, path);

        byte[] payload;
        try { payload = JsonSerializer.SerializeToUtf8Bytes(manifest, WorkspaceJsonOptions.Default); }
        catch (Exception ex) { throw new WorkspaceErrorException(new EncodeManifest(validated, ex)); }

        try { File.WriteAllBytes(validated, payload); }
        catch (Exception ex) { throw new WorkspaceErrorException(new WriteManifestError(validated, ex)); }
    }

    private async Task WriteBytesArtifact(WorkspaceHandle workspace, string path, byte[] payload)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is not null)
            await CreateManagedDirectory(workspace, parent);
        var validated = ValidateWorkspaceOwnedPath(workspace, path);

        try { File.WriteAllBytes(validated, payload); }
        catch (Exception ex) { throw new WorkspaceErrorException(new WriteArtifact(validated, ex)); }
    }

    private string ValidateWorkspaceOwnedPath(WorkspaceHandle workspace, string path)
    {
        var normalized = WorkspacePaths.NormalizeAbsolutePath(path);
        if (normalized.IsErr) throw new WorkspaceErrorException(normalized.Error);
        var norm = normalized.Value;
        EnsureDescendant(workspace.WorkspacePath, norm);

        // ht: walk each component from workspacePath downward, reject symlinks.
        var root = workspace.WorkspacePath;
        if (string.Equals(norm, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return norm;
        var relative = norm[(root.Length + 1)..];
        var current = root;
        foreach (var component in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(component)) continue;
            current = Path.Join(current, component);
            var linkTarget = GetSymlinkTarget(current);
            if (linkTarget is not null)
                throw new WorkspaceErrorException(new ManagedPathSymlink(current));
            if (!File.Exists(current) && !Directory.Exists(current))
                break; // NotFound → ok, stop walking
        }
        return norm;
    }

    private static bool PathExists(string path)
    {
        try { return File.Exists(path) || Directory.Exists(path); }
        catch { return false; }
    }

    // --- classification helpers ---

    private static ExistingIssueManifestState ClassifyIssueManifestOwnership(
        IssueDescriptor issue, WorkspaceHandle workspace, IssueManifest manifest)
    {
        if (!IssueManifestClaimsWorkspace(workspace, manifest))
            return ExistingIssueManifestState.ForeignArtifact.Instance;
        if (manifest.IssueId == issue.IssueId && manifest.Identifier == issue.Identifier)
            return new ExistingIssueManifestState.Owned(manifest);
        return new ExistingIssueManifestState.Conflict(manifest);
    }

    private static ExistingReceiptState ClassifyAfterCreateReceiptOwnership(
        IssueDescriptor issue, WorkspaceHandle workspace, AfterCreateBootstrapReceipt receipt)
    {
        if (!AfterCreateReceiptClaimsWorkspace(workspace, receipt))
            return ExistingReceiptState.ForeignArtifact.Instance;
        if (receipt.IssueId == issue.IssueId && receipt.Identifier == issue.Identifier)
            return ExistingReceiptState.Owned.Instance;
        return new ExistingReceiptState.Conflict(receipt);
    }

    private static bool IssueManifestClaimsWorkspace(WorkspaceHandle workspace, IssueManifest manifest)
        => WorkspacePathClaimMatches(workspace, manifest.SanitizedWorkspaceKey, manifest.WorkspacePath);

    private static bool AfterCreateReceiptClaimsWorkspace(WorkspaceHandle workspace, AfterCreateBootstrapReceipt receipt)
        => WorkspacePathClaimMatches(workspace, receipt.SanitizedWorkspaceKey, receipt.WorkspacePath);

    private static bool WorkspacePathClaimMatches(WorkspaceHandle workspace, string claimedKey, string claimedPath)
    {
        if (claimedKey != workspace.WorkspaceKey) return false;
        var normalized = WorkspacePaths.NormalizeAbsolutePath(claimedPath);
        return normalized.IsOk && normalized.Value == workspace.WorkspacePath;
    }

    private static bool WorkspaceMatchesIssueReference(IssueManifest manifest, string issueReference)
        => manifest.Identifier == issueReference || manifest.IssueId == issueReference;

    // --- shell command ---

    private static ProcessStartInfo BuildShellCommand(string command)
    {
        if (OperatingSystem.IsWindows())
            return new ProcessStartInfo("cmd", $"/C {command}");
        return new ProcessStartInfo("sh", "-c") { ArgumentList = { command } };
    }

    private static async Task<HookCommandOutput> RunHookCommand(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // ht: read stdout+stderr in parallel to avoid deadlock on full pipes.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(timeout));

        if (completed == exitTask)
        {
            await exitTask;
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new HookCommandCompleted(stdout, stderr, process.ExitCode);
        }
        else
        {
            // ht: timeout — kill the process tree.
            TerminateHookProcessTree(process);
            try { await process.WaitForExitAsync(); } catch { }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new HookCommandTimedOut(stdout, stderr);
        }
    }

    private static void TerminateHookProcessTree(Process process)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var killer = new Process
                {
                    StartInfo = new ProcessStartInfo("taskkill", $"/T /F /PID {process.Id}")
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true },
                };
                killer.Start();
                killer.WaitForExit();
            }
            else
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch { /* best effort */ }
    }
}

// --- internal types ---

internal abstract record HookResult;
internal sealed record HookSuccess(HookExecutionRecord? Record) : HookResult;
internal sealed record HookCwdResolved(string Path) : HookResult;
internal sealed record HookFailure(WorkspaceError Error, HookExecutionRecord Record) : HookResult;

internal abstract record HookCommandOutput;
internal sealed record HookCommandCompleted(string Stdout, string Stderr, int ExitCode) : HookCommandOutput
{
    public bool Success => ExitCode == 0;
}
internal sealed record HookCommandTimedOut(string Stdout, string Stderr) : HookCommandOutput;

internal abstract record ExistingIssueManifestState
{
    public sealed record Missing : ExistingIssueManifestState { public static Missing Instance => new(); }
    public sealed record Owned(IssueManifest Manifest) : ExistingIssueManifestState;
    public sealed record ForeignArtifact : ExistingIssueManifestState { public static ForeignArtifact Instance => new(); }
    public sealed record Conflict(IssueManifest Manifest) : ExistingIssueManifestState;
}

internal abstract record ExistingReceiptState
{
    public sealed record Missing : ExistingReceiptState { public static Missing Instance => new(); }
    public sealed record Owned : ExistingReceiptState { public static Owned Instance => new(); }
    public sealed record ForeignArtifact : ExistingReceiptState { public static ForeignArtifact Instance => new(); }
    public sealed record Conflict(AfterCreateBootstrapReceipt Receipt) : ExistingReceiptState;
}

internal abstract record ExistingWorkspaceState
{
    public sealed record Missing : ExistingWorkspaceState { public static Missing Instance => new(); }
    public sealed record Owned : ExistingWorkspaceState { public static Owned Instance => new(); }
    public sealed record AfterCreateCompleted : ExistingWorkspaceState { public static AfterCreateCompleted Instance => new(); }
    public sealed record ForeignArtifact : ExistingWorkspaceState { public static ForeignArtifact Instance => new(); }
    public sealed record Conflict(WorkspaceOwnershipClaim Claim) : ExistingWorkspaceState;
}

internal sealed record WorkspaceOwnershipClaim(string IssueId, string Identifier);
