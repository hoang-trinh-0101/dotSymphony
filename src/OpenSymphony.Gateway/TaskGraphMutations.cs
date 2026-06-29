using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;
using OpenSymphony.GatewaySchema;

namespace OpenSymphony.Gateway;

// ht: minimal port of task-graph mutation types and client interface.
//   Focus: DTOs, validation, and mutation client trait for testability.

// =============================================================================
// Request DTOs
// =============================================================================

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MilestoneOp
{
    Create,
    Update,
}

public sealed record TaskGraphMilestoneRequest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("op")] MilestoneOp Op,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("milestoneId")] string? MilestoneId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("targetDate")] string? TargetDate,
    [property: JsonPropertyName("sortOrder")] double? SortOrder
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueOp
{
    Create,
    Update,
}

public sealed record TaskGraphIssueRequest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("op")] IssueOp Op,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey,
    [property: JsonPropertyName("teamId")] string TeamId,
    [property: JsonPropertyName("issueId")] string? IssueId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] double? Priority,
    [property: JsonPropertyName("estimate")] double? Estimate,
    [property: JsonPropertyName("assigneeId")] string? AssigneeId,
    [property: JsonPropertyName("projectId")] string? ProjectId,
    [property: JsonPropertyName("projectMilestoneId")] string? ProjectMilestoneId,
    [property: JsonPropertyName("labelIds")] List<string>? LabelIds
);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubIssueOp
{
    Create,
    Update,
}

public sealed record TaskGraphSubIssueRequest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("op")] SubIssueOp Op,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey,
    [property: JsonPropertyName("teamId")] string TeamId,
    [property: JsonPropertyName("parentId")] string ParentId,
    [property: JsonPropertyName("subIssueId")] string? SubIssueId,
    [property: JsonPropertyName("parentIdentifier")] string ParentIdentifier,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] double? Priority,
    [property: JsonPropertyName("estimate")] double? Estimate,
    [property: JsonPropertyName("assigneeId")] string? AssigneeId,
    [property: JsonPropertyName("projectId")] string? ProjectId,
    [property: JsonPropertyName("projectMilestoneId")] string? ProjectMilestoneId,
    [property: JsonPropertyName("labelIds")] List<string>? LabelIds
);

public sealed record TaskGraphRelationRequest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey,
    [property: JsonPropertyName("relationType")] string RelationType,
    [property: JsonPropertyName("issueId")] string IssueId,
    [property: JsonPropertyName("relatedIssueId")] string RelatedIssueId
);

public sealed record TaskGraphEvidenceRequest(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey,
    [property: JsonPropertyName("issueId")] string IssueId,
    [property: JsonPropertyName("body")] string Body
);

// =============================================================================
// Response DTOs
// =============================================================================

public sealed record TaskGraphMilestoneResponse(
    [property: JsonPropertyName("receipt")] ActionReceipt Receipt,
    [property: JsonPropertyName("milestoneId")] string? MilestoneId,
    [property: JsonPropertyName("milestoneName")] string? MilestoneName,
    [property: JsonPropertyName("projectId")] string? ProjectId
);

public sealed record TaskGraphIssueResponse(
    [property: JsonPropertyName("receipt")] ActionReceipt Receipt,
    [property: JsonPropertyName("issueId")] string? IssueId,
    [property: JsonPropertyName("issueIdentifier")] string? IssueIdentifier,
    [property: JsonPropertyName("stateId")] string? StateId,
    [property: JsonPropertyName("projectMilestoneId")] string? ProjectMilestoneId
);

public sealed record TaskGraphSubIssueResponse(
    [property: JsonPropertyName("receipt")] ActionReceipt Receipt,
    [property: JsonPropertyName("subIssueId")] string? SubIssueId,
    [property: JsonPropertyName("subIssueIdentifier")] string? SubIssueIdentifier,
    [property: JsonPropertyName("parentIdentifier")] string? ParentIdentifier,
    [property: JsonPropertyName("stateId")] string? StateId
);

public sealed record TaskGraphRelationResponse(
    [property: JsonPropertyName("receipt")] ActionReceipt Receipt,
    [property: JsonPropertyName("relationId")] string? RelationId,
    [property: JsonPropertyName("relationType")] string? RelationType,
    [property: JsonPropertyName("relatedIssueId")] string? RelatedIssueId
);

public sealed record TaskGraphEvidenceResponse(
    [property: JsonPropertyName("receipt")] ActionReceipt Receipt,
    [property: JsonPropertyName("commentId")] string? CommentId,
    [property: JsonPropertyName("issueId")] string? IssueId,
    [property: JsonPropertyName("issueIdentifier")] string? IssueIdentifier
);

// =============================================================================
// Mutation Client Interface
// =============================================================================

public interface ILinearMutationClient
{
    Task<Result<TaskGraphMilestoneResponse, MutationError>> CreateOrUpdateProjectMilestone(
        TaskGraphMilestoneRequest request,
        string correlationId);

    Task<Result<TaskGraphIssueResponse, MutationError>> CreateOrUpdateIssue(
        TaskGraphIssueRequest request,
        string correlationId);

    Task<Result<TaskGraphSubIssueResponse, MutationError>> CreateOrUpdateSubIssue(
        TaskGraphSubIssueRequest request,
        string correlationId);

    Task<Result<TaskGraphRelationResponse, MutationError>> CreateIssueRelation(
        TaskGraphRelationRequest request,
        string correlationId);

    Task<Result<TaskGraphEvidenceResponse, MutationError>> CreateEvidenceComment(
        TaskGraphEvidenceRequest request,
        string correlationId);
}

// =============================================================================
// Mutation State for Router
// =============================================================================

public sealed record TaskGraphMutationState(
    InMemoryEventJournal Journal,
    ILinearMutationClient? LinearMutations
);

// =============================================================================
// Helper Functions
// =============================================================================

public static class TaskGraphMutationHelpers
{
    public static EntityKind EntityKindFor(ActionKind kind) => kind switch
    {
        ActionKind.TaskGraphMilestone => EntityKind.Milestone,
        ActionKind.TaskGraphIssue => EntityKind.Issue,
        ActionKind.TaskGraphSubIssue => EntityKind.SubIssue,
        ActionKind.TaskGraphRelation => EntityKind.Issue,
        ActionKind.TaskGraphEvidence => EntityKind.Issue,
        _ => EntityKind.Unknown
    };

    public static async Task<EventRecord> AppendMutationEvent(
        InMemoryEventJournal journal,
        string correlationId,
        ActionKind actionKind,
        EntityRef entityRef,
        JsonElement payload)
    {
        var evt = new EventRecordBuilder()
            .EventId(Guid.NewGuid().ToString())
            .Actor(EventActor.System("gateway"))
            .CorrelationId(correlationId)
            .Kind(EventKind.HarnessEventNormalized(actionKind.AsString()))
            .EntityRefs(new List<EntityRef> { entityRef })
            .Summary($"Task graph mutation {actionKind} completed")
            .Payload(payload)
            .Build();

        return await journal.Append(evt);
    }
}