using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Memory;

public class OkfFrontmatter
{
    [YamlMember(Alias = "type")]
    public string ConceptType { get; set; } = "";
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Resource { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Timestamp { get; set; }
    public OpenSymphonyOkfMetadata? Opensymphony { get; set; }
    public Dictionary<string, object?> Extra { get; set; } = new();

    public static OkfFrontmatter New(string conceptType)
    {
        RequireOkfType(conceptType);
        return new OkfFrontmatter { ConceptType = conceptType };
    }
}

public class OpenSymphonyOkfMetadata
{
    public MemoryVisibility? Visibility { get; set; }
    public string? Kind { get; set; }
    public ulong? SchemaVersion { get; set; }
    public List<KnowledgeScope> ScopeRefs { get; set; } = new();
    public List<MemorySourceRef> SourceRefs { get; set; } = new();
    public List<OkfLink> Links { get; set; } = new();
    public List<OkfCitation> Citations { get; set; } = new();
    public object? DocsSync { get; set; }
    public Dictionary<string, object?> Extra { get; set; } = new();
}

public class OkfLink
{
    public string Target { get; set; } = "";
    public string? Label { get; set; }
}

public class OkfCitation
{
    public string Id { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Label { get; set; }
}

public class OkfConcept
{
    public OkfBundlePath Path { get; set; } = null!;
    public string Id { get; set; } = "";
    public OkfFrontmatter Frontmatter { get; set; } = null!;
    public string Body { get; set; } = "";
    public List<OkfLink> Links { get; set; } = new();
    public bool DerivedOpensymphony { get; set; }

    public static OkfConcept Create(string path, OkfFrontmatter frontmatter, string body)
    {
        RequireOkfType(frontmatter.ConceptType);
        var bundlePath = new OkfBundlePath(path);
        var concept = new OkfConcept
        {
            Path = bundlePath,
            Id = bundlePath.ConceptId(),
            Frontmatter = frontmatter,
            Body = body,
            Links = OkfMarkdown.ExtractMarkdownLinks(body),
            DerivedOpensymphony = false,
        };
        return concept;
    }
}

public class OkfBundlePath
{
    public string Relative { get; }

    public OkfBundlePath(string path)
    {
        var normalized = new List<string>();
        foreach (var part in path.Split('/', '\\'))
        {
            if (part == "." || part.Length == 0)
                continue;
            if (part == ".." || Path.IsPathRooted(part))
                throw MemoryError.InvalidInput($"OKF concept path `{path}` must be bundle-relative and contained");
            normalized.Add(part);
        }
        var relative = string.Join("/", normalized);
        var ext = Path.GetExtension(relative);
        if (relative.Length == 0 || !ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw MemoryError.InvalidInput($"OKF concept path `{path}` must name a Markdown file");
        Relative = relative;
    }

    public string AsPath() => Relative;

    public string ConceptId()
    {
        var withoutExt = Relative[..^Path.GetExtension(Relative).Length];
        return withoutExt.Replace('\\', '/');
    }

    public OkfReservedFile? ReservedFile()
    {
        var fileName = Path.GetFileName(Relative);
        return fileName switch
        {
            "index.md" => OkfReservedFile.Index,
            "log.md" => OkfReservedFile.Log,
            _ => null,
        };
    }
}

public enum OkfReservedFile
{
    Index,
    Log,
}

public class OkfExportReport
{
    public string OutputPath { get; set; } = "";
    public List<string> CopiedFiles { get; set; } = new();
    public List<string> SkippedPrivateFiles { get; set; } = new();
    public int FindingCount { get; set; }
}

public class OkfImportReport
{
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public List<string> CopiedFiles { get; set; } = new();
    public int FindingCount { get; set; }
    public MemoryReindexReport Reindex { get; set; } = new();
}

public static partial class OkfTypes
{
    public static void RequireOkfType(string? conceptType)
    {
        if (conceptType is null || conceptType.Trim().Length == 0)
            throw MemoryError.InvalidInput("OKF concept frontmatter requires non-empty `type`");
    }

    public static bool KnownOkfType(string conceptType) =>
        conceptType is "issue-capsule" or "milestone-memory-node" or "project-memory-node" or
            "area-memory-node" or "topic-doc" or "run-summary" or "code-context" or
            "repository-memory-node" or "reference";

