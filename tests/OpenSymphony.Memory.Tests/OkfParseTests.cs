using OpenSymphony.Memory;

namespace OpenSymphony.Memory.Tests;

/// <summary>
/// Tests for OKF concept parsing, rendering, and reindex.
/// Ported from Rust older/crates/opensymphony-memory/src/lib.rs test module.
/// </summary>
public class OkfParseTests
{
    private const string LegacyIssueCapsule = """
---
type: issue-capsule
visibility: private
issue: COE-123
title: "COE-123: WebSocket reconnect recovery"
milestone: "M3: Runtime"
milestone_id: milestone-3
linear_url: https://linear.app/example/issue/COE-123
areas:
  - openhands-runtime
repository: OpenSymphony
prs:
  - number: 456
    url: https://github.com/example/repo/pull/456
    merge_sha: abcdef1234567890
source_refs:
  linear_issue: linear:COE-123
  github_prs:
    - github:pr:456
docs_sync:
  status: pending
legacy_custom: keep-me
---

# COE-123: WebSocket reconnect recovery

See [runtime docs](/areas/openhands-runtime.md).
""";

    [Fact]
    public void OkfParsesLegacyIssueCapsuleWithoutLosingMetadata()
    {
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("issues/COE-123.md", LegacyIssueCapsule);

        Assert.Equal("issues/COE-123", concept.Id);
        Assert.Equal("issue-capsule", concept.Frontmatter.ConceptType);
        Assert.True(concept.Frontmatter.Extra.ContainsKey("legacy_custom"));
        Assert.True(concept.Frontmatter.Extra.ContainsKey("source_refs"));

        var metadata = concept.Frontmatter.Opensymphony!;
        Assert.NotNull(metadata);
        Assert.Equal(MemoryVisibility.Private, metadata.Visibility);
        Assert.Equal("issue_capsule", metadata.Kind);
        Assert.Contains(metadata.ScopeRefs, s => s.Kind == KnowledgeScopeKind.WorkItem && s.Id == "COE-123");
        Assert.Contains(metadata.ScopeRefs, s => s.Kind == KnowledgeScopeKind.Milestone && s.Id == "milestone-3");
        Assert.Contains(metadata.ScopeRefs, s => s.Kind == KnowledgeScopeKind.Area && s.Id == "openhands-runtime");
        Assert.Contains(metadata.ScopeRefs, s => s.Kind == KnowledgeScopeKind.Repository && s.Id == "OpenSymphony");
        Assert.Contains(metadata.SourceRefs, s => s.Kind == "linear_issue" && s.Id == "COE-123");
        Assert.Contains(metadata.SourceRefs, s => s.Kind == "github_pr" && s.Id == "456");
        Assert.Contains(metadata.SourceRefs, s => s.Kind == "github_pr" && s.Id == "456" && s.Url == "https://github.com/example/repo/pull/456");
        Assert.Equal("/areas/openhands-runtime.md", concept.Links[0].Target);

        var rendered = OkfParse.RenderConcept(concept);
        Assert.Contains("legacy_custom: keep-me", rendered);
        Assert.Contains("issue: COE-123", rendered);
        Assert.DoesNotContain("opensymphony:", rendered);
    }

    [Fact]
    public void OkfExplicitOpensymphonyMetadataRoundTrips()
    {
        var original = """
---
type: topic-doc
area: legacy-area
visibility: private
legacy_custom: keep-me
opensymphony:
  visibility: public
  kind: curated_topic
  schema_version: 7
  scope_refs:
    - kind: area
      id: explicit-area
---

# Runtime
""";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", original);
        Assert.False(concept.DerivedOpensymphony);

        var rendered = OkfParse.RenderConcept(concept);
        Assert.Contains("opensymphony:", rendered);
        Assert.Contains("curated_topic", rendered);
        Assert.Contains("explicit-area", rendered);
        Assert.Contains("legacy_custom: keep-me", rendered);
        Assert.DoesNotContain("legacy-area", rendered);
        Assert.DoesNotContain("visibility: private", rendered);
    }

    [Fact]
    public void OkfPartialExplicitOpensymphonyPreservesUnrepresentedLegacyFields()
    {
        var original = """
---
type: topic-doc
area: legacy-area
visibility: private
issue: COE-123
legacy_custom: keep-me
opensymphony:
  kind: curated_topic
---

# Runtime
""";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", original);

        var rendered = OkfParse.RenderConcept(concept);
        Assert.Contains("opensymphony:", rendered);
        Assert.Contains("kind: curated_topic", rendered);
        Assert.Contains("area: legacy-area", rendered);
        Assert.Contains("visibility: private", rendered);
        Assert.Contains("issue: COE-123", rendered);
        Assert.Contains("legacy_custom: keep-me", rendered);
    }

    [Fact]
    public void OkfNullOpensymphonyUsesLegacySourceOfTruth()
    {
        var original = """
---
type: topic-doc
area: legacy-area
visibility: public
opensymphony: ~
---

# Runtime
""";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", original);
        Assert.True(concept.DerivedOpensymphony);

        var rendered = OkfParse.RenderConcept(concept);
        Assert.Contains("area: legacy-area", rendered);
        Assert.Contains("visibility: public", rendered);
        Assert.DoesNotContain("opensymphony:", rendered);
    }

