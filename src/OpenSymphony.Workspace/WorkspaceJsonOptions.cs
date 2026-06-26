using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSymphony.Workspace;

// ht: chrono DateTime<Utc> serde emits "yyyy-MM-ddTHH:mm:ssZ" for whole seconds
//   and "yyyy-MM-ddTHH:mm:ss.fffZ" for fractional. STJ "o" round-trip always emits
//   7 fractional digits; we strip the .0000000 suffix when zero to match chrono.
public sealed class DateTimeOffsetUtcConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        var formatted = utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        writer.WriteStringValue(formatted);
    }
}

// ht: Workspace JSON options mirror DomainJsonOptions but with the chrono-compatible
//   DateTimeOffsetUtcConverter instead of TimestampMs/DurationMs converters.
public static class WorkspaceJsonOptions
{
    public static JsonSerializerOptions Default { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
                new DateTimeOffsetUtcConverter(),
            },
        };
        return options;
    }
}
