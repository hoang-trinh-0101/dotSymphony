using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Domain;

// ht: Rust #[serde(rename_all = "snake_case")] — handled by JsonStringEnumConverter
//   with SnakeCaseLower policy in DomainJsonOptions.
public enum IssueStateCategory
{
    Active,
    NonActive,
    Terminal,
}

// ht: Rust struct fields are already snake_case; C# PascalCase maps via SnakeCaseLower
//   policy. No skip → Option None serializes as null (DefaultIgnoreCondition.Never).
public sealed record IssueState(
    StringIdentifier<TrackerStateId>? Id,
    string Name,
    IssueStateCategory Category);

public sealed record BlockerRef(
    StringIdentifier<IssueId>? Id,
    StringIdentifier<IssueIdentifier>? Identifier,
    string? State,
    TimestampMs? CreatedAt,
    TimestampMs? UpdatedAt);

public sealed record IssueRef(
    StringIdentifier<IssueId> Id,
    StringIdentifier<IssueIdentifier> Identifier,
    string State);

// ht: NormalizedIssue mirrors Rust skip_serializing_if rules exactly:
//   parent_id omitted when None, sub_issues omitted when empty, all other Option
//   fields serialize as null. A custom converter is the only faithful way to match
//   both Option::is_none and Vec::is_empty skip rules under STJ.
[JsonConverter(typeof(NormalizedIssueConverter))]
public sealed record NormalizedIssue(
    StringIdentifier<IssueId> Id,
    StringIdentifier<IssueIdentifier> Identifier,
    string Title,
    string? Description,
    byte? Priority,
    IssueState State,
    string? BranchName,
    string? Url,
    List<string> Labels,
    StringIdentifier<IssueId>? ParentId,
    List<BlockerRef> BlockedBy,
    List<IssueRef> SubIssues,
    TimestampMs? CreatedAt,
    TimestampMs? UpdatedAt);

public sealed class NormalizedIssueConverter : JsonConverter<NormalizedIssue>
{
    public override NormalizedIssue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        StringIdentifier<IssueId>? parentId = null;
        if (root.TryGetProperty("parent_id", out var pidEl) && pidEl.ValueKind != JsonValueKind.Null)
            parentId = StringIdentifier<IssueId>.New(pidEl.GetString()!).Value;

        var subIssues = new List<IssueRef>();
        if (root.TryGetProperty("sub_issues", out var siEl) && siEl.ValueKind == JsonValueKind.Array)
            subIssues = siEl.Deserialize<List<IssueRef>>(options)!;

        return new NormalizedIssue(
            Id: StringIdentifier<IssueId>.New(root.GetProperty("id").GetString()!).Value,
            Identifier: StringIdentifier<IssueIdentifier>.New(root.GetProperty("identifier").GetString()!).Value,
            Title: root.GetProperty("title").GetString()!,
            Description: root.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null,
            Priority: root.TryGetProperty("priority", out var p) && p.ValueKind != JsonValueKind.Null ? (byte?)p.GetByte() : null,
            State: root.GetProperty("state").Deserialize<IssueState>(options)!,
            BranchName: root.TryGetProperty("branch_name", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : null,
            Url: root.TryGetProperty("url", out var u) && u.ValueKind != JsonValueKind.Null ? u.GetString() : null,
            Labels: root.GetProperty("labels").Deserialize<List<string>>(options)!,
            ParentId: parentId,
            BlockedBy: root.GetProperty("blocked_by").Deserialize<List<BlockerRef>>(options)!,
            SubIssues: subIssues,
            CreatedAt: root.TryGetProperty("created_at", out var c) && c.ValueKind != JsonValueKind.Null ? TimestampMs.New(c.GetUInt64()) : null,
            UpdatedAt: root.TryGetProperty("updated_at", out var up) && up.ValueKind != JsonValueKind.Null ? TimestampMs.New(up.GetUInt64()) : null);
    }

    public override void Write(Utf8JsonWriter writer, NormalizedIssue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id.Value);
        writer.WriteString("identifier", value.Identifier.Value);
        writer.WriteString("title", value.Title);
        WriteNullableString(writer, "description", value.Description);
        if (value.Priority is byte pri) writer.WriteNumber("priority", pri); else writer.WriteNull("priority");
        writer.WritePropertyName("state");
        JsonSerializer.Serialize(writer, value.State, options);
        WriteNullableString(writer, "branch_name", value.BranchName);
        WriteNullableString(writer, "url", value.Url);
        writer.WritePropertyName("labels");
        JsonSerializer.Serialize(writer, value.Labels, options);
        // ht: skip_serializing_if = Option::is_none → omit parent_id when null.
        if (value.ParentId is not null)
            writer.WriteString("parent_id", value.ParentId.Value.Value);
        writer.WritePropertyName("blocked_by");
        JsonSerializer.Serialize(writer, value.BlockedBy, options);
        // ht: skip_serializing_if = Vec::is_empty → omit sub_issues when empty.
        if (value.SubIssues.Count > 0)
        {
            writer.WritePropertyName("sub_issues");
            JsonSerializer.Serialize(writer, value.SubIssues, options);
        }
        if (value.CreatedAt is TimestampMs ca) writer.WriteNumber("created_at", ca.AsU64()); else writer.WriteNull("created_at");
        if (value.UpdatedAt is TimestampMs ua) writer.WriteNumber("updated_at", ua.AsU64()); else writer.WriteNull("updated_at");
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null) writer.WriteString(name, value); else writer.WriteNull(name);
    }
}