    public static string? StringExtra(OkfFrontmatter fm, string key)
    {
        if (!fm.Extra.TryGetValue(key, out var value)) return null;
        return ValueAsString(value);
    }

    public static List<string> StringArrayExtra(OkfFrontmatter fm, string key)
    {
        if (!fm.Extra.TryGetValue(key, out var value)) return new();
        return value switch
        {
            string s => ValueAsString(s) is { } str ? new() { str } : new(),
            List<object?> list => list.Select(ValueAsString).Where(v => v != null).ToList()!,
            System.Collections.IList list => list.Cast<object?>().Select(ValueAsString).Where(v => v != null).ToList()!,
            _ => new(),
        };
    }

    public static string? ValueAsString(object? value)
    {
        return value switch
        {
            null => null,
            string s => s.Trim().Length == 0 ? null : s,
            int or long or float or double or decimal => value.ToString(),
            bool b => b ? "true" : "false",
            _ => null,
        };
    }

    public static MemoryVisibility? LegacyVisibility(OkfFrontmatter fm)
    {
        var v = StringExtra(fm, "visibility");
        return v switch
        {
            "public" => MemoryVisibility.Public,
            "private" => MemoryVisibility.Private,
            _ => null,
        };
    }

    public static void PushScope(List<KnowledgeScope> refs, KnowledgeScopeKind kind, string? id, string? label)
    {
        if (id is null || id.Trim().Length == 0) return;
        PushScopeRef(refs, new KnowledgeScope { Kind = kind, Id = id, Label = label });
    }

    public static void PushScopeRef(List<KnowledgeScope> refs, KnowledgeScope scope)
    {
        if (!refs.Any(r => r.Kind == scope.Kind && r.Id == scope.Id))
            refs.Add(scope);
    }

    public static void PushSourceRef(List<MemorySourceRef> refs, MemorySourceRef sourceRef)
    {
        var existing = refs.FirstOrDefault(r => r.Kind == sourceRef.Kind && r.Id == sourceRef.Id);
        if (existing != null)
        {
            if (existing.Url is null)
                existing.Url = sourceRef.Url;
        }
        else
        {
            refs.Add(sourceRef);
        }
    }

    public static List<MemorySourceRef> LegacySourceRefs(OkfFrontmatter fm) =>
        LegacySourceRefsFromExtra(fm.Extra);

    public static List<MemorySourceRef> LegacySourceRefsFromExtra(Dictionary<string, object?> extra)
    {
        var refs = new List<MemorySourceRef>();
        if (extra.TryGetValue("source_refs", out var sr) && sr is Dictionary<object, object> sourceRefs)
        {
            foreach (var (key, value) in sourceRefs)
            {
                var kind = ValueAsString(key);
                if (kind is null) continue;
                switch (value)
                {
                    case List<object?> list:
                        foreach (var v in list)
                        {
                            var token = ValueAsString(v);
                            if (token != null) PushSourceRef(refs, SourceRefFromToken(kind, token));
                        }
                        break;
                    default:
                        var token = ValueAsString(value);
                        if (token != null) PushSourceRef(refs, SourceRefFromToken(kind, token));
                        break;
                }
            }
        }
        if (extra.TryGetValue("prs", out var prsVal) && prsVal is List<object?> prs)
        {
            foreach (var pr in prs.OfType<Dictionary<object, object>>())
            {
                if (pr.TryGetValue("number", out var numVal))
                {
                    var number = ValueAsString(numVal);
                    if (number != null)
                    {
                        var url = pr.TryGetValue("url", out var urlVal) ? ValueAsString(urlVal) : null;
                        PushSourceRef(refs, new MemorySourceRef { Kind = "github_pr", Id = number, Url = url });
                    }
                }
                if (pr.TryGetValue("merge_sha", out var shaVal))
                {
                    var sha = ValueAsString(shaVal);
                    if (sha != null)
                        PushSourceRef(refs, new MemorySourceRef { Kind = "github_merge_sha", Id = sha, Url = null });
                }
            }
        }
        return refs;
    }