    [Fact]
    public void OkfDemoParseRenderPreservesLegacySourceOfTruth()
    {
        var original = """
---
type: topic-doc
area: openhands-runtime
visibility: public
docs_sync:
  status: pending
---

# Runtime

See [COE-123](/issues/COE-123.md).
""";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("./areas/./runtime.md", original);
        var rendered = OkfParse.RenderConcept(concept);

        Assert.Equal("areas/runtime.md", concept.Path.AsPath());
        Assert.True(concept.DerivedOpensymphony);
        Assert.Contains("visibility: public", rendered);
        Assert.Contains("docs_sync:", rendered);
        Assert.DoesNotContain("opensymphony:", rendered);
    }

    [Fact]
    public void OkfParsesMilestoneAndTopicDocFixtures()
    {
        var milestone = OkfParse.ParseConceptWithDerivedOpensymphony("milestones/m3-runtime.md", """
---
type: milestone-memory-node
milestone: "M3: Runtime"
updated_at: 2026-06-13T17:00:00Z
---

# M3: Runtime

- [COE-123](/issues/COE-123.md)
""");
        var milestoneMetadata = milestone.Frontmatter.Opensymphony!;
        Assert.Equal("milestone_memory_node", milestoneMetadata.Kind);
        Assert.Contains(milestoneMetadata.ScopeRefs, s => s.Kind == KnowledgeScopeKind.Milestone && s.Id == "M3: Runtime");

        var topic = OkfParse.ParseConceptWithDerivedOpensymphony("areas/openhands-runtime.md", """
---
type: topic-doc
area: openhands-runtime
visibility: public
last_memory_sync: 2026-06-13T17:00:00Z
---

# OpenHands Runtime

See [COE-123](/issues/COE-123.md).
""");
        var topicMetadata = topic.Frontmatter.Opensymphony!;
        Assert.Equal(MemoryVisibility.Public, topicMetadata.Visibility);
        Assert.Contains(topicMetadata.ScopeRefs, s => s.Kind == KnowledgeScopeKind.Area && s.Id == "openhands-runtime");
        Assert.Equal("/issues/COE-123.md", topic.Links[0].Target);
    }

    [Fact]
    public void OkfUnknownFieldsRoundTripThroughWriter()
    {
        var original = """
---
type: topic-doc
title: Runtime
x_unknown:
  nested: true
legacy_number: 7
---

# Runtime
""";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", original);
        var rendered = OkfParse.RenderConcept(concept);
        var reparsed = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", rendered);

        Assert.Equal(concept.Frontmatter.Extra["x_unknown"]?.ToString(), reparsed.Frontmatter.Extra["x_unknown"]?.ToString());
        Assert.Equal(concept.Frontmatter.Extra["legacy_number"]?.ToString(), reparsed.Frontmatter.Extra["legacy_number"]?.ToString());
        Assert.Equal("# Runtime\n", reparsed.Body);
    }

    [Fact]
    public void OkfFrontmatterAcceptsRealMarkdownDelimiters()
    {
        var contents = "---\r\ntype: topic-doc\r\ntitle: Runtime\r\n\r\n---   \r\n\r\n# Runtime\r\n";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", contents);

        Assert.Equal("topic-doc", concept.Frontmatter.ConceptType);
        Assert.Equal("# Runtime\r\n".Replace("\r\n", "\n"), concept.Body);
    }

    [Fact]
    public void OkfFrontmatterDoesNotCloseOnIndentedYamlDelimiter()
    {
        var contents = """
---
type: topic-doc
description: |
  ---
  YAML literal content
---

# Runtime
""";
        var concept = OkfParse.ParseConceptWithDerivedOpensymphony("areas/runtime.md", contents);

        Assert.Equal("---\nYAML literal content\n", concept.Frontmatter.Description);
        Assert.Equal("# Runtime\n", concept.Body);
    }

    [Fact]
    public void OkfReindexBundleIndexesConceptsAndReportsFindings()
    {
        using var temp = new TempDir();
        var bundle = Path.Combine(temp.Path, "bundle");
        Directory.CreateDirectory(Path.Combine(bundle, "issues"));
        File.WriteAllText(Path.Combine(bundle, "issues", "COE-123.md"), """
---
type: issue-capsule
visibility: public
issue: COE-123
---

# COE-123

See [missing](/issues/missing.md).
""");
        var store = new MemoryIndexStore();
        var report = OkfParse.ReindexBundle(bundle, store, emit: true);

        Assert.Single(report.Concepts);
        Assert.Contains(report.Concepts, c => c.Id == "issues/COE-123");
        // Broken link to missing.md should surface as an unresolved-link finding
        Assert.Contains(report.Findings, f => f.Rule == "unresolved-link");
        Assert.Single(store.Issues);
        Assert.Equal("issues/COE-123", store.Issues["issues/COE-123"].IssueKey);
    }
}
