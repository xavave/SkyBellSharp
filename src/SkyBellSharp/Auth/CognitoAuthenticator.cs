using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SkyBellSharp.Auth;

/// <summary>
/// JWT authentication directly against the SkyBell Amazon Cognito user pool
/// (<c>USER_PASSWORD_AUTH</c> / <c>REFRESH_TOKEN_AUTH</c>), as the mobile app does.
/// Requires the public user-pool app client id. Prefer <see cref="SkyBellLoginAuthenticator"/>
/// unless you specifically need to mimic the app.
/// </summary>
public sealed class CognitoAuthenticator : TokenAuthenticatorBase
{
    private const string InitiateAuthTarget = "AWSCognitoIdentityProviderService.InitiateAuth";
    private static readonly MediaTypeHeaderValue AmzJson = MediaTypeHeaderValue.Parse("application/x-amz-json-1.1");

    private readonly HttpClient _http;
    private readonly CognitoSettings _settings;

    public CognitoAuthenticator(HttpClient http, CognitoSettings settings, SkyBellTokens tokens,
        JwtTokenKind tokenKind = JwtTokenKind.IdToken)
        : base(tokens, tokenKind)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Logs in with email + password and returns a ready-to-use authenticator.</summary>
    public static async Task<CognitoAuthenticator> LoginAsync(
        HttpClient http, CognitoSettings settings, string email, string password,
        JwtTokenKind tokenKind = JwtTokenKind.IdToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ClientId);

        var body = new
        {
            AuthFlow = "USER_PASSWORD_AUTH",
            ClientId = settings.ClientId,
            AuthParameters = new { USERNAME = email.Trim(), PASSWORD = password },
            // Sent by the official app (legacy password migration trigger).
            ClientMetadata = new { pwForLegacyVerify = password }
        };

        var result = await InitiateAuthAsync(http, settings, body, cancellationToken).ConfigureAwait(false);
        var tokens = SkyBellTokens.From(result);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
            throw new SkyBellApiException("Cognito login returned no refresh token.");
        return new CognitoAuthenticator(http, settings, tokens, tokenKind);
    }

    protected override async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var body = new
        {
            AuthFlow = "REFRESH_TOKEN_AUTH",
            ClientId = _settings.ClientId,
            AuthParameters = new { REFRESH_TOKEN = Tokens.RefreshToken }
        };
        var result = await InitiateAuthAsync(_http, _settings, body, cancellationToken).ConfigureAwait(false);
        Tokens.Apply(result);
    }

    private static async Task<AuthenticationResult> InitiateAuthAsync(
        HttpClient http, CognitoSettings settings, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
        req.Content.Headers.ContentType = AmzJson;
        req.Headers.TryAddWithoutValidation("X-Amz-Target", InitiateAuthTarget);
        req.Headers.TryAddWithoutValidation("X-Amz-User-Agent", settings.AmzUserAgent);

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        var payload = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            string? msg = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString();
            }
            catch (JsonException) { }
            throw new SkyBellApiException($"Cognito InitiateAuth failed ({(int)resp.StatusCode}): {msg ?? "unknown error"}",
                resp.StatusCode, msg, SkyBellHttp.Truncate(payload));
        }

        using var ok = JsonDocument.Parse(payload);
        if (!ok.RootElement.TryGetProperty("AuthenticationResult", out var ar))
        {
            var challenge = ok.RootElement.TryGetProperty("ChallengeName", out var c) ? c.GetString() : null;
            throw new SkyBellApiException($"Cognito requires challenge '{challenge ?? "?"}' (not supported).",
                resp.StatusCode, responseBody: SkyBellHttp.Truncate(payload));
        }
        return ar.Deserialize<AuthenticationResult>()
               ?? throw new SkyBellApiException("Unreadable Cognito AuthenticationResult.");
    }
}

/// <summary>SkyBell Cognito user-pool settings.</summary>
public sealed class CognitoSettings
{
    /// <summary>AWS region of the user pool.</summary>
    public string Region { get; set; } = "us-east-2";

    /// <summary>Public app client id of the user pool (as sent by the mobile app).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>X-Amz-User-Agent header value.</summary>
    public string AmzUserAgent { get; set; } = "aws-amplify/5.0.4 react-native";

    public Uri Endpoint => new($"https://cognito-idp.{Region}.amazonaws.com/");
}
