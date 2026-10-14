using System.Text.Json;
using System.Text.Json.Serialization;
using SkyBellSharp.Json;

namespace SkyBellSharp.Models;

/// <summary>Event types used by activities and webhook triggers.</summary>
public static class SkyBellEventTypes
{
    /// <summary>Doorbell button press.</summary>
    public const string Doorbell = "doorbell";
    /// <summary>Motion detected.</summary>
    public const string Motion = "motion";
    /// <summary>Live stream (on-demand view).</summary>
    public const string LiveStream = "lstream";
    /// <summary>Sound detected.</summary>
    public const string Sound = "sound";

    // Trigger-only events
    public const string Telemetry = "telemetry";
    public const string Availability = "availability";
    public const string Ota = "ota";
    /// <summary>An activity video finished uploading and can be downloaded.</summary>
    public const string VideoReady = "vready";
    public const string DeviceDeleted = "devicedeleted";
}

/// <summary>An event in the history (<c>GET /api/v5/activity</c>).</summary>
public sealed class SkyBellActivity
{
    [JsonPropertyName("activity_id")] public string ActivityId { get; set; } = "";
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("device_name")] public string? DeviceName { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }

    /// <summary>See <see cref="SkyBellEventTypes"/>: doorbell, motion, lstream, sound.</summary>
    [JsonPropertyName("event_type")] public string? EventType { get; set; }

    /// <summary>Event time (the API uses Unix ms, sometimes seconds or ISO strings).</summary>
    [JsonPropertyName("event_time")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? EventTime { get; set; }

    /// <summary>Day as "MM/DD/YYYY".</summary>
    [JsonPropertyName("date")] public JsonElement? Date { get; set; }

    /// <summary>
    /// API path of the video, e.g. "/activity/{id}/video" (older responses gave a pre-signed S3 URL).
    /// Use <see cref="SkyBellClient.GetActivityVideoUrlAsync"/> to get a downloadable URL.
    /// </summary>
    [JsonPropertyName("video_url")] public string? VideoUrl { get; set; }

    /// <summary>True once the clip has been uploaded and can be downloaded.</summary>
    [JsonPropertyName("video_ready")] public bool? VideoReady { get; set; }

    /// <summary>Clip size in bytes.</summary>
    [JsonPropertyName("video_size")] public long? VideoSize { get; set; }

    [JsonPropertyName("video_ready_time")] public DateTimeOffset? VideoReadyTime { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Base64 JPEG preview. Only returned when the queried range fits in a single day and
    /// <see cref="ActivityQuery.IncludePreviews"/> is true.
    /// </summary>
    [JsonPropertyName("image")] public string? ImageBase64 { get; set; }

    /// <summary>Pre-signed URLs attached to the event (e.g. "…_notify_preview.jpeg"), valid ~5 min.</summary>
    [JsonPropertyName("events")] public List<string>? EventUrls { get; set; }

    /// <summary>What the device detected, e.g. ["motion", "lstream"].</summary>
    [JsonPropertyName("edge_tags")] public List<string>? EdgeTags { get; set; }
    [JsonPropertyName("ai_ppe")] public JsonElement? AiPpe { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Pre-signed preview JPEG URL from <see cref="EventUrls"/>, if any.</summary>
    [JsonIgnore]
    public string? PreviewUrl =>
        EventUrls?.FirstOrDefault(u => u.Contains("preview", StringComparison.OrdinalIgnoreCase))
        ?? EventUrls?.FirstOrDefault(u => u.Contains(".jp", StringComparison.OrdinalIgnoreCase));

    /// <summary>Decoded <see cref="ImageBase64"/> (null if absent or invalid).</summary>
    public byte[]? GetImageBytes() => Base64Image.TryDecode(ImageBase64);

    public bool IsDoorbell => string.Equals(EventType, SkyBellEventTypes.Doorbell, StringComparison.OrdinalIgnoreCase);
    public bool IsMotion => string.Equals(EventType, SkyBellEventTypes.Motion, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{EventTime:u} {EventType} {DeviceName ?? DeviceId}";
}

/// <summary>Page of activities.</summary>
public sealed class ActivityPage
{
    /// <summary>Total number of matching activities.</summary>
    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("rows")] public List<SkyBellActivity> Rows { get; set; } = new();

    [JsonPropertyName("offset")] public int? Offset { get; set; }

    /// <summary>Limit applied by the server (it may ignore the requested one, e.g. 100000).</summary>
    [JsonPropertyName("limit")] public int? Limit { get; set; }

    /// <summary>
    /// False when the range spans several days: the API then omits the base64 previews
    /// ("Requested multiple days. NOT returning images.").
    /// </summary>
    [JsonPropertyName("imagesIncluded")] public bool? ImagesIncluded { get; set; }

    [JsonPropertyName("start")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? Start { get; set; }

    [JsonPropertyName("end")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public DateTimeOffset? End { get; set; }
}

/// <summary>Filters for <c>GET /api/v5/activity</c>. All optional.</summary>
public sealed class ActivityQuery
{
    /// <summary>Max rows (API default 500).</summary>
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public string? DeviceId { get; set; }

    /// <summary>See <see cref="SkyBellEventTypes"/>.</summary>
    public string? EventType { get; set; }

    /// <summary>Range start. Without it the API only returns the <b>last 7 days</b>.</summary>
    public DateTimeOffset? Start { get; set; }

    public DateTimeOffset? End { get; set; }

    /// <summary>
    /// False (default) adds <c>nopreviews=true</c>, which makes responses much smaller.
    /// Previews are only returned when [Start, End] fits in a single day anyway.
    /// </summary>
    public bool IncludePreviews { get; set; }

    internal string ToQueryString()
    {
        var q = new List<string>();
        if (Limit is { } l) q.Add("limit=" + l);
        if (Offset is { } o) q.Add("offset=" + o);
        if (!string.IsNullOrWhiteSpace(DeviceId)) q.Add("device=" + Uri.EscapeDataString(DeviceId));
        if (!string.IsNullOrWhiteSpace(EventType)) q.Add("type=" + Uri.EscapeDataString(EventType));
        if (Start is { } s) q.Add("start=" + s.ToUnixTimeMilliseconds());
        if (End is { } e) q.Add("end=" + e.ToUnixTimeMilliseconds());
        if (!IncludePreviews) q.Add("nopreviews=true");
        return q.Count == 0 ? "" : "?" + string.Join("&", q);
    }
}

/// <summary>Number of events for one day (<c>GET /api/v5/activity/day</c>).</summary>
public sealed class ActivityDayCount
{
    public ActivityDayCount(DateOnly day, int eventCount)
    {
        Day = day;
        EventCount = eventCount;
    }

    public DateOnly Day { get; }
    public int EventCount { get; }

    public override string ToString() => $"{Day:yyyy-MM-dd}: {EventCount}";
}

internal static class Base64Image
{
    public static byte[]? TryDecode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var b64 = value.Trim();
        var comma = b64.IndexOf(',');
        if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) b64 = b64[(comma + 1)..];
        b64 = b64.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
        }
        try { return Convert.FromBase64String(b64); }
        catch (FormatException) { return null; }
    }
}
