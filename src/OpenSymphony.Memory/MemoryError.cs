using System.IO;

namespace OpenSymphony.Memory;

public enum MemoryErrorKind
{
    ReadFile,
    CreateDir,
    WriteFile,
    ParseYaml,
    OkfMissingFrontmatter,
    OkfUnterminatedFrontmatter,
    Json,
    DuckDb,
    ResolvePath,
    Linear,
    InvalidInput,
    PathOutsideRepo,
    PathOutsideBundle,
    OkfExportStagingCleanup,
}

public class MemoryError : Exception
{
    public MemoryErrorKind Kind { get; }
    public string? Path { get; }
    public string? RepoRoot { get; set; }
    public string? BundleRoot { get; set; }
    public MemoryError? SourceError { get; set; }
    public MemoryError? CleanupError { get; set; }

    public MemoryError(MemoryErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public MemoryError(MemoryErrorKind kind, string message, string? path)
        : base(message)
    {
        Kind = kind;
        Path = path;
    }

    public static MemoryError ReadFile(string path, Exception source) =>
        new(MemoryErrorKind.ReadFile, $"failed to read {path}: {source.Message}", path);

    public static MemoryError CreateDir(string path, Exception source) =>
        new(MemoryErrorKind.CreateDir, $"failed to create {path}: {source.Message}", path);

    public static MemoryError WriteFile(string path, Exception source) =>
        new(MemoryErrorKind.WriteFile, $"failed to write {path}: {source.Message}", path);

    public static MemoryError ParseYaml(string path, string message) =>
        new(MemoryErrorKind.ParseYaml, $"failed to parse YAML from {path}: {message}", path);

    public static MemoryError OkfMissingFrontmatter(string path) =>
        new(MemoryErrorKind.OkfMissingFrontmatter, $"{path} lacks OKF YAML frontmatter", path);

    public static MemoryError OkfUnterminatedFrontmatter(string path) =>
        new(MemoryErrorKind.OkfUnterminatedFrontmatter, $"{path} has unterminated OKF YAML frontmatter", path);

    public static MemoryError Json(string message) =>
        new(MemoryErrorKind.Json, $"failed to encode JSON: {message}");

    public static MemoryError DuckDb(string path, string message) =>
        new(MemoryErrorKind.DuckDb, $"failed to update DuckDB index {path}: {message}", path);

    public static MemoryError ResolvePath(string path, Exception source) =>
        new(MemoryErrorKind.ResolvePath, $"failed to resolve {path}: {source.Message}", path);

    public static MemoryError Linear(string message) =>
        new(MemoryErrorKind.Linear, $"Linear operation failed: {message}");

    public static MemoryError InvalidInput(string message) =>
        new(MemoryErrorKind.InvalidInput, message);

    public static MemoryError PathOutsideRepo(string path, string repoRoot) =>
        new(MemoryErrorKind.PathOutsideRepo, $"{path} is outside the repository root {repoRoot}", path) { RepoRoot = repoRoot };

    public static MemoryError PathOutsideBundle(string path, string bundleRoot) =>
        new(MemoryErrorKind.PathOutsideBundle, $"{path} is outside the OKF bundle root {bundleRoot}", path) { BundleRoot = bundleRoot };

    public static MemoryError OkfExportStagingCleanup(string path, MemoryError source, MemoryError cleanup) =>
        new(MemoryErrorKind.OkfExportStagingCleanup,
            $"{source.Message}; additionally failed to remove OKF export staging directory `{path}` after the export failure: {cleanup.Message}; remove the staging directory manually",
            path)
        { SourceError = source, CleanupError = cleanup };
}
