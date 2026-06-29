using OpenSymphony.Memory;

namespace OpenSymphony.Cli.Memory;

/// <summary>
/// Helper for recording memory initialization changes.
/// Ported from memory_init_summary.rs
/// </summary>
public static class MemoryInitSummary
{
    public static void RecordChanges(
        MemoryInitApplyReport report,
        string targetRepo,
        List<string> created,
        List<string> updated,
        List<string> unchanged)
    {
        RecordChange(
            RelativePathForSummary(targetRepo, report.ConfigPath),
            report.Config,
            created,
            updated,
            unchanged);

        RecordChange(
            RelativePathForSummary(targetRepo, report.GitignorePath),
            report.Gitignore,
            created,
            updated,
            unchanged);
    }

    public static (List<string> Created, List<string> Updated, List<string> Unchanged) GetChangeLists(
        MemoryInitApplyReport report,
        string targetRepo)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var unchanged = new List<string>();

        RecordChanges(report, targetRepo, created, updated, unchanged);

        return (created, updated, unchanged);
    }

    private static void RecordChange(
        string path,
        MemoryInitFileChange change,
        List<string> created,
        List<string> updated,
        List<string> unchanged)
    {
        switch (change)
        {
            case MemoryInitFileChange.Created:
                created.Add(path);
                break;
            case MemoryInitFileChange.Updated:
                updated.Add(path);
                break;
            case MemoryInitFileChange.Unchanged:
                unchanged.Add(path);
                break;
        }
    }

    private static string RelativePathForSummary(string root, string path)
    {
        if (path.StartsWith(root, StringComparison.Ordinal))
        {
            return path[root.Length..].TrimStart('/', '\\');
        }
        return path;
    }
}