    public static MemorySourceRef SourceRefFromToken(string kind, string token)
    {
        if (token.StartsWith("github:pr:"))
            return new MemorySourceRef { Kind = "github_pr", Id = token["github:pr:".Length..], Url = null };
        if (token.StartsWith("github:merge:"))
            return new MemorySourceRef { Kind = "github_merge_sha", Id = token["github:merge:".Length..], Url = null };
        if (token.StartsWith("linear:"))
            return new MemorySourceRef { Kind = kind, Id = token["linear:".Length..], Url = null };
        return new MemorySourceRef { Kind = kind, Id = token, Url = null };
    }

    public static OpenSymphonyOkfMetadata LegacyFrontmatterToOpensymphonyMetadata(OkfFrontmatter fm)
    {
        var metadata = new OpenSymphonyOkfMetadata
        {
            Visibility = LegacyVisibility(fm),
            Kind = fm.ConceptType.Replace('-', '_'),
            SchemaVersion = 1,
            DocsSync = fm.Extra.TryGetValue("docs_sync", out var ds) ? ds : null,
        };

        PushScope(metadata.ScopeRefs, KnowledgeScopeKind.WorkItem, StringExtra(fm, "issue"), fm.Title);
        PushScope(metadata.ScopeRefs, KnowledgeScopeKind.Milestone,
            StringExtra(fm, "milestone_id") ?? StringExtra(fm, "milestone"),
            StringExtra(fm, "milestone"));
        PushScope(metadata.ScopeRefs, KnowledgeScopeKind.Project,
            StringExtra(fm, "project_id") ?? StringExtra(fm, "project"),
            StringExtra(fm, "project"));

        foreach (var area in StringArrayExtra(fm, "areas").Concat(StringExtra(fm, "area") is { } a ? new[] { a } : Array.Empty<string>()))
        {
            PushScope(metadata.ScopeRefs, KnowledgeScopeKind.Area, area, area);
        }

        PushScope(metadata.ScopeRefs, KnowledgeScopeKind.Repository,
            StringExtra(fm, "repository") ?? StringExtra(fm, "repo"),
            StringExtra(fm, "repository") ?? StringExtra(fm, "repo"));

        if (StringExtra(fm, "issue") is { } issue)
        {
            metadata.SourceRefs.Add(new MemorySourceRef
            {
                Kind = "linear_issue",
                Id = issue,
                Url = StringExtra(fm, "linear_url"),
            });
        }

        foreach (var sourceRef in LegacySourceRefs(fm))
            PushSourceRef(metadata.SourceRefs, sourceRef);

        return metadata;
    }

    public static void RemoveRepresentedLegacyOpensymphonyFields(Dictionary<string, object?> extra, OpenSymphonyOkfMetadata metadata)
    {
        if (metadata.Visibility != null) extra.Remove("visibility");
        if (metadata.DocsSync != null) extra.Remove("docs_sync");
        if (HasScopeRef(metadata, KnowledgeScopeKind.WorkItem)) extra.Remove("issue");
        if (HasScopeRef(metadata, KnowledgeScopeKind.Milestone)) { extra.Remove("milestone"); extra.Remove("milestone_id"); }
        if (HasScopeRef(metadata, KnowledgeScopeKind.Project)) { extra.Remove("project"); extra.Remove("project_id"); }
        if (HasScopeRef(metadata, KnowledgeScopeKind.Area)) { extra.Remove("area"); extra.Remove("areas"); }
        if (HasScopeRef(metadata, KnowledgeScopeKind.Repository)) { extra.Remove("repo"); extra.Remove("repository"); }
        if (metadata.SourceRefs.Any(s => s.Kind == "linear_issue" && s.Url != null))
            extra.Remove("linear_url");

        var legacyRefs = LegacySourceRefsFromExtra(extra);
        if (legacyRefs.Count > 0 && legacyRefs.All(l => SourceRefIsRepresented(metadata, l)))
        {
            extra.Remove("prs");
            extra.Remove("source_refs");
        }
    }

    public static bool HasScopeRef(OpenSymphonyOkfMetadata metadata, KnowledgeScopeKind kind) =>
        metadata.ScopeRefs.Any(s => s.Kind == kind);

    public static bool SourceRefIsRepresented(OpenSymphonyOkfMetadata metadata, MemorySourceRef legacyRef) =>
        metadata.SourceRefs.Any(s => s.Kind == legacyRef.Kind && s.Id == legacyRef.Id &&
            (legacyRef.Url == null || s.Url == legacyRef.Url));
}
