using System.Text.Json;
using OpenSymphony.Planning;

namespace OpenSymphony.Planning.Tests;

public class ResearchTests
{
    [Fact]
    public void BuilderConstructsValidBrief()
    {
        var brief = new ResearchBriefBuilder("Test Topic")
            .Context("Researching test integrations")
            .AddFinding("Found a relevant API", "https://example.com/api", ConfidenceLevel.High)
            .Build();
        Assert.True(brief.IsOk);
        var b = brief.Value;
        Assert.Equal("Test Topic", b.Topic);
        Assert.Single(b.Findings);
        Assert.Equal(ConfidenceLevel.High, b.Findings[0].Confidence);
        Assert.NotNull(b.Findings[0].SourceUrl);
    }

    [Fact]
    public void BuilderRejectsEmptyTopic()
    {
        var result = new ResearchBriefBuilder("").Build();
        Assert.True(result.IsErr);
        Assert.Equal(ResearchErrorKind.MissingField, result.Error.Kind);
        Assert.Equal("topic", result.Error.Field);
    }

    [Fact]
    public void FindingsAboveFiltersByConfidence()
    {
        var brief = new ResearchBriefBuilder("Test")
            .AddFinding("High conf", null, ConfidenceLevel.High)
            .AddFinding("Medium conf", null, ConfidenceLevel.Medium)
            .AddFinding("Low conf", null, ConfidenceLevel.Low)
            .Build().Value;
        Assert.Single(brief.FindingsAbove(ConfidenceLevel.High));
        Assert.Equal(2, brief.FindingsAbove(ConfidenceLevel.Medium).Count);
        Assert.Equal(3, brief.FindingsAbove(ConfidenceLevel.Low).Count);
    }

    [Fact]
    public void RenderMarkdownIncludesAllFindings()
    {
        var brief = new ResearchBriefBuilder("API Research")
            .Context("Evaluating integration options")
            .AddFindingWithTitle("OpenHands supports agent-server protocol", "https://docs.all-hands.dev",
                "OpenHands Docs", ConfidenceLevel.High, new List<string> { "api", "protocol" })
            .Build().Value;
        var md = brief.RenderMarkdown();
        Assert.Contains("API Research", md);
        Assert.Contains("Evaluating integration options", md);
        Assert.Contains("OpenHands supports agent-server protocol", md);
        Assert.Contains("OpenHands Docs", md);
        Assert.Contains("api", md);
    }

    [Fact]
    public void ResearchStoreInsertAndRetrieve()
    {
        var store = new ResearchArtifactStore();
        store.Insert(new ResearchBriefBuilder("Topic A").AddFinding("Finding A", null, ConfidenceLevel.High).Build().Value);
        store.Insert(new ResearchBriefBuilder("Topic B").AddFinding("Finding B", null, ConfidenceLevel.Medium).Build().Value);
        Assert.Equal(2, store.Len());
        Assert.NotNull(store.Get("Topic A"));
        Assert.NotNull(store.Get("Topic B"));
        Assert.Null(store.Get("Topic C"));
        Assert.Equal(2, store.Topics().Count);
    }

    [Fact]
    public void ResearchStoreRenderAllMarkdown()
    {
        var store = new ResearchArtifactStore();
        store.Insert(new ResearchBriefBuilder("Topic X").AddFinding("X finding", null, ConfidenceLevel.High).Build().Value);
        store.Insert(new ResearchBriefBuilder("Topic Y").AddFinding("Y finding", null, ConfidenceLevel.Medium).Build().Value);
        var md = store.RenderAllMarkdown();
        Assert.Contains("Research Artifacts", md);
        Assert.Contains("Topic X", md);
        Assert.Contains("Topic Y", md);
    }

    [Fact]
    public void BriefSerializesToJson()
    {
        var brief = new ResearchBriefBuilder("Serialization Test")
            .AddFinding("Serde test", "https://serde.rs", ConfidenceLevel.High)
            .Build().Value;
        var json = JsonSerializer.Serialize(brief);
        Assert.Contains("Serialization Test", json);
        var deserialized = JsonSerializer.Deserialize<ResearchBrief>(json)!;
        Assert.Equal(brief.Topic, deserialized.Topic);
        Assert.Equal(brief.Findings.Count, deserialized.Findings.Count);
    }
}
