using System.IO;
using OpenSymphony.Domain;
using YamlDotNet.Serialization;

namespace OpenSymphony.Planning.GraphValidate;

using OpenSymphony.Planning.Generator;

public sealed record TaskPackageManifestFile(
    string PlanningWave,
    string TasksDir,
    List<string> Milestones,
    List<ManifestTaskEntry> Tasks);

public sealed record ManifestTaskEntry(string Id, string File);

public enum ManifestValidatorErrorKind { Io, Yaml }

public sealed record ManifestValidatorError(ManifestValidatorErrorKind Kind, string Path, string Message)
{
    public override string ToString() => Kind switch
    {
        ManifestValidatorErrorKind.Io => $"failed to read manifest {Path}: {Message}",
        ManifestValidatorErrorKind.Yaml => $"failed to parse manifest {Path}: {Message}",
        _ => Message,
    };
}

public static class ManifestValidator
{
    public static Result<TaskPackageManifestFile, ManifestValidatorError> LoadManifest(string path)
    {
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<TaskPackageManifestFile, ManifestValidatorError>.Err(
                new ManifestValidatorError(ManifestValidatorErrorKind.Io, path, ex.Message));
        }

        try
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            var dict = deserializer.Deserialize<Dictionary<string, object?>>(raw) ?? new();

            var planningWave = dict.TryGetValue("planningWave", out var pw) && pw is string pwStr ? pwStr : "";
            var tasksDir = dict.TryGetValue("tasksDir", out var td) && td is string tdStr ? tdStr : "";
            var milestones = new List<string>();
            if (dict.TryGetValue("milestones", out var ms) && ms is List<object?> msList)
                milestones = msList.Select(x => x?.ToString() ?? "").ToList();
            var tasks = new List<ManifestTaskEntry>();
            if (dict.TryGetValue("tasks", out var t) && t is List<object?> tList)
            {
                foreach (var item in tList)
                {
                    if (item is Dictionary<object, object?> taskDict)
                    {
                        var id = taskDict.TryGetValue("id", out var idVal) ? idVal?.ToString() ?? "" : "";
                        var file = taskDict.TryGetValue("file", out var fileVal) ? fileVal?.ToString() ?? "" : "";
                        tasks.Add(new ManifestTaskEntry(id, file));
                    }
                }
            }

