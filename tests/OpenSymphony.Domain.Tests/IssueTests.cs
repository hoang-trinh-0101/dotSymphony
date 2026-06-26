using System.Text.Json;
using OpenSymphony.Domain;

namespace OpenSymphony.Domain.Tests;

public class IssueTests
{
    // ht: mirrors Rust lib.rs sample_issue() exactly (lines 118-148).
    private static NormalizedIssue SampleIssue() => new(
        Id: StringIdentifier<IssueId>.New("lin_260").Value,
        Identifier: StringIdentifier<IssueIdentifier>.New("COE-260").Value,
        Title: "Domain model and orchestrator state machine",
        Description: "Define the shared orchestration model.",
        Priority: (byte)1,
        State: new IssueState(
            Id: null,
            Name: "In Progress",
            Category: IssueStateCategory.Active),
        BranchName: "leonardogonzalez/coe-260-domain-model-and-orchestrator-state-machine",
        Url: "https://linear.app/trilogy-ai-coe/issue/COE-260/domain-model-and-orchestrator-state-machine",
        Labels: ["foundation", "contracts"],
        ParentId: null,
        BlockedBy: [],
        SubIssues:
        [
            new IssueRef(
                Id: StringIdentifier<IssueId>.New("lin_261").Value,
                Identifier: StringIdentifier<IssueIdentifier>.New("COE-261").Value,
                State: "Done"),
        ],
        CreatedAt: TimestampMs.New(10),
        UpdatedAt: TimestampMs.New(20));

    [Theory]
    [InlineData(IssueStateCategory.Active, "\"active\"")]
    [InlineData(IssueStateCategory.NonActive, "\"non_active\"")]
    [InlineData(IssueStateCategory.Terminal, "\"terminal\"")]
    public void IssueStateCategory_SerializesSnakeCase(IssueStateCategory cat, string expected)
    {
        var json = JsonSerializer.Serialize(cat, DomainJsonOptions.Default);
        Assert.Equal(expected, json);
    }

    [Fact]
    public void NormalizedIssue_RoundTrip_PreservesAllFields()
    {
        var issue = SampleIssue();
        var json = JsonSerializer.Serialize(issue, DomainJsonOptions.Default);
        var back = JsonSerializer.Deserialize<NormalizedIssue>(json, DomainJsonOptions.Default)!;

        Assert.Equal(issue.Id, back.Id);
        Assert.Equal(issue.Identifier, back.Identifier);
        Assert.Equal(issue.Title, back.Title);
        Assert.Equal(issue.Description, back.Description);
        Assert.Equal(issue.Priority, back.Priority);
        Assert.Equal(issue.State, back.State);
        Assert.Equal(issue.BranchName, back.BranchName);
        Assert.Equal(issue.Url, back.Url);
        Assert.Equal(issue.Labels, back.Labels);
        Assert.Equal(issue.ParentId, back.ParentId);
        Assert.Equal(issue.BlockedBy, back.BlockedBy);
        Assert.Equal(issue.SubIssues, back.SubIssues);
        Assert.Equal(issue.CreatedAt, back.CreatedAt);
        Assert.Equal(issue.UpdatedAt, back.UpdatedAt);
    }

    [Fact]
    public void NormalizedIssue_ParentIdNone_OmittedFromJson()
    {
        var issue = SampleIssue() with { ParentId = null };
        var json = JsonSerializer.Serialize(issue, DomainJsonOptions.Default);
        Assert.DoesNotContain("\"parent_id\"", json);
    }

    [Fact]
    public void NormalizedIssue_SubIssuesEmpty_OmittedFromJson()
    {
        var issue = SampleIssue() with { SubIssues = [] };
        var json = JsonSerializer.Serialize(issue, DomainJsonOptions.Default);
        Assert.DoesNotContain("\"sub_issues\"", json);
    }

    [Fact]
    public void NormalizedIssue_ParentIdSome_Serialized()
    {
        var issue = SampleIssue() with
        {
            ParentId = StringIdentifier<IssueId>.New("lin_250").Value,
        };
        var json = JsonSerializer.Serialize(issue, DomainJsonOptions.Default);
        Assert.Contains("\"parent_id\":\"lin_250\"", json);
    }

    [Fact]
    public void NormalizedIssue_OptionFieldsNone_SerializedAsNull()
    {
        var issue = SampleIssue() with
        {
            Description = null,
            Priority = null,
            BranchName = null,
            Url = null,
            CreatedAt = null,
            UpdatedAt = null,
            SubIssues = [],
        };
        var json = JsonSerializer.Serialize(issue, DomainJsonOptions.Default);
        Assert.Contains("\"description\":null", json);
        Assert.Contains("\"priority\":null", json);
        Assert.Contains("\"branch_name\":null", json);
        Assert.Contains("\"url\":null", json);
        Assert.Contains("\"created_at\":null", json);
        Assert.Contains("\"updated_at\":null", json);
    }

    [Fact]
    public void BlockerRef_AllNone_SerializedAsNulls()
    {
        var blocker = new BlockerRef(null, null, null, null, null);
        var json = JsonSerializer.Serialize(blocker, DomainJsonOptions.Default);
        Assert.Contains("\"id\":null", json);
        Assert.Contains("\"identifier\":null", json);
        Assert.Contains("\"state\":null", json);
        Assert.Contains("\"created_at\":null", json);
        Assert.Contains("\"updated_at\":null", json);
    }

    [Fact]
    public void IssueState_RoundTrip_WithNullId()
    {
        var state = new IssueState(null, "In Progress", IssueStateCategory.Active);
        var json = JsonSerializer.Serialize(state, DomainJsonOptions.Default);
        Assert.Contains("\"id\":null", json);
        var back = JsonSerializer.Deserialize<IssueState>(json, DomainJsonOptions.Default)!;
        Assert.Equal(state, back);
    }

    [Fact]
    public void IssueState_RoundTrip_WithSomeId()
    {
        var state = new IssueState(
            StringIdentifier<TrackerStateId>.New("state_1").Value,
            "Done",
            IssueStateCategory.Terminal);
        var json = JsonSerializer.Serialize(state, DomainJsonOptions.Default);
        Assert.Contains("\"id\":\"state_1\"", json);
        var back = JsonSerializer.Deserialize<IssueState>(json, DomainJsonOptions.Default)!;
        Assert.Equal(state, back);
    }

    [Fact]
    public void NormalizedIssue_Deserialize_MissingParentIdAndSubIssues_DefaultsToNoneAndEmpty()
    {
        // ht: JSON without parent_id/sub_issues keys (Rust serde(default)).
        var json = """
        {
          "id": "lin_260",
          "identifier": "COE-260",
          "title": "t",
          "description": null,
          "priority": null,
          "state": { "id": null, "name": "n", "category": "active" },
          "branch_name": null,
          "url": null,
          "labels": [],
          "blocked_by": [],
          "created_at": null,
          "updated_at": null
        }
        """;
        var back = JsonSerializer.Deserialize<NormalizedIssue>(json, DomainJsonOptions.Default)!;
        Assert.Null(back.ParentId);
        Assert.Empty(back.SubIssues);
    }
}
