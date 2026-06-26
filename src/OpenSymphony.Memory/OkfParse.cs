using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSymphony.Memory;

public partial class OkfParse
{
    public const string OkfFrontmatterDelimiter = "---";

    public static string SerializeFrontmatter(OkfFrontmatter frontmatter)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        var yaml = serializer.Serialize(frontmatter);
        return yaml;
    }

    public static string SerializeFrontmatterCanonical(OkfFrontmatter frontmatter)
    {
        var dict = FrontmatterToCanonicalDictionary(frontmatter);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        var yaml = serializer.Serialize(dict);
        return yaml;
    }

    public static Dictionary<string, object?> FrontmatterToCanonicalDictionary(OkfFrontmatter frontmatter)
    {
        var dict = new Dictionary<string, object?>
        {
            ["type"] = frontmatter.ConceptType,
        };
        if (frontmatter.Title != null) dict["title"] = frontmatter.Title;
        if (frontmatter.Description != null) dict["description"] = frontmatter.Description;
        if (frontmatter.Resource != null) dict["resource"] = frontmatter.Resource;
        if (frontmatter.Tags.Count > 0) dict["tags"] = frontmatter.Tags.ToList();
        if (frontmatter.Timestamp != null) dict["timestamp"] = frontmatter.Timestamp;
        if (frontmatter.Opensymphony != null)
        {
            var osDict = new Dictionary<string, object?>();
            if (frontmatter.Opensymphony.Visibility != null)
                osDict["visibility"] = frontmatter.Opensymphony.Visibility.Value == MemoryVisibility.Public ? "public" : "private";
            if (frontmatter.Opensymphony.Kind != null) osDict["kind"] = frontmatter.Opensymphony.Kind;
            if (frontmatter.Opensymphony.SchemaVersion != null)
                osDict["schema_version"] = frontmatter.Opensymphony.SchemaVersion.Value;
            if (frontmatter.Opensymphony.ScopeRefs.Count > 0)
                osDict["scope_refs"] = frontmatter.Opensymphony.ScopeRefs.Select(s => new
                {
                    kind = s.Kind.ToString().ToLowerInvariant(),
                    id = s.Id,
                    label = s.Label,
                }).ToList();
            if (frontmatter.Opensymphony.SourceRefs.Count > 0)
                osDict["source_refs"] = frontmatter.Opensymphony.SourceRefs.Select(s => new
                {
                    kind = s.Kind,
                    id = s.Id,
                    url = s.Url,
                }).ToList();
            if (frontmatter.Opensymphony.Links.Count > 0)
                osDict["links"] = frontmatter.Opensymphony.Links.Select(l => new { target = l.Target, label = l.Label }).ToList();
            if (frontmatter.Opensymphony.Citations.Count > 0)
                osDict["citations"] = frontmatter.Opensymphony.Citations.Select(c => new { id = c.Id, target = c.Target, label = c.Label }).ToList();
            if (frontmatter.Opensymphony.DocsSync != null) osDict["docs_sync"] = frontmatter.Opensymphony.DocsSync;
            foreach (var (k, v) in frontmatter.Opensymphony.Extra)
                if (!osDict.ContainsKey(k)) osDict[k] = v;
            dict["opensymphony"] = osDict;
        }
        foreach (var (k, v) in frontmatter.Extra)
            if (!dict.ContainsKey(k)) dict[k] = v;
        return dict;
    }

    public static (OkfFrontmatter Frontmatter, string Body) SplitFrontmatter(string contents)
    {
        var lines = contents.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != OkfFrontmatterDelimiter)
            return (new OkfFrontmatter(), contents);
        int endIdx = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == OkfFrontmatterDelimiter)
            {
                endIdx = i;
                break;
            }
        }
        if (endIdx < 0)
            return (new OkfFrontmatter(), contents);
        var yaml = string.Join('\n', lines[1..endIdx]);
        var body = string.Join('\n', lines[(endIdx + 1)..]);
        // Preserve trailing newline behavior
        if (body.Length > 0 && !contents.EndsWith('\n'))
            body = body.TrimEnd('\n');
        var frontmatter = ParseFrontmatter(yaml);
        return (frontmatter, body);
    }

    public static OkfFrontmatter ParseFrontmatter(string yaml)
    {
        if (yaml.Trim().Length == 0)
            return new OkfFrontmatter();
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var dict = deserializer.Deserialize<Dictionary<string, object?>>(yaml) ?? new();
        return DictionaryToFrontmatter(dict);
    }

    public static OkfFrontmatter DictionaryToFrontmatter(Dictionary<string, object?> dict)
    {
        var fm = new OkfFrontmatter();
        if (dict.TryGetValue("type", out var typeVal))
            fm.ConceptType = OkfTypes.ValueAsString(typeVal) ?? "";
        if (dict.TryGetValue("title", out var titleVal))
            fm.Title = OkfTypes.ValueAsString(titleVal);
        if (dict.TryGetValue("description", out var descVal))
            fm.Description = OkfTypes.ValueAsString(descVal);
        if (dict.TryGetValue("resource", out var resVal))
            fm.Resource = OkfTypes.ValueAsString(resVal);
        if (dict.TryGetValue("timestamp", out var tsVal))
            fm.Timestamp = OkfTypes.ValueAsString(tsVal);
        if (dict.TryGetValue("tags", out var tagsVal))
            fm.Tags = ToStringList(tagsVal);
        if (dict.TryGetValue("opensymphony", out var osVal) && osVal is Dictionary<object, object> osDict)
        {
            fm.Opensymphony = DictionaryToOpensymphonyMetadata(osDict);
        }
        // Collect extra fields
        var knownKeys = new HashSet<string> { "type", "title", "description", "resource", "tags", "timestamp", "opensymphony" };
        foreach (var (k, v) in dict)
        {
            if (!knownKeys.Contains(k))
                fm.Extra[k] = v;
        }
        return fm;
    }

    public static OpenSymphonyOkfMetadata DictionaryToOpensymphonyMetadata(Dictionary<object, object> dict)
    {
        var meta = new OpenSymphonyOkfMetadata();
        string? Get(string key)
        {
            foreach (var k in dict.Keys)
            {
                if (k.ToString() == key) return OkfTypes.ValueAsString(dict[k]);
            }
            return null;
        }
        var vis = Get("visibility");
        if (vis == "public") meta.Visibility = MemoryVisibility.Public;
        else if (vis == "private") meta.Visibility = MemoryVisibility.Private;
        meta.Kind = Get("kind");
        if (Get("schema_version") is { } sv && ulong.TryParse(sv, out var svVal)) meta.SchemaVersion = svVal;
        if (dict.Keys.FirstOrDefault(k => k.ToString() == "scope_refs") is { } srKey &&
            dict[srKey] is List<object?> srList)
        {
            foreach (var item in srList.OfType<Dictionary<object, object>>())
            {
                var scope = new KnowledgeScope();
                if (item.Keys.FirstOrDefault(k => k.ToString() == "kind") is { } kKey &&
                    OkfTypes.ValueAsString(item[kKey]) is { } kindStr &&
                    Enum.TryParse<KnowledgeScopeKind>(kindStr, true, out var kind))
                    scope.Kind = kind;
                if (item.Keys.FirstOrDefault(k => k.ToString() == "id") is { } idKey)
                    scope.Id = OkfTypes.ValueAsString(item[idKey]) ?? "";
                if (item.Keys.FirstOrDefault(k => k.ToString() == "label") is { } lblKey)
                    scope.Label = OkfTypes.ValueAsString(item[lblKey]);
                meta.ScopeRefs.Add(scope);
            }
        }
        if (dict.Keys.FirstOrDefault(k => k.ToString() == "source_refs") is { } srcKey &&
            dict[srcKey] is List<object?> srcList)
        {
            foreach (var item in srcList.OfType<Dictionary<object, object>>())
            {
                var sr = new MemorySourceRef();
                if (item.Keys.FirstOrDefault(k => k.ToString() == "kind") is { } kKey)
                    sr.Kind = OkfTypes.ValueAsString(item[kKey]) ?? "";
                if (item.Keys.FirstOrDefault(k => k.ToString() == "id") is { } idKey)
                    sr.Id = OkfTypes.ValueAsString(item[idKey]) ?? "";
                if (item.Keys.FirstOrDefault(k => k.ToString() == "url") is { } urlKey)
                    sr.Url = OkfTypes.ValueAsString(item[urlKey]);
                meta.SourceRefs.Add(sr);
            }
        }
        if (dict.Keys.FirstOrDefault(k => k.ToString() == "links") is { } lnkKey &&
            dict[lnkKey] is List<object?> lnkList)
        {
            foreach (var item in lnkList.OfType<Dictionary<object, object>>())
            {
                var link = new OkfLink();
                if (item.Keys.FirstOrDefault(k => k.ToString() == "target") is { } tKey)
                    link.Target = OkfTypes.ValueAsString(item[tKey]) ?? "";
                if (item.Keys.FirstOrDefault(k => k.ToString() == "label") is { } lKey)
                    link.Label = OkfTypes.ValueAsString(item[lKey]);
                meta.Links.Add(link);
            }
        }
        if (dict.Keys.FirstOrDefault(k => k.ToString() == "citations") is { } citKey &&
            dict[citKey] is List<object?> citList)
        {
            foreach (var item in citList.OfType<Dictionary<object, object>>())
            {
                var cit = new OkfCitation();
                if (item.Keys.FirstOrDefault(k => k.ToString() == "id") is { } idKey)
                    cit.Id = OkfTypes.ValueAsString(item[idKey]) ?? "";
                if (item.Keys.FirstOrDefault(k => k.ToString() == "target") is { } tKey)
                    cit.Target = OkfTypes.ValueAsString(item[tKey]) ?? "";
                if (item.Keys.FirstOrDefault(k => k.ToString() == "label") is { } lKey)
                    cit.Label = OkfTypes.ValueAsString(item[lKey]);
                meta.Citations.Add(cit);
            }
        }
        if (dict.Keys.FirstOrDefault(k => k.ToString() == "docs_sync") is { } dsKey)
            meta.DocsSync = dict[dsKey];
        // Extra
        var knownOsKeys = new HashSet<string> { "visibility", "kind", "schema_version", "scope_refs", "source_refs", "links", "citations", "docs_sync" };
        foreach (var (k, v) in dict)
        {
            var ks = k.ToString()!;
            if (!knownOsKeys.Contains(ks))
                meta.Extra[ks] = v;
        }
        return meta;
    }

    public static List<string> ToStringList(object? value)
    {
        return value switch
        {
            null => new(),
            string s => s.Trim().Length > 0 ? new() { s.Trim() } : new(),
            List<object?> list => list.Select(OkfTypes.ValueAsString).Where(v => v != null).ToList()!,
            System.Collections.IList list => list.Cast<object?>().Select(OkfTypes.ValueAsString).Where(v => v != null).ToList()!,
            _ => new(),
        };
    }

    public static string RenderConcept(OkfConcept concept)
    {
        var yaml = SerializeFrontmatterCanonical(concept.Frontmatter);
        var sb = new StringBuilder();
        sb.AppendLine(OkfFrontmatterDelimiter);
        sb.Append(yaml);
        if (!yaml.EndsWith('\n')) sb.AppendLine();
        sb.AppendLine(OkfFrontmatterDelimiter);
        sb.Append(concept.Body);
        return sb.ToString();
    }

    public static OkfConcept ParseConcept(string path, string contents)
    {
        var (fm, body) = SplitFrontmatter(contents);
        OkfTypes.RequireOkfType(fm.ConceptType);
        return OkfConcept.Create(path, fm, body);
    }

    public static OkfConcept ParseConceptWithDerivedOpensymphony(string path, string contents)
    {
        var concept = ParseConcept(path, contents);
        concept.DerivedOpensymphony = false;
        if (concept.Frontmatter.Opensymphony != null)
            return concept;
        var metadata = OkfTypes.LegacyFrontmatterToOpensymphonyMetadata(concept.Frontmatter);
        OkfTypes.RemoveRepresentedLegacyOpensymphonyFields(concept.Frontmatter.Extra, metadata);
        concept.Frontmatter.Opensymphony = metadata;
        concept.DerivedOpensymphony = true;
        return concept;
    }

    public static List<OkfLintFinding> LintConcept(OkfConcept concept)
    {
        var findings = new List<OkfLintFinding>();
        var fm = concept.Frontmatter;

        if (fm.ConceptType.Trim().Length == 0)
            findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "missing-type", Message = "frontmatter `type` is required", Severity = "error" });
        else if (!OkfTypes.KnownOkfType(fm.ConceptType))
            findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "unknown-type", Message = $"unknown OKF concept type `{fm.ConceptType}`", Severity = "warning" });

        if (fm.Opensymphony == null)
            findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "missing-opensymphony", Message = "frontmatter is missing `opensymphony` metadata", Severity = "warning" });
        else
        {
            if (fm.Opensymphony.Visibility == null)
                findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "missing-visibility", Message = "opensymphony.visibility is required", Severity = "error" });
            if (fm.Opensymphony.ScopeRefs.Count == 0)
                findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "missing-scope", Message = "at least one scope_ref is required", Severity = "warning" });
        }

        // Check for private material in public concepts
        if (fm.Opensymphony?.Visibility == MemoryVisibility.Public)
        {
            var visible = OkfMarkdown.MarkdownVisibleText(concept.Body);
            var privateMaterial = OkfMarkdown.PublicExportPrivateMaterial(visible);
            if (privateMaterial != null)
                findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "public-private-material", Message = $"public concept contains {privateMaterial}", Severity = "error" });
        }

        // Check links resolve to known concepts
        foreach (var link in concept.Links)
        {
            var target = OkfMarkdown.NormalizedMarkdownLinkId(link.Target);
            if (target != null && target != concept.Id)
                findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "unresolved-link", Message = $"link target `{link.Target}` does not resolve to a known concept", Severity = "info" });
        }

        return findings;
    }

    public static MemoryReindexReport ReindexBundle(string bundleRoot, MemoryIndexStore store, bool emit = true)
    {
        var report = new MemoryReindexReport();
        var conceptIndex = new Dictionary<string, OkfConcept>();
        var findings = new List<OkfLintFinding>();

        if (!Directory.Exists(bundleRoot))
        {
            report.Findings = findings;
            return report;
        }

        var mdFiles = Directory.GetFiles(bundleRoot, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            var relative = Path.GetRelativePath(bundleRoot, file).Replace('\\', '/');
            try
            {
                var contents = File.ReadAllText(file);
                var concept = ParseConceptWithDerivedOpensymphony(relative, contents);
                conceptIndex[concept.Id] = concept;
                var lintFindings = LintConcept(concept);
                findings.AddRange(lintFindings);
            }
            catch (Exception ex)
            {
                findings.Add(new OkfLintFinding { ConceptId = relative, Rule = "parse-error", Message = ex.Message, Severity = "error" });
            }
        }

        // Re-lint links now that we know all concepts
        var allIds = conceptIndex.Keys.ToHashSet();
        findings = findings.Where(f => f.Rule != "unresolved-link").ToList();
        foreach (var concept in conceptIndex.Values)
        {
            foreach (var link in concept.Links)
            {
                var target = OkfMarkdown.NormalizedMarkdownLinkId(link.Target);
                if (target != null && !allIds.Contains(target))
                    findings.Add(new OkfLintFinding { ConceptId = concept.Id, Rule = "unresolved-link", Message = $"link target `{link.Target}` does not resolve to a known concept", Severity = "info" });
            }
        }

        report.Concepts = conceptIndex.Values.OrderBy(c => c.Id).ToList();
        report.Findings = findings;
        report.ErrorCount = findings.Count(f => f.Severity == "error");
        report.WarningCount = findings.Count(f => f.Severity == "warning");

        if (emit)
        {
            store.ClearReindexTables();
            foreach (var concept in report.Concepts)
            {
                var row = ConceptToIssueRow(concept);
                store.Issues[row.IssueKey] = row;
            }
        }

        return report;
    }

    public static IndexedIssueRow ConceptToIssueRow(OkfConcept concept)
    {
        var fm = concept.Frontmatter;
        var os = fm.Opensymphony;
        var row = new IndexedIssueRow
        {
            IssueKey = concept.Id,
            Title = fm.Title ?? "",
            State = null,
            Milestone = os?.ScopeRefs.FirstOrDefault(s => s.Kind == KnowledgeScopeKind.Milestone)?.Id,
            LabelsJson = JsonSerializer.Serialize(fm.Tags),
            CapsulePath = concept.Path.AsPath(),
            Visibility = os?.Visibility == MemoryVisibility.Public ? "public" : "private",
            SourceHash = "",
            WarningCount = 0,
            DocsSyncStatus = "pending",
            Body = concept.Body,
            CapturedAt = fm.Timestamp ?? "",
            ConceptId = concept.Id,
            ConceptType = fm.ConceptType,
            Description = fm.Description,
            TagsJson = JsonSerializer.Serialize(fm.Tags),
            ScopeRefsJson = JsonSerializer.Serialize(os?.ScopeRefs ?? new()),
            SourceRefsJson = JsonSerializer.Serialize(os?.SourceRefs ?? new()),
            LinksJson = JsonSerializer.Serialize(os?.Links ?? new()),
            CitationsJson = JsonSerializer.Serialize(os?.Citations ?? new()),
            Freshness = os != null ? "current" : "unknown",
            WarningsJson = "[]",
        };
        return row;
    }

    public static OkfExportReport ExportPublicBundle(string sourceRoot, string targetRoot, bool dryRun = false)
    {
        var report = new OkfExportReport { OutputPath = targetRoot };
        if (!Directory.Exists(sourceRoot))
            throw MemoryError.InvalidInput($"source bundle not found: {sourceRoot}");

        var mdFiles = Directory.GetFiles(sourceRoot, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            var contents = File.ReadAllText(file);
            var (fm, _) = SplitFrontmatter(contents);
            var visibility = fm.Opensymphony?.Visibility ?? OkfTypes.LegacyVisibility(fm) ?? MemoryVisibility.Private;
            if (visibility == MemoryVisibility.Private)
            {
                report.SkippedPrivateFiles.Add(relative);
                continue;
            }
            report.CopiedFiles.Add(relative);
            if (!dryRun)
            {
                var targetPath = Path.Join(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, true);
            }
        }
        report.FindingCount = report.SkippedPrivateFiles.Count;
        return report;
    }

    public static OkfImportReport ImportBundle(string sourceRoot, string targetRoot, MemoryIndexStore store, bool dryRun = false)
    {
        var report = new OkfImportReport { SourcePath = sourceRoot, TargetPath = targetRoot };
        if (!Directory.Exists(sourceRoot))
            throw MemoryError.InvalidInput($"source bundle not found: {sourceRoot}");

        var mdFiles = Directory.GetFiles(sourceRoot, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            report.CopiedFiles.Add(relative);
            if (!dryRun)
            {
                var targetPath = Path.Join(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, true);
            }
        }
        report.FindingCount = report.CopiedFiles.Count;
        report.Reindex = ReindexBundle(targetRoot, store, emit: !dryRun);
        return report;
    }
}

public class OkfLintFinding
{
    public string ConceptId { get; set; } = "";
    public string Rule { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "info";
}

public class MemoryReindexReport
{
    public List<OkfConcept> Concepts { get; set; } = new();
    public List<OkfLintFinding> Findings { get; set; } = new();
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}
