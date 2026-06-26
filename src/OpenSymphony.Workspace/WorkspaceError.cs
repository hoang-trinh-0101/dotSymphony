namespace OpenSymphony.Workspace;

// ht: abstract record base + sealed subtypes mirrors the Rust enum's #[error] messages.
public abstract record WorkspaceError
{
    public virtual string Message => ToString();
}

// ht: wrapper to throw/catch WorkspaceError as Exception (records can't extend Exception).
public sealed class WorkspaceErrorException : Exception
{
    public WorkspaceError Error { get; }
    public WorkspaceErrorException(WorkspaceError error) : base(error.Message) { Error = error; }
}

public sealed record RootNotAbsolute(string Path) : WorkspaceError
{
    public override string Message => $"workspace root must be absolute: {Path}";
}

public sealed record EmptyIdentifier : WorkspaceError
{
    public override string Message => "issue identifier cannot be empty";
}

public sealed record InvalidWorkspaceKey(string Key) : WorkspaceError
{
    public override string Message => $"sanitized workspace key is invalid or reserved: {Key}";
}

public sealed record PathEscape(string Root, string Path) : WorkspaceError
{
    public override string Message => $"path {Path} escapes configured root {Root}";
}

public sealed record WorkspaceOwnershipConflictDetails(
    string Workspace,
    string WorkspaceKey,
    string ExistingIssueId,
    string ExistingIdentifier,
    string RequestedIssueId,
    string RequestedIdentifier)
{
    public override string ToString()
        => $"workspace {Workspace} with sanitized key {WorkspaceKey} is already owned by issue {ExistingIdentifier} ({ExistingIssueId}), cannot reuse it for {RequestedIdentifier} ({RequestedIssueId})";
}

public sealed record WorkspaceOwnershipConflict(WorkspaceOwnershipConflictDetails Details) : WorkspaceError
{
    public override string Message => Details.ToString();
}

public sealed record WorkspacePathSymlink(string Path) : WorkspaceError
{
    public override string Message => $"issue workspace path may not be a symlink: {Path}";
}

public sealed record ManagedPathSymlink(string Path) : WorkspaceError
{
    public override string Message => $"OpenSymphony-managed path may not be a symlink: {Path}";
}

public sealed record CreateDirectoryError(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to create directory {Path}: {Source}";
}

public sealed record CanonicalizeError(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to canonicalize {Path}: {Source}";
}

public sealed record ReadDirectory(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to read directory {Path}: {Source}";
}

public sealed record ReadManifest(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to read manifest {Path}: {Source}";
}

public sealed record ReadManagedFile(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to read managed file {Path}: {Source}";
}

public sealed record DecodeManifest(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to decode manifest {Path}: {Source}";
}

public sealed record EncodeManifest(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to encode manifest {Path}: {Source}";
}

public sealed record EncodeJsonArtifact(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to encode JSON artifact {Path}: {Source}";
}

public sealed record WriteManifestError(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to write manifest {Path}: {Source}";
}

public sealed record WriteArtifact(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to write artifact {Path}: {Source}";
}

public sealed record LaunchHook(HookKind Hook, string Cwd, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to launch hook `{Hook}` in {Cwd}: {Source}";
}

public sealed record HookPathEscape(HookKind Hook, string Workspace, string Cwd) : WorkspaceError
{
    public override string Message => $"hook `{Hook}` cwd {Cwd} escapes workspace {Workspace}";
}

public sealed record HookTimedOut(HookKind Hook, string Command, TimeSpan Timeout) : WorkspaceError
{
    public override string Message => $"hook `{Hook}` timed out after {Timeout}: {Command}";
}

public sealed record HookFailed(HookKind Hook, string Command, int? ExitCode, string Stdout, string Stderr) : WorkspaceError
{
    public override string Message => $"hook `{Hook}` failed with exit code {ExitCode}: {Command}";
}

public sealed record RemoveWorkspace(string Path, Exception Source) : WorkspaceError
{
    public override string Message => $"failed to remove workspace {Path}: {Source}";
}
