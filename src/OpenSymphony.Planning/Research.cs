namespace OpenSymphony.Planning;

public enum ConfidenceLevel { High, Medium, Low }

public sealed record ResearchFinding(
    string Summary,
    string? SourceUrl,
    string? SourceTitle,
    ConfidenceLevel Confidence,
    List<string> Tags);

public sealed record ResearchBrief(
    string Topic,
    string ResearchContext,
    List<ResearchFinding> Findings,
    DateTime GeneratedAt)
{
    public List<ResearchFinding> FindingsAbove(ConfidenceLevel minConfidence)
        => Findings.Where(f => ConfidenceRank(f.Confidence) >= ConfidenceRank(minConfidence)).ToList();

    public string RenderMarkdown()
    {
        var md = $"# Research Brief: {Topic}\n\n";

        if (!string.IsNullOrEmpty(ResearchContext))
            md += $"**Context:** {ResearchContext}\n\n";

        md += $"**Generated:** {GeneratedAt:yyyy-MM-dd HH:mm:ssZ}\n\n";
        md += "## Findings\n\n";

        for (var i = 0; i < Findings.Count; i++)
        {
            var finding = Findings[i];
            md += $"### {i + 1} {ConfidenceLabel(finding.Confidence)}\n\n{finding.Summary}\n";

            if (finding.SourceUrl is { } url)
            {
                var title = finding.SourceTitle ?? url;
                md += $"- **Source:** [{title}]({url})\n";
            }

            if (finding.Tags.Count > 0)
                md += $"- **Tags:** {string.Join(", ", finding.Tags)}\n";
            md += "\n";
        }

        return md;
    }

    private static int ConfidenceRank(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.High => 3,
        ConfidenceLevel.Medium => 2,
        ConfidenceLevel.Low => 1,
        _ => 0,
    };

    private static string ConfidenceLabel(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.High => "🟢 High",
        ConfidenceLevel.Medium => "🟡 Medium",
        ConfidenceLevel.Low => "🔴 Low",
        _ => "Unknown",
    };
}

public enum ResearchErrorKind { MissingField }

public sealed record ResearchError(ResearchErrorKind Kind, string Field)
{
    public override string ToString() => Kind switch
    {
        ResearchErrorKind.MissingField => $"missing required field: {Field}",
        _ => Field,
    };
}

public sealed class ResearchBriefBuilder
{
    private readonly string _topic;
    private string _researchContext = "";
    private readonly List<ResearchFinding> _findings = new();

    public ResearchBriefBuilder(string topic) => _topic = topic;

    public ResearchBriefBuilder Context(string ctx) { _researchContext = ctx; return this; }

    public ResearchBriefBuilder AddFinding(string summary, string? sourceUrl, ConfidenceLevel confidence)
    {
        _findings.Add(new ResearchFinding(summary, sourceUrl, null, confidence, new List<string>()));
        return this;
    }

    public ResearchBriefBuilder AddFindingWithTitle(string summary, string? sourceUrl, string sourceTitle, ConfidenceLevel confidence, List<string> tags)
    {
        _findings.Add(new ResearchFinding(summary, sourceUrl, sourceTitle, confidence, tags));
        return this;
    }

    public Result<ResearchBrief, ResearchError> Build()
    {
        if (string.IsNullOrEmpty(_topic))
            return Result<ResearchBrief, ResearchError>.Err(new ResearchError(ResearchErrorKind.MissingField, "topic"));
        return Result<ResearchBrief, ResearchError>.Ok(new ResearchBrief(_topic, _researchContext, _findings, DateTime.UtcNow));
    }
}

public sealed class ResearchArtifactStore
{
    public SortedDictionary<string, ResearchBrief> Briefs { get; } = new();

    public ResearchArtifactStore() { }

    public void Insert(ResearchBrief brief) => Briefs[brief.Topic] = brief;

    public ResearchBrief? Get(string topic) => Briefs.TryGetValue(topic, out var b) ? b : null;

    public List<string> Topics() => Briefs.Keys.ToList();

    public bool IsEmpty() => Briefs.Count == 0;

    public int Len() => Briefs.Count;

    public string RenderAllMarkdown()
    {
        var md = "# Research Artifacts\n\n";
        var i = 0;
        foreach (var kv in Briefs)
        {
            if (i > 0) md += "\n---\n\n";
            md += kv.Value.RenderMarkdown();
            i++;
        }
        return md;
    }
}
