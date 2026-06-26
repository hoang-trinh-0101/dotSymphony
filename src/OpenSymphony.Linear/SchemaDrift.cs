using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/schema_drift.rs.

public sealed class RequiredField
{
    public string TypeName { get; }
    public string FieldName { get; }
    public bool Critical { get; }

    public RequiredField(string typeName, string fieldName, bool critical)
    {
        TypeName = typeName;
        FieldName = fieldName;
        Critical = critical;
    }
}

public sealed class SchemaDriftReport
{
    public bool IsCompatible { get; set; }
    public List<SchemaDriftViolation> MissingFields { get; set; } = new();
    public DateTimeOffset? CheckedAt { get; set; }
}

public sealed class SchemaDriftViolation
{
    public string TypeName { get; set; } = "";
    public string FieldName { get; set; } = "";
    public bool Critical { get; set; }
    public string Remediation { get; set; } = "";
}

public sealed class IntrospectedType
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("fields")]
    public List<IntrospectedField>? Fields { get; set; }
}

public sealed class IntrospectedField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public static class RequiredFields
{
    public static IReadOnlyList<RequiredField> List => _fields;

    private static readonly RequiredField[] _fields =
    [
        new("Issue", "id", true),
        new("Issue", "identifier", true),
        new("Issue", "url", true),
        new("Issue", "title", true),
        new("Issue", "description", false),
        new("Issue", "priority", false),
        new("Issue", "createdAt", true),
        new("Issue", "updatedAt", true),
        new("Issue", "state", true),
        new("Issue", "parent", false),
        new("Issue", "projectMilestone", false),
        new("Issue", "children", false),
        new("Issue", "labels", false),
        new("Issue", "inverseRelations", true),
        new("Issue", "comments", false),
        new("WorkflowState", "id", true),
        new("WorkflowState", "name", true),
        new("WorkflowState", "type", true),
        new("Project", "id", true),
        new("Project", "name", true),
        new("Project", "slugId", true),
        new("Project", "url", false),
        new("Project", "content", false),
        new("ProjectMilestone", "id", true),
        new("ProjectMilestone", "name", true),
        new("Label", "id", true),
        new("Label", "name", true),
        new("Comment", "id", true),
        new("Comment", "body", true),
        new("Comment", "updatedAt", false),
        new("Comment", "resolvedAt", false),
        new("ProjectMilestoneCreateInput", "projectId", true),
        new("ProjectMilestoneCreateInput", "name", true),
        new("IssueCreateInput", "teamId", true),
        new("IssueCreateInput", "title", true),
        new("IssueRelationCreateInput", "issueId", true),
        new("IssueRelationCreateInput", "relatedIssueId", true),
        new("IssueRelationCreateInput", "type", true),
        new("CommentCreateInput", "issueId", true),
        new("CommentCreateInput", "body", true),
    ];
}
