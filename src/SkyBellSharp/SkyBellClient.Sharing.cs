using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyBellSharp.Json;
using SkyBellSharp.Models;

namespace SkyBellSharp;

public sealed partial class SkyBellClient
{
    // =================================================================
    //  Device sharing
    // =================================================================

    /// <summary>
    /// <c>POST /api/v5/devices/{id}/share</c> — invites an existing SkyBell user by email.
    /// Calling it again re-sends the email if at least 10 minutes have passed.
    /// </summary>
    public async Task<ShareInvite> ShareDeviceAsync(string deviceId, string invitedEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitedEmail);
        var path = $"/api/v5/devices/{Seg(deviceId)}/share";
        using var resp = await SendAsync(HttpMethod.Post, path,
            () => JsonContent.Create(new { invitedEmail = invitedEmail.Trim() }, options: SkyBellJson.Options),
            cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // "Invite already exists" comes back with error=true but a 200/202 status: not a failure.
        var data = SkyBellHttp.ReadData(resp, body, path, allowErrorFlag: true);
        var invite = data is JsonObject ? ConvertData<ShareInvite>(data, path) : new ShareInvite();

        string? message = null;
        try { message = JsonNode.Parse(body)?["message"]?.GetValue<string>(); } catch (JsonException) { }
        invite.Message = message;
        invite.Created = resp.StatusCode == HttpStatusCode.Created;
        invite.EmailSent = resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK;
        if (invite.InviteToken is null)
            throw new SkyBellApiException($"{path}: {message ?? "no invite token returned"}", resp.StatusCode, message);
        return invite;
    }

    /// <summary>
    /// Accepts a share invitation (called by the invited user) with the invite token
    /// (<c>GET /api/v5/shares/{token}/redeem</c>). Returns the newly shared device data.
    /// </summary>
    public Task<JsonNode?> RedeemShareAsync(string inviteToken, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Get, $"/api/v5/shares/{Seg(inviteToken)}/redeem", null, cancellationToken);

    /// <summary><c>GET /api/v5/shares</c> — existing device shares.</summary>
    public async Task<IReadOnlyList<DeviceShare>> GetSharesAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendForDataAsync(HttpMethod.Get, "/api/v5/shares", null, cancellationToken).ConfigureAwait(false);
        var rows = Rows(data);
        return rows is null ? Array.Empty<DeviceShare>() : (IReadOnlyList<DeviceShare>)ConvertData<List<DeviceShare>>(rows, "/api/v5/shares");
    }

    /// <summary><c>DELETE /api/v5/shares/{shareId}</c> — revokes a share (the guest is not notified).</summary>
    public Task RevokeShareAsync(string shareId, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Delete, $"/api/v5/shares/{Seg(shareId)}", new { share_id = shareId }, cancellationToken);

    /// <summary><c>PUT /api/v5/shares/{shareId}</c> — read-only guests cannot change device settings.</summary>
    public Task SetShareReadOnlyAsync(string shareId, bool readOnly, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Put, $"/api/v5/shares/{Seg(shareId)}", new { readOnly }, cancellationToken);

    /// <summary>
    /// <c>POST /api/v5/shares/{shareId}</c> — the guest's own notification settings
    /// (button press / motion) for a shared device.
    /// </summary>
    public Task SetSharedDeviceNotificationsAsync(string shareId, SharedDeviceNotifications settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SendForDataAsync(HttpMethod.Post, $"/api/v5/shares/{Seg(shareId)}", settings, cancellationToken);
    }

    // =================================================================
    //  Triggers (webhooks)
    // =================================================================

    /// <summary><c>GET /api/v5/devices/{id}/triggers</c> — webhooks and integrations (IFTTT, Alexa, Google…).</summary>
    public async Task<IReadOnlyList<SkyBellTrigger>> GetTriggersAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v5/devices/{Seg(deviceId)}/triggers";
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var rows = Rows(data);
        return rows is null ? Array.Empty<SkyBellTrigger>() : (IReadOnlyList<SkyBellTrigger>)ConvertData<List<SkyBellTrigger>>(rows, path);
    }

    /// <summary>
    /// <c>POST /api/v5/devices/{id}/triggers</c> — creates a webhook called on the selected events.
    /// The API disables a trigger after 25 consecutive failures.
    /// </summary>
    /// <remarks>
    /// SkyBell calls the URL once while creating the trigger (with the chosen method) and rejects it
    /// with HTTP 400 "Webhook trigger test failed…" unless the endpoint answers with a success status.
    /// </remarks>
    public Task<SkyBellTrigger> CreateWebhookAsync(string deviceId, WebhookTrigger webhook,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        return SendDataAsync<SkyBellTrigger>(HttpMethod.Post, $"/api/v5/devices/{Seg(deviceId)}/triggers", webhook, cancellationToken);
    }

    /// <summary>
    /// <c>DELETE /api/v5/devices/{id}/triggers/{triggerId}</c> — removes a webhook, or disables an
    /// IFTTT / Alexa / Google Home integration.
    /// </summary>
    public Task DeleteTriggerAsync(string deviceId, string triggerId, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Delete, $"/api/v5/devices/{Seg(deviceId)}/triggers/{Seg(triggerId)}", null,
            cancellationToken);

    // =================================================================
    //  Locations
    // =================================================================

    /// <summary><c>GET /api/v5/locations</c>.</summary>
    public async Task<IReadOnlyList<SkyBellLocation>> GetLocationsAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendForDataAsync(HttpMethod.Get, "/api/v5/locations", null, cancellationToken).ConfigureAwait(false);
        var rows = Rows(data);
        return rows is null ? Array.Empty<SkyBellLocation>() : (IReadOnlyList<SkyBellLocation>)ConvertData<List<SkyBellLocation>>(rows, "/api/v5/locations");
    }

    /// <summary><c>POST /api/v5/locations</c> — creates a location (building, address, contacts).</summary>
    public async Task<SkyBellLocation?> CreateLocationAsync(LocationDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var data = await SendForDataAsync(HttpMethod.Post, "/api/v5/locations", details, cancellationToken).ConfigureAwait(false);
        return data is JsonObject ? ConvertData<SkyBellLocation>(data, "/api/v5/locations") : null;
    }

    /// <summary>
    /// <c>POST /api/v5/locations/{id}</c> — partial update. Note: <c>device_ids</c> replaces the whole
    /// list (send existing ids + new ones; an empty array removes all devices).
    /// </summary>
    public Task UpdateLocationAsync(string locationId, JsonObject changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return SendForDataAsync(HttpMethod.Post, $"/api/v5/locations/{Seg(locationId)}", changes, cancellationToken);
    }

    /// <summary>Sets the devices of a location (replaces the current list).</summary>
    public Task SetLocationDevicesAsync(string locationId, IEnumerable<string> deviceIds,
        CancellationToken cancellationToken = default) =>
        UpdateLocationAsync(locationId,
            new JsonObject { ["device_ids"] = new JsonArray(deviceIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) },
            cancellationToken);

    /// <summary><c>DELETE /api/v5/locations/{id}</c> — its devices stay on the account, without a location.</summary>
    public Task DeleteLocationAsync(string locationId, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Delete, $"/api/v5/locations/{Seg(locationId)}", null, cancellationToken);
}
