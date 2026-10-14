using System.Net;

namespace SkyBellSharp;

/// <summary>Error returned by the SkyBell API (non-success HTTP status or <c>error: true</c> envelope).</summary>
public class SkyBellApiException : Exception
{
    public SkyBellApiException(string message, HttpStatusCode? statusCode = null,
        string? apiMessage = null, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
        ResponseBody = responseBody;
    }

    /// <summary>HTTP status code, if the request reached the API.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The <c>message</c> field of the SkyBell JSON envelope, if any.</summary>
    public string? ApiMessage { get; }

    /// <summary>Raw response body (may be truncated).</summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// HTTP 599: the cloud reached the device over MQTT but the device did not answer within 30 s
    /// (device offline, asleep or with a poor connection).
    /// </summary>
    public bool IsDeviceTimeout => (int?)StatusCode == 599;

    /// <summary>HTTP 401/403: invalid/expired token, bad API key, or disabled account.</summary>
    public bool IsAuthenticationError => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary>HTTP 404.</summary>
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
}
