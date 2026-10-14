using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyBellSharp.Json;
using SkyBellSharp.Models;

namespace SkyBellSharp;

public sealed partial class SkyBellClient
{
    // =================================================================
    //  Devices
    // =================================================================

    /// <summary><c>GET /api/v5/devices</c> — devices owned by or shared with the user.</summary>
    public async Task<IReadOnlyList<SkyBellDevice>> GetDevicesAsync(int? limit = null, int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var q = new List<string>();
        if (limit is { } l) q.Add("limit=" + l);
        if (offset is { } o) q.Add("offset=" + o);
        var path = "/api/v5/devices" + (q.Count > 0 ? "?" + string.Join("&", q) : "");

        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var rows = Rows(data);
        return rows is null ? Array.Empty<SkyBellDevice>() : (IReadOnlyList<SkyBellDevice>)ConvertData<List<SkyBellDevice>>(rows, path);
    }

    /// <summary><c>GET /api/v5/devices/{id}</c> — settings, hardware info and telemetry.</summary>
    public Task<SkyBellDevice> GetDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        GetDataAsync<SkyBellDevice>($"/api/v5/devices/{Seg(deviceId)}", cancellationToken);

    /// <summary><c>GET /api/v5/devices/{id}</c> as raw JSON (to read fields not modelled by <see cref="SkyBellDevice"/>).</summary>
    public async Task<JsonObject> GetDeviceRawAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v5/devices/{Seg(deviceId)}";
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        return data as JsonObject ?? throw new SkyBellApiException($"{path}: unexpected response.");
    }

    /// <summary>
    /// <c>POST /api/v5/devices/{id}/settings</c> — only the provided settings change. An offline
    /// device applies them when it reconnects.
    /// </summary>
    public Task UpdateSettingsAsync(string deviceId, DeviceSettingsUpdate settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return UpdateSettingsAsync(deviceId, settings.ToJson(), cancellationToken);
    }

    /// <summary><c>POST /api/v5/devices/{id}/settings</c> with a raw JSON body.</summary>
    public Task UpdateSettingsAsync(string deviceId, JsonObject settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SendForDataAsync(HttpMethod.Post, $"/api/v5/devices/{Seg(deviceId)}/settings", settings, cancellationToken);
    }

    /// <summary><c>POST /api/v5/devices/{id}/reboot</c> — the device reboots 10–60 s later.</summary>
    public Task RebootAsync(string deviceId, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Post, $"/api/v5/devices/{Seg(deviceId)}/reboot", new { }, cancellationToken);

    /// <summary>
    /// <c>DELETE /api/v5/devices/{id}</c> — <b>factory-resets</b> the device and removes it from
    /// the account (shares and certificates included; past activities are kept).
    /// </summary>
    public Task DeleteDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Delete, $"/api/v5/devices/{Seg(deviceId)}", null, cancellationToken);

    /// <summary><c>GET /api/v5/devices/{id}/snapshot</c> — asks the device for a fresh picture (up to 30 s).</summary>
    public async Task<Snapshot> GetSnapshotAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v5/devices/{Seg(deviceId)}/snapshot";
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);

        var preview = data?["preview"] is JsonValue pv && pv.TryGetValue<string>(out var s) ? s : null;
        var jpeg = Base64Image.TryDecode(preview)
                   ?? throw new SkyBellApiException($"{path}: no image in the response.");

        DateTimeOffset? takenAt = data?["date_time"] switch
        {
            JsonValue v when v.TryGetValue<string>(out var str) => FlexibleTimestampConverter.Parse(str),
            JsonValue v when v.TryGetValue<long>(out var n) => FlexibleTimestampConverter.FromUnix(n),
            _ => null
        };
        return new Snapshot(jpeg, takenAt);
    }

    // =================================================================
    //  Live stream (WebRTC over Amazon Kinesis Video Streams)
    // =================================================================

    /// <summary>
    /// <c>POST /api/v5/devices/{id}/videostream</c> — asks the device to start a live session and
    /// returns the KVS signaling data. Plug it into a KVS WebRTC VIEWER client.
    /// </summary>
    /// <param name="force">Do not wait for the device acknowledgement (useful with several viewers).</param>
    public Task<VideoStreamSession> StartLiveStreamAsync(string deviceId, bool force = false,
        CancellationToken cancellationToken = default) =>
        SendDataAsync<VideoStreamSession>(HttpMethod.Post, $"/api/v5/devices/{Seg(deviceId)}/videostream",
            force ? new { force = true } : (object)new { }, cancellationToken);

    /// <summary>
    /// <c>DELETE /api/v5/devices/{id}/videostream</c> — ends the live stream, stops any recording and
    /// finalizes the running event (upload). Always succeeds even if nothing was running.
    /// </summary>
    public async Task StopLiveStreamAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v5/devices/{Seg(deviceId)}/videostream";
        using var resp = await SendAsync(HttpMethod.Delete, path, null, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return;
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SkyBellHttp.ReadData(resp, body, path, allowErrorFlag: true);
    }

    // =================================================================
    //  Tones
    // =================================================================

    /// <summary><c>GET /api/v5/tones</c> — featured doorbell tones (no authentication needed).</summary>
    public Task<List<SkyBellTone>> GetDoorbellTonesAsync(CancellationToken cancellationToken = default) =>
        GetDataAsync<List<SkyBellTone>>("/api/v5/tones", cancellationToken);

    /// <summary><c>GET /api/v5/tones/chime</c> — tones for SkyBell Chime devices (no authentication needed).</summary>
    public Task<List<SkyBellTone>> GetChimeTonesAsync(CancellationToken cancellationToken = default) =>
        GetDataAsync<List<SkyBellTone>>("/api/v5/tones/chime", cancellationToken);

    /// <summary>
    /// <c>POST /api/v5/devices/{id}/tone</c> — sets a tone from the SkyBell catalog. The device downloads
    /// it first, so this can take several seconds.
    /// </summary>
    /// <param name="toneFile"><see cref="SkyBellTone.File"/> of a catalog tone.</param>
    public Task SetToneAsync(string deviceId, ToneType toneType, string toneFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toneFile);
        return SendForDataAsync(HttpMethod.Post, $"/api/v5/devices/{Seg(deviceId)}/tone",
            new { tone_type = toneType.ToApi(), tone_file = toneFile }, cancellationToken);
    }

    /// <summary>
    /// <c>PUT /api/v5/devices/{id}/tone</c> — uploads a custom tone (MP3, OGG or WAV, max 5 MB; only the
    /// first 10 s are used on cameras, 30 s on Chimes). Then select it with chime_file = "user.wav".
    /// </summary>
    public async Task UploadToneAsync(string deviceId, ToneType toneType, Stream audio, string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Buffered so the request can be replayed after a token refresh.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await audio.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            bytes = ms.ToArray();
        }
        if (bytes.Length > 5 * 1024 * 1024)
            throw new ArgumentException("Tone files are limited to 5 MB.", nameof(audio));

        var mime = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };

        HttpContent Build()
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, "user_tone", Path.GetFileName(fileName));
            form.Add(new StringContent(toneType.ToApi()), "tone_type");
            return form;
        }

        var path = $"/api/v5/devices/{Seg(deviceId)}/tone";
        using var resp = await SendAsync(HttpMethod.Put, path, Build, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SkyBellHttp.ReadData(resp, body, path);
    }

    /// <summary><c>POST /api/v5/devices/{id}/testtone</c> — plays a tone on the device (see <see cref="DeviceTones"/>).</summary>
    public Task TestToneAsync(string deviceId, string fileName = DeviceTones.User,
        CancellationToken cancellationToken = default) =>
        SendForDataAsync(HttpMethod.Post, $"/api/v5/devices/{Seg(deviceId)}/testtone",
            new { file_name = fileName }, cancellationToken);

    // =================================================================
    //  On-device rules
    // =================================================================

    /// <summary>
    /// <c>GET /api/v5/devices/{id}/rules</c> — rules of the on-device rule engine
    /// (see <see cref="SkyBellDevice.UsingRules"/>).
    /// </summary>
    public async Task<IReadOnlyList<SkyBellRule>> GetDeviceRulesAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v5/devices/{Seg(deviceId)}/rules";
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var rules = data is JsonObject o ? o["rules"] as JsonArray : data as JsonArray;
        return rules is null ? Array.Empty<SkyBellRule>() : (IReadOnlyList<SkyBellRule>)ConvertData<List<SkyBellRule>>(rules, path);
    }

    /// <summary>
    /// <c>GET /api/v5/rules</c> — featured rule sets by name ("Base Rules", "Face Detection Record"…).
    /// No authentication needed.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SkyBellRule>>> GetDefaultRuleSetsAsync(
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v5/rules";
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, IReadOnlyList<SkyBellRule>>();
        if (data is not JsonObject sets) return result;

        foreach (var (name, value) in sets)
        {
            // Production: name -> [rules]; documented: name -> { rules: [rules] }.
            var rules = value as JsonArray ?? (value as JsonObject)?["rules"] as JsonArray;
            if (rules is not null) result[name] = ConvertData<List<SkyBellRule>>(rules, path);
        }
        return result;
    }

    // =================================================================
    //  Provisioning (cloud side)
    // =================================================================

    /// <summary>
    /// <c>GET /api/v5/devices/provision</c> — step 1 of provisioning: a fresh invite token to send to
    /// a device in AP mode (see <see cref="Local.SkyBellProvisioningClient"/>).
    /// </summary>
    public Task<ProvisionInvite> GetProvisionInviteAsync(CancellationToken cancellationToken = default) =>
        GetDataAsync<ProvisionInvite>("/api/v5/devices/provision", cancellationToken);
}
