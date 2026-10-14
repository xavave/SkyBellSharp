using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SkyBellSharp.Json;
using SkyBellSharp.Models;

namespace SkyBellSharp;

public sealed partial class SkyBellClient
{
    // =================================================================
    //  User / account
    // =================================================================

    /// <summary><c>GET /api/v5/user</c> — current user (also a cheap way to validate credentials).</summary>
    public Task<SkyBellUser> GetUserAsync(CancellationToken cancellationToken = default) =>
        GetDataAsync<SkyBellUser>("/api/v5/user", cancellationToken);

    /// <summary>
    /// <c>POST /api/v5/user</c> — creates a SkyBell account (the user must then confirm the email).
    /// No authentication required.
    /// </summary>
    public static async Task CreateUserAsync(HttpClient http, NewUser user,
        SkyBellClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SkyBellClientOptions();
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BaseAddress, "/api/v5/user"))
        {
            Content = JsonContent.Create(user, options: SkyBellJson.Options)
        };
        req.Headers.TryAddWithoutValidation("user-agent", options.UserAgent);
        req.Headers.TryAddWithoutValidation("x-skybell-app", options.AppVersion);
        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SkyBellHttp.ReadData(resp, body, "/api/v5/user");
    }

    /// <summary><c>GET /api/v5/logout</c> — revokes all tokens and sessions of the user.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, "/api/v5/logout", null, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized) return; // already invalid
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SkyBellHttp.ReadData(resp, body, "/api/v5/logout");
    }

    /// <summary>
    /// Step 1 of account deletion (<c>DELETE /api/v5/user</c> without body).
    /// Returns the confirmation key to pass to <see cref="ConfirmAccountDeletionAsync"/>.
    /// </summary>
    public async Task<string> RequestAccountDeletionAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendForDataAsync(HttpMethod.Delete, "/api/v5/user", null, cancellationToken,
            allowErrorFlag: true).ConfigureAwait(false);
        return data?["confirm"]?.GetValue<string>()
               ?? throw new SkyBellApiException("No confirmation key returned by DELETE /api/v5/user.");
    }

    /// <summary>
    /// Step 2 of account deletion. <b>Irreversible after 30 days</b>: the account is disabled,
    /// then all data and devices are deleted.
    /// </summary>
    public async Task ConfirmAccountDeletionAsync(string confirmationKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationKey);
        await SendForDataAsync(HttpMethod.Delete, "/api/v5/user", new { confirm = confirmationKey },
            cancellationToken).ConfigureAwait(false);
    }

    // =================================================================
    //  Personal API key (needs a JWT session)
    // =================================================================

    /// <summary>
    /// <c>GET /api/v5/user/api_access</c> — current personal API key, or null if none.
    /// Requires a JWT session (the API answers 401 "Please login first." to API-key clients).
    /// </summary>
    public async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        EnsureJwt();
        var data = await SendForDataAsync(HttpMethod.Get, "/api/v5/user/api_access", null, cancellationToken,
            allowErrorFlag: true, notFoundIsNull: true).ConfigureAwait(false);
        return ExtractApiKey(data);
    }

    /// <summary>
    /// <c>POST /api/v5/user/api_access</c> — creates the personal API key, or <b>regenerates</b> it
    /// (invalidating the previous one).
    /// </summary>
    public async Task<string> CreateApiKeyAsync(CancellationToken cancellationToken = default)
    {
        EnsureJwt();
        var data = await SendForDataAsync(HttpMethod.Post, "/api/v5/user/api_access", new { }, cancellationToken)
            .ConfigureAwait(false);
        return ExtractApiKey(data)
               ?? throw new SkyBellApiException("No API key found in the response.",
                   responseBody: SkyBellHttp.Truncate(data?.ToJsonString()));
    }

    /// <summary>Returns the existing key, or creates one (never regenerates a valid key).</summary>
    public async Task<string> GetOrCreateApiKeyAsync(CancellationToken cancellationToken = default) =>
        await GetApiKeyAsync(cancellationToken).ConfigureAwait(false)
        ?? await CreateApiKeyAsync(cancellationToken).ConfigureAwait(false);

    /// <summary><c>DELETE /api/v5/user/api_access</c> — revokes the personal API key (JWT session required).</summary>
    public Task DeleteApiKeyAsync(CancellationToken cancellationToken = default)
    {
        EnsureJwt();
        return SendForDataAsync(HttpMethod.Delete, "/api/v5/user/api_access", null, cancellationToken);
    }

    private void EnsureJwt()
    {
        if (!Authenticator.IsJwt)
            throw new InvalidOperationException(
                "Managing the personal API key requires an email/password (JWT) session, not an API key.");
    }

    /// <summary>
    /// The response format of <c>/user/api_access</c> is undocumented: accepts <c>data</c> as a
    /// UUID string, or an object holding a UUID under a property whose name contains "key".
    /// </summary>
    internal static string? ExtractApiKey(JsonNode? node)
    {
        return Find(node, keyContext: true);

        static string? Find(JsonNode? n, bool keyContext)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var s):
                    return keyContext && Guid.TryParse(s, out _) ? s : null;

                case JsonObject o:
                    foreach (var (name, child) in o)
                        if (name.Contains("key", StringComparison.OrdinalIgnoreCase) && Find(child, true) is { } k1)
                            return k1;
                    foreach (var (_, child) in o)
                        if (child is JsonObject or JsonArray && Find(child, false) is { } k2)
                            return k2;
                    return null;

                case JsonArray a:
                    foreach (var item in a)
                        if (Find(item, keyContext) is { } k3) return k3;
                    return null;

                default:
                    return null;
            }
        }
    }

    // =================================================================
    //  Mobile notifications (Firebase) & health
    // =================================================================

    /// <summary><c>POST /api/v5/notification_token</c> — registers a Firebase token for push notifications.</summary>
    public Task RegisterNotificationTokenAsync(string fcmToken, string mobileDeviceId, string accountId,
        CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Post, "/api/v5/notification_token",
            new { fcm_token = fcmToken, mobile_device_id = mobileDeviceId, account_id = accountId }, cancellationToken);

    /// <summary><c>DELETE /api/v5/notification_token</c> — stops push notifications for a mobile device.</summary>
    public Task RemoveNotificationTokenAsync(string mobileDeviceId, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Delete, "/api/v5/notification_token",
            new { mobile_device_id = mobileDeviceId }, cancellationToken);

    /// <summary><c>GET /health</c> — API status and current versions (no authentication needed).</summary>
    public Task<SkyBellHealth> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetDataAsync<SkyBellHealth>("/health", cancellationToken);
}
