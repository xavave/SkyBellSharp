using System.Text.Json.Serialization;

namespace SkyBellSharp.Auth;

/// <summary>JWT session (Cognito tokens). Serializable so applications can persist it.</summary>
public sealed class SkyBellTokens
{
    public string IdToken { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>True when the token expires within <paramref name="margin"/> (default 5 min).</summary>
    public bool IsExpired(TimeSpan? margin = null) =>
        DateTimeOffset.UtcNow >= ExpiresAt - (margin ?? TimeSpan.FromMinutes(5));

    /// <summary>Applies a login/refresh result; keeps the existing refresh token if none is returned.</summary>
    public void Apply(AuthenticationResult result)
    {
        if (!string.IsNullOrEmpty(result.IdToken)) IdToken = result.IdToken;
        if (!string.IsNullOrEmpty(result.AccessToken)) AccessToken = result.AccessToken;
        if (!string.IsNullOrEmpty(result.RefreshToken)) RefreshToken = result.RefreshToken;
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn > 0 ? result.ExpiresIn : 3600);
    }

    public static SkyBellTokens From(AuthenticationResult result)
    {
        var t = new SkyBellTokens();
        t.Apply(result);
        return t;
    }
}

/// <summary>Cognito <c>AuthenticationResult</c> (also wrapped by <c>POST /api/v5/login</c>).</summary>
public sealed class AuthenticationResult
{
    [JsonPropertyName("IdToken")] public string? IdToken { get; set; }
    [JsonPropertyName("AccessToken")] public string? AccessToken { get; set; }
    [JsonPropertyName("RefreshToken")] public string? RefreshToken { get; set; }
    [JsonPropertyName("ExpiresIn")] public int ExpiresIn { get; set; }
    [JsonPropertyName("TokenType")] public string? TokenType { get; set; }
}

/// <summary>Which Cognito token goes into the <c>Authorization</c> header.</summary>
public enum JwtTokenKind
{
    /// <summary>IdToken (what the SkyBell docs and the iOS app use).</summary>
    IdToken,
    /// <summary>AccessToken.</summary>
    AccessToken
}
