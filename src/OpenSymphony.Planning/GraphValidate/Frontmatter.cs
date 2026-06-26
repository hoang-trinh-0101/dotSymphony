using System.IO;
using OpenSymphony.Domain;
using YamlDotNet.Serialization;

namespace OpenSymphony.Planning.GraphValidate;

using OpenSymphony.Planning.Generator;

public sealed class TaskFrontmatter
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Milestone { get; set; }
    public long? Priority { get; set; }
    public long? Estimate { get; set; }
    public List<string> BlockedBy { get; set; } = new();
    public List<string> Blocks { get; set; } = new();
    public string? Parent { get; set; }
    public List<string> Areas { get; set; } = new();
    public Dictionary<string, object?> Extra { get; set; } = new();
}

public sealed record ParsedTaskFile(TaskFrontmatter Frontmatter, string Body);

public enum TaskFrontmatterErrorKind { Io, MissingFrontmatter, Yaml }

public sealed record TaskFrontmatterError(TaskFrontmatterErrorKind Kind, string Path, string Message)
{
    public override string ToString() => Kind switch
    {
        TaskFrontmatterErrorKind.Io => $"failed to read task file {Path}: {Message}",
        TaskFrontmatterErrorKind.MissingFrontmatter => $"task file {Path} is missing YAML frontmatter",
        TaskFrontmatterErrorKind.Yaml => $"failed to parse YAML frontmatter in {Path}: {Message}",
        _ => Message,
    };
}

public static class FrontmatterParser
{
    public static Result<ParsedTaskFile, TaskFrontmatterError> ParseTaskFile(string path)
    {
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ParsedTaskFile, TaskFrontmatterError>.Err(
                new TaskFrontmatterError(TaskFrontmatterErrorKind.Io, path, ex.Message));
        }
        return ParseTaskText(raw, path);
    }

    public static Result<ParsedTaskFile, TaskFrontmatterError> ParseTaskText(string raw, string path)
    {
        var trimmed = raw.StartsWith('\uFEFF') ? raw[1..] : raw;
        var lines = trimmed.Split('\n');
        var first = lines.Length > 0 ? lines[0].TrimEnd() : "";
        if (first != "---")
            return Result<ParsedTaskFile, TaskFrontmatterError>.Err(
                new TaskFrontmatterError(TaskFrontmatterErrorKind.MissingFrontmatter, path, ""));

        var yamlLines = new List<string>();
        int? closingLineIdx = null;
        for (var idx = 1; idx < lines.Length; idx++)
        {
            var normalized = lines[idx].TrimEnd();
            if (normalized == "---" || normalized == "...")
            {
                closingLineIdx = idx;
                break;
            }
            yamlLines.Add(lines[idx]);
        }

        if (closingLineIdx is null)
            return Result<ParsedTaskFile, TaskFrontmatterError>.Err(
                new TaskFrontmatterError(TaskFrontmatterErrorKind.MissingFrontmatter, path, ""));

        var yamlText = string.Join("\n", yamlLines);
        TaskFrontmatter frontmatter;
        try
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            var dict = deserializer.Deserialize<Dictionary<string, object?>>(yamlText) ?? new();
            frontmatter = new TaskFrontmatter();
            if (dict.TryGetValue("id", out var id) && id is string idStr) frontmatter.Id = idStr;
            if (dict.TryGetValue("title", out var title) && title is string titleStr) frontmatter.Title = titleStr;
            if (dict.TryGetValue("milestone", out var ms) && ms is string msStr) frontmatter.Milestone = msStr;
            if (dict.TryGetValue("priority", out var pri) && pri is long priVal) frontmatter.Priority = priVal;
            if (dict.TryGetValue("estimate", out var est) && est is long estVal) frontmatter.Estimate = estVal;
            if (dict.TryGetValue("blockedBy", out var bb) && bb is List<object?> bbList)
                frontmatter.BlockedBy = bbList.Select(x => x?.ToString() ?? "").ToList();
            else if (dict.TryGetValue("blocked_by", out var bb2) && bb2 is List<object?> bb2List)
                frontmatter.BlockedBy = bb2List.Select(x => x?.ToString() ?? "").ToList();
            if (dict.TryGetValue("blocks", out var bl) && bl is List<object?> blList)
                frontmatter.Blocks = blList.Select(x => x?.ToString() ?? "").ToList();
            if (dict.TryGetValue("parent", out var par) && par is string parStr) frontmatter.Parent = parStr;
            if (dict.TryGetValue("areas", out var ar) && ar is List<object?> arList)
                frontmatter.Areas = arList.Select(x => x?.ToString() ?? "").ToList();
            // Preserve unknown keys
            var knownKeys = new HashSet<string> { "id", "title", "milestone", "priority", "estimate", "blockedBy", "blocked_by", "blocks", "parent", "areas" };
            foreach (var kv in dict)
                if (!knownKeys.Contains(kv.Key))
                    frontmatter.Extra[kv.Key] = kv.Value;
        }
        catch (Exception ex)
        {
            return Result<ParsedTaskFile, TaskFrontmatterError>.Err(
                new TaskFrontmatterError(TaskFrontmatterErrorKind.Yaml, path, ex.Message));
        }

        var body = closingLineIdx.Value + 1 < lines.Length
            ? string.Join("\n", lines[(closingLineIdx.Value + 1)..])
            : "";

        return Result<ParsedTaskFile, TaskFrontmatterError>.Ok(new ParsedTaskFile(frontmatter, body));
    }

    public static Result<TaskFrontmatter, TaskFrontmatterError> ReadTaskFrontmatterOrDefault(string path)
    {
        var result = ParseTaskFile(path);
        if (result.IsOk) return Result<TaskFrontmatter, TaskFrontmatterError>.Ok(result.Value.Frontmatter);
        if (result.Error.Kind == TaskFrontmatterErrorKind.Io && result.Error.Message.Contains("Could not find"))
            return Result<TaskFrontmatter, TaskFrontmatterError>.Ok(new TaskFrontmatter());
        return Result<TaskFrontmatter, TaskFrontmatterError>.Err(result.Error);
    }

    public static TaskId? TaskIdFrom(TaskFrontmatter frontmatter)
    {
        if (frontmatter.Id is { } id && !string.IsNullOrEmpty(id))
            return new TaskId(id);
        return null;
    }
}
