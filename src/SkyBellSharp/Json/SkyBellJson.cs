using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Json;

/// <summary>Shared serializer settings for SkyBell payloads.</summary>
public static class SkyBellJson
{
    /// <summary>Options used for every request/response body.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

/// <summary>
/// Reads a timestamp that SkyBell sends either as a number (Unix seconds or milliseconds,
/// sometimes as a string) or as an ISO-8601 string. Writes Unix milliseconds.
/// </summary>
public sealed class FlexibleTimestampConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var n) ? FromUnix(n)
                     : reader.TryGetDouble(out var d) ? FromUnix((long)d) : null;
            case JsonTokenType.String:
                return Parse(reader.GetString());
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value.ToUnixTimeMilliseconds());
    }

    /// <summary>Parses a SkyBell timestamp string (Unix s/ms digits or ISO-8601).</summary>
    public static DateTimeOffset? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return FromUnix(n);
        return DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dto) ? dto : null;
    }

    /// <summary>Values above 10^11 are treated as milliseconds, below as seconds.</summary>
    public static DateTimeOffset? FromUnix(long value)
    {
        if (value <= 0) return null;
        try
        {
            return value > 100_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

/// <summary>Accepts either a single JSON string or an array of strings.</summary>
public sealed class StringOrArrayConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return Array.Empty<string>();
        if (reader.TokenType == JsonTokenType.String) return new[] { reader.GetString() ?? "" };

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String) list.Add(reader.GetString() ?? "");
                else reader.Skip();
            }
            return list;
        }

        throw new JsonException("Expected a string or an array of strings.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}
