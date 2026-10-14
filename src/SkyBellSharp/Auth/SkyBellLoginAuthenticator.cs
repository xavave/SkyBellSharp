using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyBellSharp.Json;

namespace SkyBellSharp.Auth;

/// <summary>
/// JWT authentication through the SkyBell API itself:
/// <c>POST /api/v5/login</c> (email + password) and <c>PUT /api/v5/token</c> (refresh).
/// No Cognito client id is needed.
/// </summary>
public sealed class SkyBellLoginAuthenticator : TokenAuthenticatorBase
{
    private readonly HttpClient _http;
    private readonly SkyBellClientOptions _options;

    public SkyBellLoginAuthenticator(HttpClient http, SkyBellTokens tokens,
        SkyBellClientOptions? options = null, JwtTokenKind tokenKind = JwtTokenKind.IdToken)
        : base(tokens, tokenKind)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? new SkyBellClientOptions();
    }

    /// <summary>Logs in with email + password and returns a ready-to-use authenticator.</summary>
    public static async Task<SkyBellLoginAuthenticator> LoginAsync(
        HttpClient http, string email, string password,
        SkyBellClientOptions? options = null, JwtTokenKind tokenKind = JwtTokenKind.IdToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);
        options ??= new SkyBellClientOptions();

        using var req = NewRequest(HttpMethod.Post, options, "/api/v5/login",
            new { username = email.Trim(), password });
        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var data = SkyBellHttp.ReadData(resp, body, "/api/v5/login");

        var resultNode = data?["AuthenticationResult"] ?? data;
        var result = resultNode?.Deserialize<AuthenticationResult>(SkyBellJson.Options);
        if (result is null || string.IsNullOrEmpty(result.IdToken))
        {
            var challenge = data?["ChallengeName"]?.GetValue<string>();
            throw new SkyBellApiException(
                challenge is null ? "Login response contains no token." : $"Login requires challenge '{challenge}' (not supported).",
                resp.StatusCode, responseBody: SkyBellHttp.Truncate(body));
        }

        return new SkyBellLoginAuthenticator(http, SkyBellTokens.From(result), options, tokenKind);
    }

    protected override async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        using var req = NewRequest(HttpMethod.Put, _options, "/api/v5/token",
            new { refresh_token = Tokens.RefreshToken });
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var data = SkyBellHttp.ReadData(resp, body, "/api/v5/token");

        var node = data?["AuthenticationResult"] ?? data;
        var result = node?.Deserialize<AuthenticationResult>(SkyBellJson.Options)
                     ?? throw new SkyBellApiException("Token refresh returned no data.", resp.StatusCode);
        Tokens.Apply(result);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, SkyBellClientOptions options, string path, object body)
    {
        var req = new HttpRequestMessage(method, new Uri(options.BaseAddress, path))
        {
            Content = JsonContent.Create(body, options: SkyBellJson.Options)
        };
        req.Headers.TryAddWithoutValidation("accept", "application/json");
        req.Headers.TryAddWithoutValidation("user-agent", options.UserAgent);
        req.Headers.TryAddWithoutValidation("x-skybell-app", options.AppVersion);
        return req;
    }
}
