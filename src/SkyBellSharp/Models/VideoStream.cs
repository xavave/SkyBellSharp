using System.Text.Json;
using System.Text.Json.Serialization;
using SkyBellSharp.Json;

namespace SkyBellSharp.Models;

/// <summary>
/// Live session returned by <c>POST /api/v5/devices/{id}/videostream</c>: everything a
/// WebRTC client needs to join the device's Amazon Kinesis Video Streams signaling channel
/// as a VIEWER (signaling endpoints, ICE servers, temporary AWS credentials).
/// </summary>
public sealed class VideoStreamSession
{
    [JsonPropertyName("state")] public bool State { get; set; }

    /// <summary>STUN/TURN servers, directly usable as RTCIceServer entries.</summary>
    [JsonPropertyName("ice")] public List<IceServer> IceServers { get; set; } = new();

    [JsonPropertyName("endpoints")] public List<SignalingEndpoint> Endpoints { get; set; } = new();

    /// <summary>KVS channel ARN ("…:channel/{serial}_{deviceId}/…").</summary>
    [JsonPropertyName("channelARN")] public string? ChannelArn { get; set; }

    /// <summary>Temporary AWS credentials used to SigV4-sign the signaling WebSocket.</summary>
    [JsonPropertyName("credentials")] public AwsCredentials? Credentials { get; set; }

    [JsonPropertyName("aws_region")] public string? AwsRegion { get; set; }

    /// <summary>Pre-signed WSS URL, when provided.</summary>
    [JsonPropertyName("signedWSS")] public string? SignedWss { get; set; }

    /// <summary>Pre-signed HTTPS URL, when provided.</summary>
    [JsonPropertyName("signedHTTPS")] public string? SignedHttps { get; set; }

    /// <summary>Pre-computed signing descriptors (JSON strings), when provided.</summary>
    [JsonPropertyName("signed")] public List<string>? Signed { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>WSS signaling endpoint.</summary>
    [JsonIgnore]
    public string? WssEndpoint => Endpoints.FirstOrDefault(e =>
        string.Equals(e.Protocol, "WSS", StringComparison.OrdinalIgnoreCase))?.ResourceEndpoint;

    /// <summary>HTTPS signaling endpoint.</summary>
    [JsonIgnore]
    public string? HttpsEndpoint => Endpoints.FirstOrDefault(e =>
        string.Equals(e.Protocol, "HTTPS", StringComparison.OrdinalIgnoreCase))?.ResourceEndpoint;

    /// <summary>Region from <see cref="AwsRegion"/>, or parsed from the channel ARN.</summary>
    [JsonIgnore]
    public string? Region
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AwsRegion)) return AwsRegion;
            var parts = ChannelArn?.Split(':');
            return parts is { Length: > 3 } ? parts[3] : null;
        }
    }
}

public sealed class SignalingEndpoint
{
    /// <summary>"WSS" or "HTTPS".</summary>
    [JsonPropertyName("Protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("ResourceEndpoint")] public string? ResourceEndpoint { get; set; }
}

public sealed class AwsCredentials
{
    [JsonPropertyName("AccessKeyId")] public string? AccessKeyId { get; set; }
    [JsonPropertyName("SecretAccessKey")] public string? SecretAccessKey { get; set; }
    [JsonPropertyName("SessionToken")] public string? SessionToken { get; set; }
    [JsonPropertyName("Expiration")] public DateTimeOffset? Expiration { get; set; }
}

/// <summary>ICE server; <c>urls</c> may be a string or an array in the API.</summary>
public sealed class IceServer
{
    [JsonPropertyName("urls")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public IReadOnlyList<string> Urls { get; set; } = Array.Empty<string>();

    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("credential")] public string? Credential { get; set; }
}

/// <summary><c>GET /api/v5/devices/{id}/snapshot</c>.</summary>
public sealed class Snapshot
{
    public Snapshot(byte[] jpeg, DateTimeOffset? takenAt)
    {
        Jpeg = jpeg;
        TakenAt = takenAt;
    }

    /// <summary>JPEG bytes.</summary>
    public byte[] Jpeg { get; }

    /// <summary>Capture time reported by the API.</summary>
    public DateTimeOffset? TakenAt { get; }
}
