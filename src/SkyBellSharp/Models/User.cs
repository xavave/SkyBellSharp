using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBellSharp.Models;

/// <summary><c>GET /api/v5/user</c>.</summary>
public sealed class SkyBellUser
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("fname")] public string? FirstName { get; set; }
    [JsonPropertyName("lname")] public string? LastName { get; set; }
    [JsonPropertyName("user_since")] public DateTimeOffset? UserSince { get; set; }
    [JsonPropertyName("eula_accepted")] public DateTimeOffset? EulaAccepted { get; set; }

    /// <summary>"free" or "premium".</summary>
    [JsonPropertyName("premium_status")] public string? PremiumStatus { get; set; }

    [JsonPropertyName("premium")] public PremiumInfo? Premium { get; set; }

    public bool IsPremium => string.Equals(PremiumStatus, "premium", StringComparison.OrdinalIgnoreCase);

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PremiumInfo
{
    [JsonPropertyName("renewing")] public bool? Renewing { get; set; }
    [JsonPropertyName("term")] public int? Term { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("ended_at")] public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>Parameters of <c>POST /api/v5/user</c> (sign-up).</summary>
public sealed class NewUser
{
    [JsonPropertyName("email")] public required string Email { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
    [JsonPropertyName("fname")] public string? FirstName { get; init; }
    [JsonPropertyName("lname")] public string? LastName { get; init; }
}
