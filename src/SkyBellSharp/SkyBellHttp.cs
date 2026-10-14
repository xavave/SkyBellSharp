using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkyBellSharp;

/// <summary>Envelope parsing and error mapping shared by the client and the authenticators.</summary>
internal static class SkyBellHttp
{
    /// <summary>
    /// Validates the response and returns the <c>data</c> node of the SkyBell envelope
    /// (<c>{ error, message, data }</c>), or the whole document when there is no envelope.
    /// </summary>
    public static JsonNode? ReadData(HttpResponseMessage resp, string body, string path,
        bool allowErrorFlag = false)
    {
        JsonNode? root = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { root = JsonNode.Parse(body); }
            catch (JsonException) { /* not JSON */ }
        }

        var apiMessage = root is JsonObject o1 && o1["message"] is JsonValue mv && mv.TryGetValue<string>(out var m) ? m : null;

        if (!resp.IsSuccessStatusCode)
            throw CreateException(resp.StatusCode, path, apiMessage, body);

        if (root is JsonObject obj)
        {
            if (!allowErrorFlag && obj["error"] is JsonValue ev && ev.TryGetValue<bool>(out var isError) && isError)
                throw new SkyBellApiException($"{path}: {apiMessage ?? "API returned error=true"}",
                    resp.StatusCode, apiMessage, Truncate(body));

            if (obj.ContainsKey("data") && (obj.ContainsKey("error") || obj.ContainsKey("message")))
                return obj["data"];
        }
        return root;
    }

    public static SkyBellApiException CreateException(HttpStatusCode status, string path, string? apiMessage, string? body)
    {
        var what = (int)status switch
        {
            401 => "unauthorized (expired token or invalid API key)",
            403 => "forbidden",
            404 => "not found",
            429 => "too many requests",
            599 => "the device did not answer within 30 s",
            _ => $"HTTP {(int)status}"
        };
        var msg = apiMessage is null ? $"{path}: {what}" : $"{path}: {what} - {apiMessage}";
        return new SkyBellApiException(msg, status, apiMessage, Truncate(body));
    }

    public static string? Truncate(string? s, int max = 2000) =>
        s is null || s.Length <= max ? s : s[..max] + "…";
}
