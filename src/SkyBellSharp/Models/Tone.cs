using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary>A downloadable tone (<c>GET /api/v5/tones</c>, <c>GET /api/v5/tones/chime</c>).</summary>
public sealed class SkyBellTone
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("modified")] public DateTimeOffset? Modified { get; set; }

    /// <summary>File name to pass to <see cref="SkyBellClient.SetToneAsync"/>.</summary>
    [JsonPropertyName("file")] public string? FileAlias { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("classicDingDong")] public bool? ClassicDingDong { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>File name (from <c>file</c>, or the last segment of <see cref="Url"/>).</summary>
    [JsonIgnore]
    public string? File => FileAlias ?? (Url is null ? null : Uri.UnescapeDataString(Url[(Url.LastIndexOf('/') + 1)..]));

    public override string ToString() => Name ?? File ?? Url ?? "?";
}

/// <summary>What a tone is used for (<c>tone_type</c>).</summary>
public enum ToneType
{
    Test,
    Button,
    Doorbell,
    Motion
}

internal static class ToneTypeExtensions
{
    public static string ToApi(this ToneType t) => t switch
    {
        ToneType.Test => "test",
        ToneType.Button => "button",
        ToneType.Doorbell => "doorbell",
        ToneType.Motion => "motion",
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };
}
