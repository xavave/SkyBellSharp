using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SkyBellSharp.Sample;

/// <summary>
/// Saves the raw JSON of every read-only endpoint, with secrets and personal data redacted,
/// so the response formats (some undocumented) can be checked and the models fixed.
/// Nothing is modified on the account and no live stream / snapshot is requested.
/// </summary>
internal static partial class Dump
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Property names whose values are secrets or personal data.
    [GeneratedRegex("token|key|secret|password|credential|signature|invite|certificate|email|serial|mac|essid|ssid|phone|full_name|street|zipcode|^lat|^lon|latitude|longitude|account_id|client_id|psk|access",
        RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    public static async Task RunAsync(SkyBellClient client, string folder)
    {
        Directory.CreateDirectory(folder);
        Console.WriteLine($"Dumping to {folder}");

        await SaveAsync(client, folder, "health", "/health");
        await SaveAsync(client, folder, "user", "/api/v5/user");
        await SaveAsync(client, folder, "user_api_access", "/api/v5/user/api_access");
        var devices = await SaveAsync(client, folder, "devices", "/api/v5/devices");
        await SaveAsync(client, folder, "activity", "/api/v5/activity?limit=3&nopreviews=true");
        await SaveAsync(client, folder, "activity_day", "/api/v5/activity/day");
        await SaveAsync(client, folder, "shares", "/api/v5/shares");
        await SaveAsync(client, folder, "locations", "/api/v5/locations");
        await SaveAsync(client, folder, "tones", "/api/v5/tones");
        await SaveAsync(client, folder, "tones_chime", "/api/v5/tones/chime");
        await SaveAsync(client, folder, "rules_default", "/api/v5/rules");

        var ids = (devices?["data"]?["rows"] as JsonArray ?? devices?["data"] as JsonArray ?? new JsonArray())
            .Select(d => d?["device_id"]?.GetValue<string>() ?? d?["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Take(3)
            .ToList();

        for (var i = 0; i < ids.Count; i++)
        {
            var id = Uri.EscapeDataString(ids[i]!);
            await SaveAsync(client, folder, $"device{i}", $"/api/v5/devices/{id}");
            await SaveAsync(client, folder, $"device{i}_triggers", $"/api/v5/devices/{id}/triggers");
            await SaveAsync(client, folder, $"device{i}_rules", $"/api/v5/devices/{id}/rules");
        }

        Console.WriteLine("Done. Check the files before sharing them: identifiers are replaced, but review anyway.");
    }

    private static async Task<JsonNode?> SaveAsync(SkyBellClient client, string folder, string name, string path)
    {
        JsonNode? raw = null;
        JsonNode output;
        try
        {
            raw = await client.GetRawAsync(path);
            output = Redact(raw) ?? JsonValue.Create("(empty)")!;
            Console.WriteLine($"  ok    {path}");
        }
        catch (SkyBellApiException ex)
        {
            output = new JsonObject
            {
                ["status"] = ex.StatusCode is null ? null : (int)ex.StatusCode,
                ["message"] = ex.ApiMessage ?? ex.Message,
                ["body"] = Redact(TryParse(ex.ResponseBody))
            };
            Console.WriteLine($"  {(int?)ex.StatusCode,-5} {path}");
        }

        await File.WriteAllTextAsync(Path.Combine(folder, name + ".json"),
            new JsonObject { ["request"] = "GET " + path, ["response"] = output }.ToJsonString(Indented));
        return raw;
    }

    private static JsonNode? TryParse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return JsonNode.Parse(s); }
        catch (JsonException) { return JsonValue.Create(s.Length > 300 ? s[..300] + "…" : s); }
    }

    /// <summary>
    /// Returns a redacted <b>copy</b>: sensitive values masked (shape kept), UUIDs and URL queries
    /// removed, long strings and arrays shortened.
    /// </summary>
    private static JsonNode? Redact(JsonNode? node, string? name = null)
    {
        switch (node)
        {
            case JsonObject o:
                var obj = new JsonObject();
                foreach (var (key, child) in o) obj[key] = Redact(child, key);
                return obj;

            case JsonArray a:
                var arr = new JsonArray();
                foreach (var child in a.Take(3)) arr.Add(Redact(child, name));
                if (a.Count > 3) arr.Add(JsonValue.Create($"<{a.Count - 3} more>"));
                return arr;

            case JsonValue v when v.TryGetValue<string>(out var s):
                if (name is not null && SensitiveName().IsMatch(name) && s.Length > 0)
                    return JsonValue.Create(Shape(s));
                if (Guid.TryParse(s, out _)) return JsonValue.Create("<uuid>");
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var q = s.IndexOf('?');
                    var url = q < 0 ? s : s[..q] + "?<query>";
                    return JsonValue.Create(Regex.Replace(url,
                        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<uuid>", RegexOptions.IgnoreCase));
                }
                if (s.Length > 120) return JsonValue.Create($"<{s.Length} chars>");
                return JsonValue.Create(s);

            case JsonValue when name is not null && SensitiveName().IsMatch(name):
                return JsonValue.Create("<redacted number/bool>");

            default:
                return node?.DeepClone();
        }
    }

    /// <summary>"adb4424f-…" -> "<36 chars, uuid>", keeps a hint of the format only.</summary>
    private static string Shape(string s) =>
        Guid.TryParse(s, out _) ? "<uuid>"
        : s.Contains('@') ? "<email>"
        : $"<{s.Length} chars>";
}
