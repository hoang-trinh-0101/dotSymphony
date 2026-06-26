using System.Text;
using System.Text.RegularExpressions;

namespace OpenSymphony.Memory;

public static partial class OkfMarkdown
{
    public static List<OkfLink> ExtractMarkdownLinks(string body)
    {
        var links = new List<OkfLink>();
        var references = ReferenceLinkTargets(body);
        int index = 0;
        while (index < body.Length)
        {
            if (!CharAt(body, index, out var current, out var next))
                break;
            if (body[index..].StartsWith("<!--"))
            {
                index = SkipHtmlComment(body, index);
                continue;
            }
            if (IsFencedCodeStart(body, index))
            {
                index = SkipFencedCodeBlock(body, index);
                continue;
            }
            if (current == '`') { index = SkipCodeSpan(body, index); continue; }
            if (current == '\\') { index = next; continue; }
            if (current == '<')
            {
                if (ParseAutolink(body, next, out var target, out var afterTarget))
                {
                    links.Add(new OkfLink { Target = target });
                    index = afterTarget;
                    continue;
                }
                index = next;
                continue;
            }
            if (current == '[' && !IsImageMarker(body, index))
            {
                if (ParseLinkLabel(body, next, out var label, out var afterLabel))
                {
                    if (CharAt(body, afterLabel, out var afterChar, out var afterOpen))
                    {
                        if (afterChar == '(')
                        {
                            if (ParseLinkTarget(body, afterOpen, out var target, out var afterTarget))
                            {
                                if (target.Length > 0)
                                    links.Add(new OkfLink { Target = target, Label = label.Length > 0 ? label : null });
                                index = afterTarget;
                                continue;
                            }
                            index = afterOpen;
                            continue;
                        }
                        if (afterChar == '[')
                        {
                            if (ParseLinkLabel(body, afterOpen, out var reference, out var afterReference))
                            {
                                var key = reference.Length == 0 ? label : reference;
                                if (references.TryGetValue(NormalizeReferenceLabel(key), out var refTarget))
                                    links.Add(new OkfLink { Target = refTarget, Label = label.Length > 0 ? label : null });
                                index = afterReference;
                                continue;
                            }
                            index = afterOpen;
                            continue;
                        }
                        if (afterChar == ':')
                        {
                            index = afterLabel;
                            continue;
                        }
                    }
                    // Shortcut link
                    if (references.TryGetValue(NormalizeReferenceLabel(label), out var shortcutTarget))
                        links.Add(new OkfLink { Target = shortcutTarget, Label = label.Length > 0 ? label : null });
                    index = afterLabel;
                    continue;
                }
                index = next;
                continue;
            }
            index = next;
        }
        return links;
    }

    public static bool CharAt(string s, int index, out char current, out int next)
    {
        if (index >= s.Length)
        {
            current = '\0';
            next = index;
            return false;
        }
        current = s[index];
        next = index + 1;
        return true;
    }

    public static int SkipCodeSpan(string s, int index)
    {
        int tickEnd = index;
        while (tickEnd < s.Length && s[tickEnd] == '`') tickEnd++;
        int tickCount = tickEnd - index;
        int cursor = tickEnd;
        string fence = new string('`', tickCount);
        while (cursor < s.Length)
        {
            if (s[cursor..].StartsWith(fence))
                return cursor + tickCount;
            cursor++;
        }
        return tickEnd;
    }

    public static int SkipHtmlComment(string s, int index)
    {
        var end = s.IndexOf("-->", index, StringComparison.Ordinal);
        return end < 0 ? s.Length : end + 3;
    }

    public static bool IsFencedCodeStart(string s, int index)
    {
        var lineStart = s.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        for (int i = lineStart; i < index; i++)
        {
            if (s[i] != ' ' && s[i] != '\t') return false;
        }
        return s[index..].StartsWith("```") || s[index..].StartsWith("~~~");
    }

    public static int SkipFencedCodeBlock(string s, int index)
    {
        var fence = s.Substring(index, 3);
        int cursor = s.IndexOf('\n', index);
        if (cursor < 0) return s.Length;
        cursor++;
        while (cursor < s.Length)
        {
            if (IsFencedCodeStart(s, cursor) && s[cursor..].StartsWith(fence))
            {
                var nl = s.IndexOf('\n', cursor);
                return nl < 0 ? s.Length : nl + 1;
            }
            var nextNl = s.IndexOf('\n', cursor);
            if (nextNl < 0) return s.Length;
            cursor = nextNl + 1;
        }
        return s.Length;
    }

