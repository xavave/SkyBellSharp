using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary>Result of <c>POST /api/v5/devices/{id}/share</c>.</summary>
public sealed class ShareInvite
{
    [JsonPropertyName("invite_token")] public string? InviteToken { get; set; }
    [JsonPropertyName("last_invite_sent")] public DateTimeOffset? LastInviteSent { get; set; }
    [JsonPropertyName("can_resend_at")] public DateTimeOffset? CanResendAt { get; set; }

    /// <summary>API message ("Device share created, invite sent.", "Invite already exists. Resending."…).</summary>
    [JsonIgnore] public string? Message { get; set; }

    /// <summary>True when a new invite was created (HTTP 201).</summary>
    [JsonIgnore] public bool Created { get; set; }

    /// <summary>True when an invite email was (re)sent.</summary>
    [JsonIgnore] public bool EmailSent { get; set; }
}

/// <summary>An entry of <c>GET /api/v5/shares</c>. The format is not documented; raw fields are kept in <see cref="Extra"/>.</summary>
public sealed class DeviceShare
{
    [JsonPropertyName("share_id")] public string? ShareId { get; set; }
    [JsonPropertyName("id")] public string? IdAlias { get; set; }
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("invited_email")] public string? InvitedEmail { get; set; }
    [JsonPropertyName("readOnly")] public bool? ReadOnly { get; set; }

    [JsonIgnore] public string? Id => ShareId ?? IdAlias;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Per-user notification settings of a shared device.</summary>
public sealed class SharedDeviceNotifications
{
    [JsonPropertyName("button_pressed")] public bool? ButtonPressed { get; set; }
    [JsonPropertyName("motion_detection")] public bool? MotionDetection { get; set; }
}
