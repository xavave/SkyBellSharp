namespace SkyBellSharp.Auth;

/// <summary>
/// Base class for JWT authenticators: refreshes the token shortly before expiry and once
/// after an HTTP 401. Refreshes are serialized (thread-safe).
/// </summary>
public abstract class TokenAuthenticatorBase : ISkyBellAuthenticator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected TokenAuthenticatorBase(SkyBellTokens tokens, JwtTokenKind tokenKind)
    {
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        TokenKind = tokenKind;
    }

    /// <summary>Current tokens (updated in place on refresh).</summary>
    public SkyBellTokens Tokens { get; }

    public JwtTokenKind TokenKind { get; }

    public bool IsJwt => true;

    /// <summary>Raised after a successful refresh, e.g. to persist the new tokens.</summary>
    public event EventHandler<SkyBellTokens>? TokensRefreshed;

    /// <summary>Performs the refresh call and updates <see cref="Tokens"/>.</summary>
    protected abstract Task RefreshCoreAsync(CancellationToken cancellationToken);

    /// <summary>Forces a refresh now.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        TokensRefreshed?.Invoke(this, Tokens);
    }

    public async ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Tokens.IsExpired() && !string.IsNullOrEmpty(Tokens.RefreshToken))
        {
            var before = Tokens.ExpiresAt;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var refreshed = false;
            try
            {
                // Another caller may have refreshed while we were waiting.
                if (Tokens.ExpiresAt == before && Tokens.IsExpired())
                {
                    await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                    refreshed = true;
                }
            }
            finally { _gate.Release(); }
            if (refreshed) TokensRefreshed?.Invoke(this, Tokens);
        }

        var jwt = TokenKind == JwtTokenKind.IdToken ? Tokens.IdToken : Tokens.AccessToken;
        request.Headers.TryAddWithoutValidation("authorization", jwt);
    }

    public async ValueTask<bool> TryRecoverAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Tokens.RefreshToken)) return false;
        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SkyBellApiException) { return false; }
    }
}
