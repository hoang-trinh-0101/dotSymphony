using OpenSymphony.Memory;

namespace OpenSymphony.Memory.Tests;

/// <summary>
/// Tests for Util helpers: SanitizeIssueKey, Slugify, TitleizeSlug,
/// ContainsPrivateMemoryLink, and related utilities.
/// Ported from Rust older/crates/opensymphony-memory/src/lib.rs test module.
/// </summary>
public class UtilTests
{
    [Fact]
    public void SanitizedIssueKeysAvoidSeparatorCollisions()
    {
        // Rust: assert_ne!(sanitize_issue_key("COE_123"), sanitize_issue_key("COE-123"));
        Assert.NotEqual(Util.SanitizeIssueKey("COE_123"), Util.SanitizeIssueKey("COE-123"));
    }

    [Fact]
    public void PrivateLinkGuardAllowsTrackedMemoryConfigPath()
    {
        // Rust: assert!(!contains_private_memory_link("Commit .opensymphony/memory/memory.yaml"));
        Assert.False(Util.ContainsPrivateMemoryLink("Commit .opensymphony/memory/memory.yaml"));

        // Rust: assert!(contains_private_memory_link("See .opensymphony/memory/issues/COE-123.md"));
        Assert.True(Util.ContainsPrivateMemoryLink("See .opensymphony/memory/issues/COE-123.md"));

        // Rust: assert!(!contains_private_memory_link("Do not publish .opensymphony/memory/memory.duckdb"));
        Assert.False(Util.ContainsPrivateMemoryLink("Do not publish .opensymphony/memory/memory.duckdb"));
    }

    [Fact]
    public void PrivateLinkGuardIgnoresMarkdownExamples()
    {
        // Rust test: hidden content in code spans, fenced code, and HTML comments
        // should not trigger the private link guard after markdown_visible_text.
        var hidden = "Inline sample: `.opensymphony/memory/issues/COE-123.md`.\n\n" +
                     "```text\n.opensymphony/memory/issues/COE-123.md\n```\n\n" +
                     "<!-- .opensymphony/memory/issues/COE-123.md -->\n";

        // markdown_visible_text strips code spans, fenced code, and HTML comments
        var visibleHidden = OkfMarkdown.MarkdownVisibleText(hidden);
        Assert.False(Util.ContainsPrivateMemoryLink(visibleHidden));

        var visibleReal = OkfMarkdown.MarkdownVisibleText(
            "See .opensymphony/memory/issues/COE-123.md for private details.");
        Assert.True(Util.ContainsPrivateMemoryLink(visibleReal));
    }

    [Fact]
    public void SlugifyProducesLowercaseHyphenatedSlugs()
    {
        Assert.Equal("openhands-runtime", Util.Slugify("OpenHands Runtime"));
        Assert.Equal("openhands-runtime", Util.Slugify("  OpenHands  Runtime  "));
        Assert.Equal("coe-123", Util.Slugify("COE 123"));
        Assert.Equal("abc", Util.Slugify("---abc---"));
    }

    [Fact]
    public void TitleizeSlugCapitalizesEachWord()
    {
        Assert.Equal("Openhands Runtime", Util.TitleizeSlug("openhands-runtime"));
        Assert.Equal("Coe 123", Util.TitleizeSlug("coe-123"));
        Assert.Equal("Runtime", Util.TitleizeSlug("runtime"));
    }

    [Fact]
    public void NormalizeIssueKeyTrimsAndUppercases()
    {
        Assert.Equal("COE-123", Util.NormalizeIssueKey("coe-123"));
        Assert.Equal("COE-123", Util.NormalizeIssueKey("  coe-123  "));
    }

    [Fact]
    public void SplitIssueKeyParsesPrefixAndNumber()
    {
        var (prefix, number) = Util.SplitIssueKey("COE-123");
        Assert.Equal("COE", prefix);
        Assert.Equal(123UL, number);
    }

    [Fact]
    public void SplitIssueKeyRejectsMissingSeparator()
    {
        Assert.Throws<MemoryError>(() => Util.SplitIssueKey("COE123"));
    }

    [Fact]
    public void IssueIsBeforeComparesSamePrefix()
    {
        Assert.True(Util.IssueIsBefore("COE-123", "COE-124"));
        Assert.False(Util.IssueIsBefore("COE-124", "COE-123"));
        Assert.False(Util.IssueIsBefore("ABC-123", "COE-124"));
    }

    [Fact]
    public void ShortShaReturnsFirstSevenChars()
    {
        Assert.Equal("abcdef1", Util.ShortSha("abcdef1234567890"));
        Assert.Equal("abc", Util.ShortSha("abc"));
    }

    [Fact]
    public void ShouldCopyCommentSummaryRejectsTranscripts()
    {
        Assert.False(Util.ShouldCopyCommentSummary("assistant: a full transcript should not be copied"));
        Assert.False(Util.ShouldCopyCommentSummary("user: question about the task"));
        Assert.False(Util.ShouldCopyCommentSummary("full transcript of the conversation"));
        Assert.True(Util.ShouldCopyCommentSummary("Decision: reconcile REST event backlog after readiness."));
    }

    [Fact]
    public void IsArchiveBlockingCaptureWarningIgnoresNoPrWarning()
    {
        Assert.False(Util.IsArchiveBlockingCaptureWarning("no GitHub PR source was matched"));
        Assert.True(Util.IsArchiveBlockingCaptureWarning("some other warning"));
    }
}