            return Result<TaskPackageManifestFile, ManifestValidatorError>.Ok(
                new TaskPackageManifestFile(planningWave, tasksDir, milestones, tasks));
        }
        catch (Exception ex)
        {
            return Result<TaskPackageManifestFile, ManifestValidatorError>.Err(
                new ManifestValidatorError(ManifestValidatorErrorKind.Yaml, path, ex.Message));
        }
    }

    public static Result<ManifestValidationResult, ManifestValidatorError> Validate(string manifestPath, string repoRoot)
    {
        var manifestResult = LoadManifest(manifestPath);
        if (manifestResult.IsErr)
            return Result<ManifestValidationResult, ManifestValidatorError>.Err(manifestResult.Error);
        return Result<ManifestValidationResult, ManifestValidatorError>.Ok(
            ValidateAgainstRepoRoot(manifestResult.Value, repoRoot));
    }

    public static ManifestValidationResult ValidateAgainstRepoRoot(TaskPackageManifestFile manifest, string repoRoot)
    {
        var result = new ManifestValidationResult(
            manifest.PlanningWave,
            new List<TaskId>(),
            new List<MissingTaskFile>(),
            new List<InvalidTaskFile>(),
            new List<UnknownMilestone>(),
            new List<UnknownDependency>(),
            new List<List<TaskId>>(),
            new List<SelfBlock>(),
            new List<TaskId>());

        var seenIds = new SortedSet<TaskId>();
        var entries = new List<(TaskId, TaskFrontmatter)>();
        var milestoneSet = new HashSet<string>(manifest.Milestones);

        foreach (var entry in manifest.Tasks)
        {
            var id = new TaskId(entry.Id);
            if (!seenIds.Add(id))
            {
                result = result with { DuplicateTaskIds = [..result.DuplicateTaskIds, id] };
                continue;
            }
            result = result with { DeclaredTaskIds = [..result.DeclaredTaskIds, id] };
            var path = Path.Combine(repoRoot, entry.File.Replace('/', Path.DirectorySeparatorChar));
            var parseResult = FrontmatterParser.ParseTaskFile(path);
            if (parseResult.IsOk)
            {
                entries.Add((id, parseResult.Value.Frontmatter));
            }
            else if (parseResult.Error.Kind == TaskFrontmatterErrorKind.Io && IsNotFound(parseResult.Error))
            {
                result = result with { MissingTaskFiles = [..result.MissingTaskFiles, new MissingTaskFile(id, entry.File)] };
            }
            else
            {
                result = result with { InvalidTaskFiles = [..result.InvalidTaskFiles, new InvalidTaskFile(id, entry.File, parseResult.Error.ToString())] };
            }
        }

        var idSet = new SortedSet<TaskId>(result.DeclaredTaskIds);
        var adjacency = new SortedDictionary<TaskId, SortedSet<TaskId>>();

        foreach (var (taskId, frontmatter) in entries)
        {
            var milestone = frontmatter.Milestone ?? "";
            if (!milestoneSet.Contains(milestone) && frontmatter.Milestone is { } declared)
            {
                result = result with { UnknownMilestones = [..result.UnknownMilestones, new UnknownMilestone(taskId, declared)] };
            }
            foreach (var dep in frontmatter.BlockedBy)
            {
                if (dep == taskId.Value)
                {
                    result = result with { SelfBlocks = [..result.SelfBlocks, new SelfBlock(taskId)] };
                }
                else if (!idSet.Contains(new TaskId(dep)))
                {
                    result = result with { UnknownDependencies = [..result.UnknownDependencies, new UnknownDependency(taskId, new TaskId(dep))] };
                }
                else
                {
                    if (!adjacency.TryGetValue(taskId, out var set))
                    {
                        set = new SortedSet<TaskId>();
                        adjacency[taskId] = set;
                    }
                    set.Add(new TaskId(dep));
                }
            }
        }

        var cycles = CreationOrderCycles(adjacency, idSet);
        result = result with { CreationOrderCycles = cycles };

        // Stable sort
        var missingSorted = result.MissingTaskFiles.OrderBy(x => x.TaskId, TaskIdComparer.Instance).ToList();
        var invalidSorted = result.InvalidTaskFiles.OrderBy(x => x.TaskId, TaskIdComparer.Instance).ToList();
        var unknownMsSorted = result.UnknownMilestones.OrderBy(x => x.TaskId, TaskIdComparer.Instance).ToList();
        var unknownDepsSorted = result.UnknownDependencies.OrderBy(x => x.FromTaskId, TaskIdComparer.Instance).ToList();
        var selfBlocksSorted = result.SelfBlocks.OrderBy(x => x.TaskId, TaskIdComparer.Instance).ToList();

        return result with
        {
            MissingTaskFiles = missingSorted,
            InvalidTaskFiles = invalidSorted,
            UnknownMilestones = unknownMsSorted,
            UnknownDependencies = unknownDepsSorted,
            SelfBlocks = selfBlocksSorted,
        };
    }

    private static bool IsNotFound(TaskFrontmatterError error) =>
        error.Message.Contains("Could not find") || error.Message.Contains("not found") || error.Message.Contains("Cannot find");

    private static List<List<TaskId>> CreationOrderCycles(SortedDictionary<TaskId, SortedSet<TaskId>> adjacency, SortedSet<TaskId> nodes)
    {
        var visited = new HashSet<TaskId>();
        var onStack = new HashSet<TaskId>();
        var stack = new List<TaskId>();
        var seenCycles = new HashSet<List<TaskId>>(new TaskIdListComparer());
        var collected = new List<List<TaskId>>();

        foreach (var entry in nodes)
        {
            if (!visited.Contains(entry))
                DfsCycle(entry, adjacency, visited, onStack, stack, seenCycles, collected);
        }
        return collected;
    }

    private static void DfsCycle(TaskId node, SortedDictionary<TaskId, SortedSet<TaskId>> adjacency,
        HashSet<TaskId> visited, HashSet<TaskId> onStack, List<TaskId> stack,
        HashSet<List<TaskId>> seenCycles, List<List<TaskId>> collected)
    {
        visited.Add(node);
        onStack.Add(node);
        stack.Add(node);

        if (adjacency.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                if (!visited.Contains(dep))
                {
                    DfsCycle(dep, adjacency, visited, onStack, stack, seenCycles, collected);
                }
                else if (onStack.Contains(dep))
                {
                    var startIdx = stack.IndexOf(dep);
                    if (startIdx >= 0)
                    {
                        var cycle = stack.Skip(startIdx).ToList();
                        cycle.Sort();
                        if (seenCycles.Add(cycle))
                            collected.Add(cycle);
                    }
                }
            }
        }

        onStack.Remove(node);
        stack.RemoveAt(stack.Count - 1);
    }

    private sealed class TaskIdComparer : IComparer<TaskId>
    {
        public static readonly TaskIdComparer Instance = new();
        public int Compare(TaskId? x, TaskId? y) =>
            (x, y) switch
            {
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                _ => x.CompareTo(y),
            };
    }

    private sealed class TaskIdListComparer : IEqualityComparer<List<TaskId>>
    {
        public bool Equals(List<TaskId>? x, List<TaskId>? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.SequenceEqual(y);
        }
        public int GetHashCode(List<TaskId> obj) => obj.Aggregate(0, (h, t) => h ^ t.GetHashCode());
    }
}
