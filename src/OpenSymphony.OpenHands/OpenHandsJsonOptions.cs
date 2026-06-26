using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.OpenHands;

// ht: shared JSON options — snake_case naming, skip null, case-insensitive.
public static class OpenHandsJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}
