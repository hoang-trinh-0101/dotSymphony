using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenSymphony.Memory;

public static class MemoryConstants
{
    public const string DefaultPrivateMemoryConfigFile = ".opensymphony/memory/memory.yaml";
    public const string DefaultMemoryConfigFile = "opensymphony-memory.yaml";
    public const string FallbackPrivateMemoryConfigFile = ".opensymphony/memory/config.yaml";
    public const string DefaultMemoryRoot = ".opensymphony/memory";
    public const string DefaultIndexFileName = "memory.duckdb";
    public const string DefaultPublicDocsRoot = "docs";
    public const string IssueCapsuleBegin = "<!-- BEGIN OPENSYMPHONY MANAGED ISSUE CAPSULE -->";
    public const string IssueCapsuleEnd = "<!-- END OPENSYMPHONY MANAGED ISSUE CAPSULE -->";
    public const string TopicDocBegin = "<!-- BEGIN OPENSYMPHONY MANAGED MEMORY SYNC -->";
    public const string TopicDocEnd = "<!-- END OPENSYMPHONY MANAGED MEMORY SYNC -->";
    public const int MemorySchemaVersion = 2;
    public const string OkfVersion = "0.1";
}

public static class Util
{
    public static string NormalizeIssueKey(string value) => value.Trim().ToUpperInvariant();

