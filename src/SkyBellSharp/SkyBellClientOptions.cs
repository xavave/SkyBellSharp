namespace SkyBellSharp;

/// <summary>Options for <see cref="SkyBellClient"/>.</summary>
public sealed class SkyBellClientOptions
{
    /// <summary>Default production API base address.</summary>
    public const string DefaultBaseUrl = "https://api.skybell.network";

    /// <summary>API base address (no trailing slash needed).</summary>
    public Uri BaseAddress { get; set; } = new(DefaultBaseUrl);

    /// <summary>
    /// Value of the <c>x-skybell-app</c> header (mobile app version). Required by SkyBell for
    /// JWT-authenticated requests; ignored by the API when a personal API key is used.
    /// </summary>
    public string AppVersion { get; set; } = "1.255.57";

    /// <summary>User-Agent sent with every request.</summary>
    public string UserAgent { get; set; } = "SkyBellSharp";

    /// <summary>Value of the <c>x-skybell-lang</c> header.</summary>
    public string Language { get; set; } = "en";

    /// <summary>IANA time zone sent in the <c>tz</c> header (null = not sent).</summary>
    public string? TimeZone { get; set; } = TimeZoneInfo.Local.HasIanaId
        ? TimeZoneInfo.Local.Id
        : (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : null);

    /// <summary>Adds <c>cache-control: no-cache</c> to GET requests (the API may otherwise answer 304).</summary>
    public bool DisableHttpCache { get; set; } = true;
}
