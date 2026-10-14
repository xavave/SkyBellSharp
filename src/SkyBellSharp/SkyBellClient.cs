using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyBellSharp.Auth;
using SkyBellSharp.Json;

namespace SkyBellSharp;

/// <summary>
/// Client for the SkyBell Gen5 cloud API (https://api.skybell.network/docs/).
/// Thread-safe; create one per account and reuse it.
/// </summary>
/// <remarks>
/// SkyBell asks API consumers not to poll faster than once every 60 seconds.
/// Endpoints that talk to the device over MQTT (snapshot, live stream, reboot, settings,
/// tones) can take up to 30 s and fail with HTTP 599 (<see cref="SkyBellApiException.IsDeviceTimeout"/>).
/// </remarks>
public sealed partial class SkyBellClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>Creates a client with an existing <see cref="HttpClient"/> (recommended with IHttpClientFactory).</summary>
    public SkyBellClient(HttpClient httpClient, ISkyBellAuthenticator authenticator, SkyBellClientOptions? options = null)
        : this(httpClient, authenticator, options, ownsHttp: false) { }

    /// <summary>Creates a client that owns its own <see cref="HttpClient"/>.</summary>
    public SkyBellClient(ISkyBellAuthenticator authenticator, SkyBellClientOptions? options = null)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, authenticator, options, ownsHttp: true) { }

    private SkyBellClient(HttpClient httpClient, ISkyBellAuthenticator authenticator, SkyBellClientOptions? options, bool ownsHttp)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        Options = options ?? new SkyBellClientOptions();
        _ownsHttp = ownsHttp;
    }

    /// <summary>Client authenticated with a personal API key.</summary>
    public static SkyBellClient WithApiKey(string apiKey, SkyBellClientOptions? options = null) =>
        new(new ApiKeyAuthenticator(apiKey), options);

    /// <summary>Client for the public endpoints only (health, tones, default rules).</summary>
    public static SkyBellClient Anonymous(SkyBellClientOptions? options = null) =>
        new(AnonymousAuthenticator.Instance, options);

    /// <summary>Logs in with email + password through <c>POST /api/v5/login</c>.</summary>
    public static async Task<SkyBellClient> LoginAsync(string email, string password,
        SkyBellClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SkyBellClientOptions();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            var auth = await SkyBellLoginAuthenticator
                .LoginAsync(http, email, password, options, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new SkyBellClient(http, auth, options, ownsHttp: true);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    public ISkyBellAuthenticator Authenticator { get; }
    public SkyBellClientOptions Options { get; }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // =================================================================
    //  Plumbing
    // =================================================================

    internal async Task<JsonNode?> SendForDataAsync(HttpMethod method, string path, object? body,
        CancellationToken ct, bool allowErrorFlag = false, bool notFoundIsNull = false)
    {
        Func<HttpContent>? content = body is null
            ? null
            : () => JsonContent.Create(body, body.GetType(), options: SkyBellJson.Options);
        using var resp = await SendAsync(method, path, content, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (notFoundIsNull && resp.StatusCode == HttpStatusCode.NotFound) return null;
        return SkyBellHttp.ReadData(resp, text, path, allowErrorFlag);
    }

    internal async Task<T> GetDataAsync<T>(string path, CancellationToken ct)
    {
        var data = await SendForDataAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        return ConvertData<T>(data, path);
    }

    internal async Task<T> SendDataAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var data = await SendForDataAsync(method, path, body, ct).ConfigureAwait(false);
        return ConvertData<T>(data, path);
    }

    internal static T ConvertData<T>(JsonNode? data, string path)
    {
        if (data is null)
            throw new SkyBellApiException($"{path}: response contains no data.");
        try
        {
            return data.Deserialize<T>(SkyBellJson.Options)
                   ?? throw new SkyBellApiException($"{path}: unreadable response.");
        }
        catch (JsonException ex)
        {
            throw new SkyBellApiException($"{path}: unexpected response format ({ex.Message}).", inner: ex,
                responseBody: SkyBellHttp.Truncate(data.ToJsonString()));
        }
    }

    /// <summary>Sends a request with auth headers; retries once after a 401 if the authenticator can recover.</summary>
    internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path,
        Func<HttpContent>? content, CancellationToken ct, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using (var req = await BuildAsync(method, path, content, ct).ConfigureAwait(false))
        {
            var resp = await _http.SendAsync(req, completion, ct).ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.Unauthorized ||
                !await Authenticator.TryRecoverAsync(ct).ConfigureAwait(false))
                return resp;
            resp.Dispose();
        }

        using var retry = await BuildAsync(method, path, content, ct).ConfigureAwait(false);
        return await _http.SendAsync(retry, completion, ct).ConfigureAwait(false);
    }

    private async Task<HttpRequestMessage> BuildAsync(HttpMethod method, string path, Func<HttpContent>? content, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, new Uri(Options.BaseAddress, path));
        if (content is not null) req.Content = content();

        await Authenticator.AuthenticateAsync(req, ct).ConfigureAwait(false);

        req.Headers.TryAddWithoutValidation("accept", "application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("user-agent", Options.UserAgent);
        req.Headers.TryAddWithoutValidation("x-skybell-lang", Options.Language);
        if (!string.IsNullOrEmpty(Options.TimeZone)) req.Headers.TryAddWithoutValidation("tz", Options.TimeZone);
        if (Authenticator.IsJwt) req.Headers.TryAddWithoutValidation("x-skybell-app", Options.AppVersion);

        if (Options.DisableHttpCache && method == HttpMethod.Get)
        {
            req.Headers.TryAddWithoutValidation("cache-control", "no-cache");
            req.Headers.TryAddWithoutValidation("pragma", "no-cache");
        }
        return req;
    }

    internal static string Seg(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value.Trim());
    }

    /// <summary>Extracts <c>rows</c> from <c>{ count, rows }</c>, or returns the array itself.</summary>
    internal static JsonArray? Rows(JsonNode? data) => data switch
    {
        JsonArray a => a,
        JsonObject o when o["rows"] is JsonArray r => r,
        JsonObject o when o["devices"] is JsonArray d => d,
        _ => null
    };
}
