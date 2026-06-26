using OpenSymphony.Domain;

namespace OpenSymphony.Workspace;

// ht: static helpers mirror the Rust free functions in paths.rs.
public static class WorkspacePaths
{
    public static Result<string, WorkspaceError> SanitizeWorkspaceKey(string identifier)
    {
        // ht: Rust checks identifier.trim().is_empty() first.
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Result<string, WorkspaceError>.Err(new EmptyIdentifier());
        }

        var key = WorkspaceKey.Sanitize(identifier);

        // ht: post-sanitization, only empty / "." / ".." are non-Normal single
        //   components — matches Rust's Component::Normal + single-component
        //   check without reimplementing Path::components().
        if (key is "" or "." or "..")
        {
            return Result<string, WorkspaceError>.Err(new InvalidWorkspaceKey(key));
        }

        return Result<string, WorkspaceError>.Ok(key);
    }

    public static Result<string, WorkspaceError> WorkspacePathForRoot(string root, string issueIdentifier)
    {
        var normalizedRoot = NormalizeAbsolutePath(root);
        if (normalizedRoot.IsErr) return normalizedRoot;

        var key = SanitizeWorkspaceKey(issueIdentifier);
        if (key.IsErr) return key;

        return ResolvePathWithinRoot(normalizedRoot.Value, key.Value);
    }

    public static Result<string, WorkspaceError> ResolvePathWithinRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeAbsolutePath(root);
        if (normalizedRoot.IsErr) return normalizedRoot;

        // ht: candidate absolute → normalize candidate; else normalize root.Join(candidate).
        Result<string, WorkspaceError> normalizedCandidate =
            Path.IsPathFullyQualified(candidate)
                ? NormalizeAbsolutePath(candidate)
                : NormalizeAbsolutePath(Path.Join(root, candidate));
        if (normalizedCandidate.IsErr) return normalizedCandidate;

        var normRoot = normalizedRoot.Value;
        var normCandidate = normalizedCandidate.Value;

        // ht: component-aware containment. Exact match OR candidate starts with
        //   root + separator prevents "rootX" falsely containing "root".
        if (normCandidate == normRoot ||
            normCandidate.StartsWith(normRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Result<string, WorkspaceError>.Ok(normCandidate);
        }

        return Result<string, WorkspaceError>.Err(new PathEscape(normRoot, normCandidate));
    }

    public static Result<string, WorkspaceError> NormalizeAbsolutePath(string path)
    {
        // ht: Path.IsPathFullyQualified mirrors Path::is_absolute on Windows
        //   (both reject relative paths). Path.GetFullPath does lexical "."/".."
        //   resolution matching Rust's manual component walk; no symlink
        //   resolution, same as Rust.
        if (!Path.IsPathFullyQualified(path))
        {
            return Result<string, WorkspaceError>.Err(new RootNotAbsolute(path));
        }
        return Result<string, WorkspaceError>.Ok(Path.GetFullPath(path));
    }
}
