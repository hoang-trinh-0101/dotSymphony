using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSymphony.Domain;

namespace OpenSymphony.Control;

// ht: shared snake_case JSON options for control-plane wire contract.
public static class ControlPlaneJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
