namespace SkyBellSharp.Auth;

/// <summary>Adds SkyBell credentials to outgoing API requests.</summary>
public interface ISkyBellAuthenticator
{
    /// <summary>True for JWT modes (the API then requires the <c>x-skybell-app</c> header).</summary>
    bool IsJwt { get; }

    /// <summary>Adds the authentication headers (refreshing the token first if needed).</summary>
    ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken);

    /// <summary>
    /// Called after an HTTP 401. Returns true when credentials were renewed and the request
    /// should be retried once.
    /// </summary>
    ValueTask<bool> TryRecoverAsync(CancellationToken cancellationToken);
}

/// <summary>No authentication (public endpoints only: health, tones, default rules).</summary>
public sealed class AnonymousAuthenticator : ISkyBellAuthenticator
{
    public static AnonymousAuthenticator Instance { get; } = new();

    public bool IsJwt => false;
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken) => default;
    public ValueTask<bool> TryRecoverAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>
/// Personal API key (UUID) generated at https://api.skybell.network/developers or with
/// <see cref="SkyBellClient.CreateApiKeyAsync"/>. Sent in <c>x-skybell-user-api-key</c>.
/// The key grants full access to the account: protect it like a password.
/// </summary>
public sealed class ApiKeyAuthenticator : ISkyBellAuthenticator
{
    public const string HeaderName = "x-skybell-user-api-key";

    private readonly string _apiKey;

    public ApiKeyAuthenticator(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey.Trim();
    }

    public bool IsJwt => false;

    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(HeaderName, _apiKey);
        return default;
    }

    public ValueTask<bool> TryRecoverAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
