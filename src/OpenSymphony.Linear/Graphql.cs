using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Linear;

// ht: Port of older/crates/opensymphony-linear/src/graphql.rs DTOs.
//   Uses camelCase JSON (Linear API native) via JsonPropertyName attributes.

// =============================================================================
// Mutation input DTOs (public — serialized into GraphQL variables).
//   skip_serializing_if = Option::is_none → omit null fields.
// =============================================================================

public sealed class ProjectMilestoneCreateInput
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("targetDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDate { get; set; }

    [JsonPropertyName("sortOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SortOrder { get; set; }
}

public sealed class ProjectMilestoneUpdateInput
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("targetDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDate { get; set; }

    [JsonPropertyName("sortOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SortOrder { get; set; }
}

public sealed class IssueCreateInput
{
    [JsonPropertyName("teamId")]
    public string TeamId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Priority { get; set; }

    [JsonPropertyName("estimate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Estimate { get; set; }

    [JsonPropertyName("stateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StateId { get; set; }

    [JsonPropertyName("assigneeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssigneeId { get; set; }

    [JsonPropertyName("projectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; set; }

    [JsonPropertyName("projectMilestoneId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectMilestoneId { get; set; }

    [JsonPropertyName("parentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    [JsonPropertyName("labelIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LabelIds { get; set; }
}

public sealed class IssueUpdateInput
{
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Priority { get; set; }

    [JsonPropertyName("estimate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Estimate { get; set; }

    [JsonPropertyName("stateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StateId { get; set; }

    [JsonPropertyName("assigneeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssigneeId { get; set; }

    [JsonPropertyName("projectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; set; }

    [JsonPropertyName("projectMilestoneId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectMilestoneId { get; set; }

    [JsonPropertyName("labelIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LabelIds { get; set; }
}

public sealed class CommentCreateInput
{
    [JsonPropertyName("issueId")]
    public string IssueId { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
}

public sealed class IssueRelationCreateInput
{
    [JsonPropertyName("issueId")]
    public string IssueId { get; set; } = "";

    [JsonPropertyName("relatedIssueId")]
    public string RelatedIssueId { get; set; } = "";

    [JsonPropertyName("type")]
    public string RelationType { get; set; } = "";
}

// =============================================================================
// GraphQL response envelope and error payloads.
// =============================================================================

internal sealed class GraphqlEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<GraphqlErrorPayload>? Errors { get; set; }
}

internal sealed class GraphqlErrorPayload
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("extensions")]
    public GraphqlErrorExtensions? Extensions { get; set; }
}

internal sealed class GraphqlErrorExtensions
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

// =============================================================================
// Connection / page info shared types.
// =============================================================================

internal sealed class PageInfo
{
    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }

    [JsonPropertyName("endCursor")]
    public string? EndCursor { get; set; }
}

internal sealed class IssuesConnection<T>
{
    [JsonPropertyName("nodes")]
    public List<T> Nodes { get; set; } = new();

    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();
}

// =============================================================================
// Query response data types.
// =============================================================================

internal sealed class IssuesByStateData
{
    [JsonPropertyName("issues")]
    public IssuesConnection<LinearIssueNode> Issues { get; set; } = new();
}

internal sealed class ProjectIssuesData
{
    [JsonPropertyName("issues")]
    public IssuesConnection<LinearIssueNode> Issues { get; set; } = new();
}

internal sealed class IssueByIdentifierData
{
    [JsonPropertyName("issue")]
    public LinearIssueNode? Issue { get; set; }
}

internal sealed class IssueStatesByIdsData
{
    [JsonPropertyName("issues")]
    public IssuesConnection<LinearIssueStateNode> Issues { get; set; } = new();
}

internal sealed class IssueInverseRelationsData
{
    [JsonPropertyName("issue")]
    public LinearIssueRelationsNode? Issue { get; set; }
}

internal sealed class IssueLabelsData
{
    [JsonPropertyName("issue")]
    public LinearIssueLabelsNode? Issue { get; set; }
}

internal sealed class IssueCommentsData
{
    [JsonPropertyName("issue")]
    public LinearIssueCommentsNode? Issue { get; set; }
}

internal sealed class IssueArchiveData
{
    [JsonPropertyName("issueArchive")]
    public IssueArchivePayload IssueArchive { get; set; } = new();
}

internal sealed class ProjectBySlugData
{
    [JsonPropertyName("projects")]
    public ProjectsConnection Projects { get; set; } = new();
}

internal sealed class ProjectUpdateContentData
{
    [JsonPropertyName("projectUpdate")]
    public ProjectUpdatePayload ProjectUpdate { get; set; } = new();
}

internal sealed class IssueArchivePayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

internal sealed class ProjectUpdatePayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

internal sealed class ProjectsConnection
{
    [JsonPropertyName("nodes")]
    public List<LinearProjectNode> Nodes { get; set; } = new();
}

internal sealed class LinearProjectNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slugId")]
    public string SlugId { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

// =============================================================================
// Issue node types (query responses).
// =============================================================================

internal sealed class LinearIssueNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    public double Priority { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("state")]
    public LinearWorkflowState State { get; set; } = new();

    [JsonPropertyName("parent")]
    public LinearParentNode? Parent { get; set; }

    [JsonPropertyName("projectMilestone")]
    public LinearProjectMilestoneNode? ProjectMilestone { get; set; }

    [JsonPropertyName("children")]
    public LinearChildConnection Children { get; set; } = new();

    [JsonPropertyName("labels")]
    public LinearLabelConnection Labels { get; set; } = new();

    [JsonPropertyName("inverseRelations")]
    public LinearRelationConnection InverseRelations { get; set; } = new();
}

internal sealed class LinearIssueRelationsNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("inverseRelations")]
    public LinearRelationConnection InverseRelations { get; set; } = new();
}

internal sealed class LinearIssueLabelsNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("labels")]
    public LinearLabelConnection Labels { get; set; } = new();
}

internal sealed class LinearIssueCommentsNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("comments")]
    public LinearCommentConnection Comments { get; set; } = new();
}

internal sealed class LinearIssueStateNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("state")]
    public LinearWorkflowState State { get; set; } = new();
}

internal sealed class LinearWorkflowState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Kind { get; set; } = "";
}

internal sealed class LinearParentNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string? Identifier { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("state")]
    public LinearIssueRefState? State { get; set; }
}

internal sealed class LinearProjectMilestoneNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class LinearChildConnection
{
    [JsonPropertyName("nodes")]
    public List<LinearChildNode> Nodes { get; set; } = new();
}

internal sealed class LinearChildNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("state")]
    public LinearIssueRefState State { get; set; } = new();
}

internal sealed class LinearIssueRefState
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class LinearLabelConnection
{
    [JsonPropertyName("nodes")]
    public List<LinearLabelNode> Nodes { get; set; } = new();

    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();
}

internal sealed class LinearLabelNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class LinearCommentConnection
{
    [JsonPropertyName("nodes")]
    public List<LinearCommentNode> Nodes { get; set; } = new();

    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();
}

internal sealed class LinearCommentNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("resolvedAt")]
    public DateTimeOffset? ResolvedAt { get; set; }
}

internal sealed class LinearRelationConnection
{
    [JsonPropertyName("nodes")]
    public List<LinearRelationNode> Nodes { get; set; } = new();

    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();
}

internal sealed class LinearRelationNode
{
    [JsonPropertyName("type")]
    public string RelationType { get; set; } = "";

    [JsonPropertyName("issue")]
    public LinearBlockerNode Issue { get; set; } = new();
}

internal sealed class LinearBlockerNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("state")]
    public LinearWorkflowState State { get; set; } = new();
}

// =============================================================================
// Mutation response data types.
// =============================================================================

internal sealed class ProjectMilestoneCreateData
{
    [JsonPropertyName("projectMilestoneCreate")]
    public ProjectMilestoneMutationPayload ProjectMilestoneCreate { get; set; } = new();
}

internal sealed class ProjectMilestoneUpdateData
{
    [JsonPropertyName("projectMilestoneUpdate")]
    public ProjectMilestoneMutationPayload ProjectMilestoneUpdate { get; set; } = new();
}

internal sealed class IssueCreateData
{
    [JsonPropertyName("issueCreate")]
    public IssueMutationPayload IssueCreate { get; set; } = new();
}

internal sealed class IssueUpdateData
{
    [JsonPropertyName("issueUpdate")]
    public IssueMutationPayload IssueUpdate { get; set; } = new();
}

internal sealed class CommentCreateData
{
    [JsonPropertyName("commentCreate")]
    public CommentMutationPayload CommentCreate { get; set; } = new();
}

internal sealed class IssueRelationCreateData
{
    [JsonPropertyName("issueRelationCreate")]
    public IssueRelationMutationPayload IssueRelationCreate { get; set; } = new();
}

internal sealed class ProjectMilestoneMutationPayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("projectMilestone")]
    public ProjectMilestoneMutationNode? ProjectMilestone { get; set; }
}

internal sealed class ProjectMilestoneMutationNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("targetDate")]
    public string? TargetDate { get; set; }

    [JsonPropertyName("sortOrder")]
    public double? SortOrder { get; set; }

    [JsonPropertyName("project")]
    public LinearProjectNode Project { get; set; } = new();
}

internal sealed class IssueMutationPayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("issue")]
    public IssueMutationNode? Issue { get; set; }
}

internal sealed class IssueMutationNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("priority")]
    public double? Priority { get; set; }

    [JsonPropertyName("estimate")]
    public double? Estimate { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("state")]
    public LinearWorkflowState State { get; set; } = new();

    [JsonPropertyName("project")]
    public LinearProjectNode? Project { get; set; }

    [JsonPropertyName("projectMilestone")]
    public LinearProjectMilestoneNode? ProjectMilestone { get; set; }

    [JsonPropertyName("parent")]
    public LinearParentRefNode? Parent { get; set; }

    [JsonPropertyName("assignee")]
    public LinearAssigneeNode? Assignee { get; set; }

    [JsonPropertyName("labels")]
    public LinearLabelMutationConnection Labels { get; set; } = new();
}

internal sealed class LinearParentRefNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";
}

internal sealed class LinearAssigneeNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal sealed class LinearLabelMutationConnection
{
    [JsonPropertyName("nodes")]
    public List<LinearLabelRefNode> Nodes { get; set; } = new();
}

internal sealed class LinearLabelRefNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal sealed class CommentMutationPayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("comment")]
    public CommentMutationNode? Comment { get; set; }
}

internal sealed class CommentMutationNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("issue")]
    public IssueRefNode Issue { get; set; } = new();
}

internal sealed class IssueRefNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";
}

internal sealed class IssueRelationMutationPayload
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("issueRelation")]
    public IssueRelationMutationNode? IssueRelation { get; set; }
}

internal sealed class IssueRelationMutationNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string RelationType { get; set; } = "";

    [JsonPropertyName("issue")]
    public IssueRefNode Issue { get; set; } = new();

    [JsonPropertyName("relatedIssue")]
    public IssueRefNode RelatedIssue { get; set; } = new();
}