    public static bool IsImageMarker(string s, int index) =>
        index > 0 && s[index - 1] == '!' && !IsEscaped(s, index - 1);

    public static bool IsEscaped(string s, int index)
    {
        int slashCount = 0;
        int cursor = index;
        while (cursor > 0)
        {
            cursor--;
            if (s[cursor] != '\\') break;
            slashCount++;
        }
        return slashCount % 2 == 1;
    }

    public static bool ParseLinkLabel(string s, int index, out string label, out int next)
    {
        label = "";
        int labelStart = index;
        int depth = 1;
        while (index < s.Length)
        {
            if (!CharAt(s, index, out var current, out next)) break;
            if (current == '\\') { index = next; continue; }
            if (current == '`') { index = SkipCodeSpan(s, index); continue; }
            if (current == '[') { depth++; index = next; continue; }
            if (current == ']')
            {
                depth--;
                if (depth == 0)
                {
                    label = s[labelStart..index];
                    next = next;
                    return true;
                }
                index = next;
                continue;
            }
            index = next;
        }
        next = index;
        return false;
    }

    public static bool ParseLinkTarget(string s, int index, out string target, out int next)
    {
        target = "";
        int targetStart = index;
        int depth = 1;
        while (index < s.Length)
        {
            if (!CharAt(s, index, out var current, out next)) break;
            if (current == '\\') { index = next; continue; }
            if (current == '(') { depth++; index = next; continue; }
            if (current == ')')
            {
                depth--;
                if (depth == 0)
                {
                    var raw = s[targetStart..index].Trim();
                    target = NormalizeLinkTarget(raw) ?? "";
                    next = next;
                    return target.Length > 0;
                }
                index = next;
                continue;
            }
            index = next;
        }
        next = index;
        return false;
    }

    public static string? NormalizeLinkTarget(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return null;
        if (raw.StartsWith('<'))
        {
            var gt = raw.IndexOf('>');
            if (gt > 0)
            {
                var target = raw[1..gt];
                return target.Length > 0 ? target : null;
            }
        }
        if (MarkdownTargetBeforeOptionalTitle(raw) is { } mdTarget)
            return mdTarget;
        var firstSpace = raw.IndexOfAny(new[] { ' ', '\t' });
        return firstSpace < 0 ? raw : raw[..firstSpace];
    }

    public static string? MarkdownTargetBeforeOptionalTitle(string raw)
    {
        int boundary = raw.Length;
        for (int i = 0; i < raw.Length; i++)
        {
            if (char.IsWhiteSpace(raw[i]) && LocalMarkdownTarget(raw[..i]) != null)
            {
                boundary = i;
                break;
            }
        }
        var candidate = raw[..boundary].Trim();
        return LocalMarkdownTarget(candidate) != null && candidate.Length > 0 ? candidate : null;
    }

    public static bool ParseAutolink(string s, int index, out string target, out int next)
    {
        target = "";
        var end = s.IndexOf('>', index);
        if (end < 0) { next = index; return false; }
        var t = s[index..end];
        if (t.StartsWith("http://") || t.StartsWith("https://"))
        {
            target = t;
            next = end + 1;
            return true;
        }
        next = index;
        return false;
    }

