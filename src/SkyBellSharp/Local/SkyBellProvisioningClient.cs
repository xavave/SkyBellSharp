using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SkyBellSharp.Json;

namespace SkyBellSharp.Local;

/// <summary>
/// Talks to a Gen5 SkyBell device in access-point mode (LED flashing red/green, Wi-Fi network
/// "SkyBell_XXXXXXXXX"). Connect the computer to that network first.
/// Flow: <see cref="SkyBellClient.GetProvisionInviteAsync"/> (cloud) → <see cref="ScanWifiAsync"/>
/// → <see cref="ProvisionAsync"/>. Legacy SkyBell HD devices (CoAP) are not supported.
/// </summary>
public sealed class SkyBellProvisioningClient : IDisposable
{
    /// <summary>Default device address in AP mode.</summary>
    public static readonly Uri DefaultDeviceAddress = new("http://192.168.1.1:3030");

    /// <summary>Production SkyBell backend number.</summary>
    public const int ProductionBackend = 2;

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _device;

    public SkyBellProvisioningClient(Uri? deviceAddress = null, HttpClient? httpClient = null)
    {
        _device = deviceAddress ?? DefaultDeviceAddress;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary><c>GET /ap/scan</c> — Wi-Fi networks visible to the device.</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken cancellationToken = default)
    {
        using var resp = await _http.GetAsync(new Uri(_device, "/ap/scan"), cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var data = SkyBellHttp.ReadData(resp, body, "/ap/scan");
        var list = (data as JsonObject)?["ssid"] as JsonArray ?? data as JsonArray;
        return list is null ? Array.Empty<WifiNetwork>() : (IReadOnlyList<WifiNetwork>)SkyBellClient.ConvertData<List<WifiNetwork>>(list, "/ap/scan");
    }

    /// <summary>
    /// <c>POST /ap/provision</c> — sends the Wi-Fi credentials and the cloud invite token; the device
    /// then joins the network and registers itself on the account that issued the invite.
    /// </summary>
    public async Task ProvisionAsync(string inviteToken, WifiCredentials network,
        int backendNumber = ProductionBackend, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteToken);
        ArgumentNullException.ThrowIfNull(network);

        var body = new { invite = inviteToken, backend_num = backendNumber, network };
        using var resp = await _http.PostAsJsonAsync(new Uri(_device, "/ap/provision"), body, SkyBellJson.Options,
            cancellationToken).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SkyBellHttp.ReadData(resp, text, "/ap/provision");
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

/// <summary>A network returned by <c>/ap/scan</c>.</summary>
public sealed class WifiNetwork
{
    [JsonPropertyName("essid")] public string? Ssid { get; set; }
    [JsonPropertyName("mac")] public string? Mac { get; set; }
    [JsonPropertyName("channel")] public int? Channel { get; set; }

    /// <summary>dBm.</summary>
    [JsonPropertyName("signal_level")] public int? SignalLevel { get; set; }

    /// <summary>"wpa", "wpa2", "wep"…</summary>
    [JsonPropertyName("security")] public string? Security { get; set; }

    public override string ToString() => $"{Ssid} ({SignalLevel} dBm, {Security})";
}

/// <summary>Wi-Fi settings sent to the device (<c>network</c> object of <c>/ap/provision</c>).</summary>
public sealed class WifiCredentials
{
    [JsonPropertyName("essid")] public required string Ssid { get; init; }
    [JsonPropertyName("psk")] public string? Password { get; init; }

    /// <summary>"wpa", "wpa2", "wep" or "auto".</summary>
    [JsonPropertyName("wireless_type")] public string WirelessType { get; init; } = "auto";

    [JsonPropertyName("hidden_essid")] public bool HiddenSsid { get; init; }

    /// <summary>"dhcp" or "manual".</summary>
    [JsonPropertyName("ipconfig_type")] public string IpConfigType { get; init; } = "dhcp";

    [JsonPropertyName("static_ip")] public string? StaticIp { get; init; }
    [JsonPropertyName("netmask")] public string? Netmask { get; init; }
    [JsonPropertyName("gateway_ip")] public string? GatewayIp { get; init; }
    [JsonPropertyName("wep_tx_keyidx")] public int? WepKeyIndex { get; init; }
    [JsonPropertyName("wep_key")] public string? WepKey { get; init; }
}
