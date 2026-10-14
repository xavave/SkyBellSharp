using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary><c>GET /health</c> (no authentication).</summary>
public sealed class SkyBellHealth
{
    [JsonPropertyName("STARTED")] public DateTimeOffset? Started { get; set; }
    [JsonPropertyName("UPTIME")] public long? Uptime { get; set; }
    [JsonPropertyName("ENV")] public string? Environment { get; set; }
    [JsonPropertyName("VERSION")] public string? NodeVersion { get; set; }
    [JsonPropertyName("SKYBELL_API_VER")] public string? ApiVersion { get; set; }

    /// <summary>Current firmware per model (trim2, hd2, slim = SkyBell Micro…).</summary>
    [JsonPropertyName("CURRENT_FIRMWARE")] public Dictionary<string, string>? CurrentFirmware { get; set; }

    [JsonPropertyName("MOBILE_BETA")] public JsonElement? MobileBeta { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary><c>GET /api/v5/devices/provision</c>: tokens used to provision a new device.</summary>
public sealed class ProvisionInvite
{
    /// <summary>Invite token to send to the device (<c>POST /ap/provision</c>).</summary>
    [JsonPropertyName("token")] public string? Token { get; set; }

    [JsonPropertyName("account_id")] public string? AccountId { get; set; }

    /// <summary>Chime serial number -> PSK.</summary>
    [JsonPropertyName("chimes")] public Dictionary<string, string>? Chimes { get; set; }

    /// <summary>Tokens for legacy SkyBell HD devices (CoAP).</summary>
    [JsonPropertyName("legacy")] public LegacyProvisionTokens? Legacy { get; set; }
}

public sealed class LegacyProvisionTokens
{
    [JsonPropertyName("invite")] public string? Invite { get; set; }
    [JsonPropertyName("access")] public string? Access { get; set; }
}