    public static Dictionary<string, string> ReferenceLinkTargets(string body)
    {
        var references = new Dictionary<string, string>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('[')) continue;
            var rest = trimmed[1..];
            var colonIdx = rest.IndexOf("]:");
            if (colonIdx < 0) continue;
            var label = rest[..colonIdx];
            var target = rest[(colonIdx + 2)..];
            var normalizedTarget = NormalizeLinkTarget(target);
            if (normalizedTarget != null)
                references[NormalizeReferenceLabel(label)] = normalizedTarget;
        }
        return references;
    }

    public static string NormalizeReferenceLabel(string label) =>
        string.Join(" ", label.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static string? LocalMarkdownTarget(string target)
    {
        target = target.Trim();
        if (target.Length == 0 || target.StartsWith('#') || target.StartsWith("http://") ||
            target.StartsWith("https://") || target.StartsWith("mailto:"))
            return null;
        int end = target.IndexOfAny(new[] { '#', '?' });
        if (end < 0) end = target.Length;
        target = target[..end];
        return target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? target : null;
    }

    public static string? NormalizeOkfRelativePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', '\\'))
        {
            if (part == "." || part.Length == 0) continue;
            if (part == "..")
            {
                if (parts.Count == 0) return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return string.Join("/", parts);
    }

    public static string? NormalizedMarkdownLinkId(string target)
    {
        target = LocalMarkdownTarget(target);
        return target != null ? NormalizeOkfLinkId(target) : null;
    }

    public static string NormalizedWikiLinkId(string target)
    {
        var pipe = target.IndexOf('|');
        if (pipe >= 0) target = target[..pipe];
        return NormalizeOkfLinkId(target);
    }

    public static string NormalizeOkfLinkId(string target)
    {
        target = target.Trim().TrimStart('/');
        if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            target = target[..^3];
        else if (target.EndsWith(".MD"))
            target = target[..^3];
        return target.Replace(' ', '-').ToLowerInvariant();
    }

    public static List<string> ExtractWikiLinks(string body)
    {
        var links = new List<string>();
        int index = 0;
        while (index < body.Length)
        {
            if (!CharAt(body, index, out var current, out var next)) break;
            if (body[index..].StartsWith("<!--")) { index = SkipHtmlComment(body, index); continue; }
            if (IsFencedCodeStart(body, index)) { index = SkipFencedCodeBlock(body, index); continue; }
            if (current == '`') { index = SkipCodeSpan(body, index); continue; }
            if (current == '\\') { index = next; continue; }
            if (current == '[' && body[index..].StartsWith("[["))
            {
                int contentStart = index + 2;
                var end = body.IndexOf("]]", contentStart);
                if (end < 0) { index = next; continue; }
                var link = body[contentStart..end].Trim();
                if (link.Length > 0) links.Add(link);
                index = end + 2;
                continue;
            }
            index = next;
        }
        return links;
    }

    public static string MarkdownVisibleText(string contents)
    {
        var visible = new StringBuilder();
        int index = 0;
        while (index < contents.Length)
        {
            if (!CharAt(contents, index, out var current, out var next)) break;
            if (contents[index..].StartsWith("<!--")) { index = SkipHtmlComment(contents, index); continue; }
            if (IsFencedCodeStart(contents, index)) { index = SkipFencedCodeBlock(contents, index); continue; }
            if (current == '`') { index = SkipCodeSpan(contents, index); continue; }
            if (current == '\\') { index = next; continue; }
            visible.Append(current);
            index = next;
        }
        return visible.ToString();
    }

    public static string? FirstHeading(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# "))
            {
                var value = trimmed[2..].Trim();
                if (value.Length > 0) return value;
            }
        }
        return null;
    }

    public static string? FirstParagraph(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#') && !trimmed.StartsWith('-') && !trimmed.StartsWith("```"))
                return trimmed;
        }
        return null;
    }

    public static bool HasCitationsSection(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == "# Citations" || trimmed == "## Citations" || trimmed == "### Citations")
                return true;
        }
        return false;
    }

    // Private material patterns
    public static readonly string[] PublicPrivateCommentPatterns = { "linear:comment:" };
    public static readonly string[] PublicPrivateLocalPathPatterns = {
        ".opensymphony/memory/issues", ".opensymphony\\memory\\issues", "../.opensymphony/memory/issues"
    };
    public static readonly string[] PublicPrivateSourcePatterns = {
        ".opensymphony/memory/source", ".opensymphony\\memory\\source", "../.opensymphony/memory/source",
        ".opensymphony/memory/snapshot", ".opensymphony\\memory\\snapshot", "../.opensymphony/memory/snapshot"
    };

    public static string? PublicExportPrivateMaterial(string visible)
    {
        if (ContainsAnyAsciiCaseInsensitive(visible, PublicPrivateCommentPatterns))
            return "private comment references";
        if (ContainsAnyAsciiCaseInsensitive(visible, PublicPrivateLocalPathPatterns))
            return "private local paths";
        if (ContainsAnyAsciiCaseInsensitive(visible, PublicPrivateSourcePatterns))
            return "private source snapshots";
        return null;
    }

    public static bool ContainsAnyAsciiCaseInsensitive(string contents, string[] patterns) =>
        patterns.Any(p => ContainsAsciiCaseInsensitive(contents, p));

    public static bool ContainsAsciiCaseInsensitive(string contents, string pattern)
    {
        if (pattern.Length == 0) return false;
        for (int i = 0; i <= contents.Length - pattern.Length; i++)
        {
            if (contents.AsSpan(i, pattern.Length).Equals(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
