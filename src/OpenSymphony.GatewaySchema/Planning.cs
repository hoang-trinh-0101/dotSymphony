using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.GatewaySchema;

// ht: minimal port of planning types.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlanningArtifactKind
{
    Intake,
    ProjectContext,
    Requirements,
    ResearchBrief,
    CodebaseAnalysis,
    ArchitectureNotes,
    RiskRegister,
    MilestoneDraft,
    IssueDraft,
    SubIssueDraft,
    DependencyMap,
    VerificationPlan,
    AcceptanceCriteria,
    PlanValidation,
    LinearDraft,
    ReviewComments,
    PublishReceipt,
    PlanningWave,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnRole
{
    User,
    Agent,
    System,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlanningSessionStatus
{
    Draft,
    InReview,
    Approved,
    Published,
    Archived,
}

public sealed record PlanningArtifact(
    SchemaVersion SchemaVersion,
    string ArtifactId,
    string SessionId,
    PlanningArtifactKind Kind,
    string Title,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? GeneratedBy,
    bool Approved,
    bool PublishedToTracker
);

public sealed record ArtifactRevision(
    string RevisionId,
    string ArtifactId,
    uint Version,
    string ContentHash,
    string Content,
    DateTimeOffset CreatedAt,
    string? AuthoredBy,
    string? ChangeSummary
);

public sealed record ArtifactDiff(
    string DiffId,
    string ArtifactId,
    uint FromVersion,
    uint ToVersion,
    string UnifiedDiff,
    uint LinesAdded,
    uint LinesRemoved,
    string? Summary,
    DateTimeOffset GeneratedAt
);

public sealed record ReviewComment(
    string CommentId,
    string SessionId,
    string ArtifactId,
    string? RevisionId,
    string Author,
    string Body,
    bool Resolved,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record ConversationTurn(
    string TurnId,
    string SessionId,
    uint TurnNumber,
    TurnRole Role,
    string Content,
    DateTimeOffset CreatedAt,
    List<string> ArtifactsModified,
    JsonElement? Metadata
)
{
    public ConversationTurn() : this("", "", 0, default, "", DateTimeOffset.UtcNow, [], null) { }
}

public sealed record PlanningSession(
    SchemaVersion SchemaVersion,
    string SessionId,
    string ProjectId,
    string Title,
    PlanningSessionStatus Status,
    string? PlanningWave,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<ConversationTurn> Turns,
    List<PlanningArtifact> Artifacts,
    JsonElement? Metadata
)
{
    public PlanningSessionSummary Summary() => new(
        SchemaVersion,
        SessionId,
        ProjectId,
        Title,
        Status,
        PlanningWave,
        (uint)Turns.Count,
        (uint)Artifacts.Count,
        CreatedAt,
        UpdatedAt
    );
}

public sealed record PlanningSessionSummary(
    SchemaVersion SchemaVersion,
    string SessionId,
    string ProjectId,
    string Title,
    PlanningSessionStatus Status,
    string? PlanningWave,
    uint TurnCount,
    uint ArtifactCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);