    public static string? NormalizeOptional(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static string Slugify(string value)
    {
        var slug = new StringBuilder();
        var previousDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                slug.Append(ch);
                previousDash = false;
            }
            else if (!previousDash)
            {
                slug.Append('-');
                previousDash = true;
            }
        }
        return slug.ToString().Trim('-');
    }

    public static string TitleizeSlug(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var titled = parts.Select(p =>
        {
            if (p.Length == 0) return "";
            return char.ToUpperInvariant(p[0]) + p[1..];
        });
        return string.Join(" ", titled);
    }

    public static string SanitizeIssueKey(string value)
    {
        var normalized = NormalizeIssueKey(value);
        var sanitized = new string(normalized.Select(c =>
            char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray());
        if (sanitized == normalized)
            return sanitized;
        using var hasher = SHA256.Create();
        var digest = hasher.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return $"{sanitized}-{digest[0]:x2}{digest[1]:x2}{digest[2]:x2}{digest[3]:x2}";
    }

    public static string FallbackTitle(string value, string fallback) =>
        NormalizeOptional(value) ?? fallback;

    public static string IssueTitle(IssueEvidence issue) =>
        FallbackTitle(issue.Title, issue.Identifier);

    public static List<string> NormalizeList(List<string> values)
    {
        var normalized = values
            .Select(v => NormalizeOptional(v))
            .Where(v => v != null)
            .Select(v => v!.ToLowerInvariant())
            .ToList();
        normalized.Sort();
        return normalized.Distinct().ToList();
    }

    public static bool ContainsIssueKey(string text, string issueKey) =>
        text.ToUpperInvariant().Contains(NormalizeIssueKey(issueKey));

    public static string ShortSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    public static string SummarizeText(string value, int limit)
    {
        var collapsed = string.Join(" ", value.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));
        if (collapsed.Length <= limit)
            return collapsed;
        var take = Math.Max(0, limit - 3);
        return collapsed[..take] + "...";
    }

    public static string SummarizeMarkdown(string value, int limit)
    {
        var summary = SummarizeText(value, limit);
        if (summary.StartsWith('-') || summary.StartsWith('#'))
            return summary;
        return summary + "\n";
    }

    public static bool ShouldCopyCommentSummary(string body)
    {
        var lower = body.ToLowerInvariant();
        return !lower.Contains("full transcript")
            && !lower.Contains("assistant:")
            && !lower.Contains("user:")
            && body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 400;
    }

    public static string DisplayPath(string repoRoot, string path)
    {
        var fullRepo = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(fullRepo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return fullPath[(fullRepo.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return fullPath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static string PathRelativeTo(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return fullPath[(fullRoot.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return fullPath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static string Sha256Hex(string contents)
    {
        using var hasher = SHA256.Create();
        var digest = hasher.ComputeHash(Encoding.UTF8.GetBytes(contents));
        return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
    }

    public static string SourceHash(IssueEvidence issue, List<PullRequestEvidence> prs)
    {
        using var hasher = SHA256.Create();
        var issueJson = JsonSerializer.Serialize(issue, MemoryJsonOptions.Options);
        hasher.TransformBlock(Encoding.UTF8.GetBytes(issueJson), 0, issueJson.Length, null, 0);
        var prsJson = JsonSerializer.Serialize(prs, MemoryJsonOptions.Options);
        hasher.TransformFinalBlock(Encoding.UTF8.GetBytes(prsJson), 0, prsJson.Length);
        return BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
    }

    public static bool IsArchiveBlockingCaptureWarning(string warning) =>
        !warning.Trim().Equals("no GitHub PR source was matched", StringComparison.OrdinalIgnoreCase);

    public static int ArchiveBlockingWarningCount(List<string> warnings) =>
        warnings.Count(w => IsArchiveBlockingCaptureWarning(w));

    public static string ReadToString(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException ex) { throw MemoryError.ReadFile(path, ex); }
        catch (Exception ex) { throw MemoryError.ReadFile(path, ex); }
    }

    public static void CreateDirAll(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) { throw MemoryError.CreateDir(path, ex); }
    }

    public static void WriteFile(string path, string contents)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            CreateDirAll(parent);
        try { File.WriteAllText(path, contents); }
        catch (Exception ex) { throw MemoryError.WriteFile(path, ex); }
    }

    public static string NormalizePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);

    public static string ResolvePath(string repoRoot, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path);

    public static string CanonicalizeExistingPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) { throw MemoryError.ResolvePath(path, ex); }
    }

    public static string CanonicalizeExistingPrefix(string path)
    {
        var full = Path.GetFullPath(path);
        return full;
    }

    public static void EnsureRepoContained(string repoRoot, string path)
    {
        var fullRepo = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(fullRepo, path);
        fullPath = Path.GetFullPath(fullPath);
        if (!fullPath.StartsWith(fullRepo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            fullPath != fullRepo)
        {
            throw MemoryError.PathOutsideRepo(fullPath, fullRepo);
        }
    }

    public static string ReplaceManagedBlock(string existing, string begin, string end, string replacement)
    {
        var beginIndex = existing.IndexOf(begin, StringComparison.Ordinal);
        if (beginIndex < 0) return existing;
        var endIndex = existing.IndexOf(end, beginIndex + begin.Length, StringComparison.Ordinal);
        if (endIndex < 0) return existing;
        endIndex += end.Length;
        var output = existing[..beginIndex].TrimEnd() + "\n\n" + replacement.TrimEnd() + "\n" +
                     existing[endIndex..].TrimStart('\n');
        return output;
    }

    public static (string Prefix, ulong Number) SplitIssueKey(string value)
    {
        var normalized = NormalizeIssueKey(value);
        var idx = normalized.LastIndexOf('-');
        if (idx < 0)
            throw MemoryError.InvalidInput($"issue key `{normalized}` must look like PREFIX-123");
        var prefix = normalized[..idx];
        if (!ulong.TryParse(normalized[(idx + 1)..], out var number))
            throw MemoryError.InvalidInput($"issue key `{normalized}` has an invalid numeric suffix");
        return (prefix, number);
    }

    public static bool IssueIsBefore(string issueKey, string beforeIssue)
    {
        try
        {
            var (ip, inum) = SplitIssueKey(issueKey);
            var (bp, bnum) = SplitIssueKey(beforeIssue);
            return ip == bp && inum < bnum;
        }
        catch { return false; }
    }

    public static string CompactCapsuleBody(string body)
    {
        var output = new StringBuilder();
        var include = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## Outcome") || line.StartsWith("## Decisions") ||
                line.StartsWith("## Validation") || line.StartsWith("## Follow-ups") ||
                line.StartsWith("## Documentation"))
            {
                include = true;
                output.AppendLine(line);
                continue;
            }
            if (line.StartsWith("## ") && include)
                include = false;
            if (include && output.ToString().Split('\n').Length < 80)
                output.AppendLine(line);
        }
        var result = output.ToString();
        if (string.IsNullOrWhiteSpace(result))
            return FirstInterestingLine(body);
        return result;
    }

    public static string FirstInterestingLine(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("---") && !trimmed.StartsWith("<!--") && !trimmed.StartsWith("type:"))
                return trimmed;
        }
        return "No summary available.";
    }

    public static string? FirstSectionLine(string body, string section)
    {
        var inSection = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith(section))
            {
                inSection = true;
                continue;
            }
            if (inSection && line.StartsWith("## "))
                return null;
            if (inSection)
            {
                var trimmed = line.Trim().TrimStart('-').Trim();
                if (trimmed.Length > 0)
                    return trimmed;
            }
        }
        return null;
    }

    public static List<string> NormalizeQueryTerms(string query) =>
        query.Split(c => !char.IsAsciiLetterOrDigit(c) && c != '-', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => NormalizeOptional(t))
            .Where(t => t != null)
            .Select(t => t!.ToLowerInvariant())
            .ToList();

    public static string SnippetForTerms(string body, List<string> terms)
    {
        foreach (var line in body.Split('\n'))
        {
            var lower = line.ToLowerInvariant();
            if (terms.Any(t => lower.Contains(t)))
                return SummarizeText(line, 240);
        }
        return FirstInterestingLine(body);
    }

    public static bool ContainsPrivateMemoryLink(string contents)
    {
        var lower = contents.ToLowerInvariant();
        return lower.Contains(".opensymphony/memory/issues") ||
               lower.Contains(".opensymphony\\memory\\issues") ||
               lower.Contains("../.opensymphony/memory/issues");
    }

    public static string? FirstMarkdownHeading(string contents)
    {
        foreach (var line in contents.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# "))
                return NormalizeOptional(trimmed[2..]);
        }
        return null;
    }
}

public static class StringExtensions
{
    public static string[] Split(this string s, Func<char, bool> predicate, StringSplitOptions options = StringSplitOptions.None)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in s)
        {
            if (predicate(ch))
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result.Where(r => options != StringSplitOptions.RemoveEmptyEntries || r.Length > 0).ToArray();
    }
}
