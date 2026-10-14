using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary>A trigger (webhook, IFTTT, Alexa, Google…) from <c>GET /api/v5/devices/{id}/triggers</c>.</summary>
public sealed class SkyBellTrigger
{
    [JsonPropertyName("trigger_id")] public string TriggerId { get; set; } = "";
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }

    /// <summary>"webhook", "ifttt", "google", "amazon"…</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("on_events")] public List<string> Events { get; set; } = new();

    /// <summary>Consecutive failures; the API disables a trigger after 25.</summary>
    [JsonPropertyName("fail_count")] public int FailCount { get; set; }

    [JsonPropertyName("disabled_on")] public DateTimeOffset? DisabledOn { get; set; }
    [JsonPropertyName("disabled_reason")] public string? DisabledReason { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Webhook definition for <c>POST /api/v5/devices/{id}/triggers</c>.
/// Triggers cannot be edited: delete and recreate them.
/// </summary>
public sealed class WebhookTrigger
{
    [JsonPropertyName("type")] public string Type { get; init; } = "webhook";

    /// <summary>URL called by SkyBell.</summary>
    [JsonPropertyName("url")] public required string Url { get; init; }

    /// <summary>"post" (default) or "get".</summary>
    [JsonPropertyName("method")] public string Method { get; init; } = "post";

    /// <summary>
    /// Events that fire the webhook (see <see cref="SkyBellEventTypes"/>); null = all events.
    /// </summary>
    [JsonPropertyName("events")] public IReadOnlyList<string>? Events { get; init; }

    /// <summary>Optional custom header (e.g. a shared secret) added to each call.</summary>
    [JsonPropertyName("header_name")] public string? HeaderName { get; init; }
    [JsonPropertyName("header_value")] public string? HeaderValue { get; init; }
}
