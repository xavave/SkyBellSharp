using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyBellSharp.Models;

namespace SkyBellSharp.Sample;

/// <summary>
/// Exercises the write endpoints with <b>reversible</b> operations only, and saves the raw
/// (redacted) responses so undocumented formats can be modelled:
/// <list type="bullet">
/// <item>settings: writes back the current speaker volume (no actual change);</item>
/// <item>snapshot: requests a fresh picture;</item>
/// <item>webhook: creates a trigger on the rare "vready" event, then deletes it;</item>
/// <item>location: creates a test location, then deletes it;</item>
/// <item>test tone: only after confirmation (the doorbell plays a sound).</item>
/// </list>
/// Nothing is rebooted, deleted from the history, shared, or streamed.
/// </summary>
internal static class SelfTest
{
    private const string WebhookUrl = "https://example.com/skybellsharp-selftest";
    private const string LocationName = "SkyBellSharp selftest";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task RunAsync(SkyBellClient client, string folder)
    {
        Directory.CreateDirectory(folder);
        Console.WriteLine($"Self-test, results in {folder}");

        var devices = await client.GetDevicesAsync();
        if (devices.Count == 0) { Console.WriteLine("No device on this account."); return; }
        var device = devices[0];
        var id = device.Id;
        var path = $"/api/v5/devices/{Uri.EscapeDataString(id)}";
        Console.WriteLine($"Device: {device.Name} ({device.Hardware}, fw {device.Firmware})");

        var results = new List<(string Step, bool Ok, string Detail)>();

        // 1. Settings: idempotent write (current value written back).
        await StepAsync(results, "settings", async () =>
        {
            var before = await client.GetDeviceAsync(id);
            var volume = before.Settings?.SpeakerVolume ?? 0;
            var raw = await client.SendRawAsync(HttpMethod.Post, path + "/settings",
                new JsonObject { ["speaker_volume"] = volume });
            await SaveAsync(folder, "settings_update", "POST " + path + "/settings {speaker_volume}", raw);
            var after = await client.GetDeviceAsync(id);
            return after.Settings?.SpeakerVolume == volume
                ? $"speaker_volume {volume} written back and read again"
                : throw new InvalidOperationException($"speaker_volume changed: {volume} -> {after.Settings?.SpeakerVolume}");
        });

        // 2. Snapshot (typed) - the device answers within ~30 s.
        await StepAsync(results, "snapshot", async () =>
        {
            var sw = Stopwatch.StartNew();
            var snap = await client.GetSnapshotAsync(id);
            var file = Path.Combine(folder, "snapshot.jpg");
            await File.WriteAllBytesAsync(file, snap.Jpeg);
            return $"{snap.Jpeg.Length / 1024} KB in {sw.Elapsed.TotalSeconds:0.0} s, taken {snap.TakenAt:u} (saved as snapshot.jpg, not to share)";
        });

        // 3. Webhook: create (raw), find (typed), delete (typed).
        await StepAsync(results, "webhook", async () =>
        {
            var raw = await client.SendRawAsync(HttpMethod.Post, path + "/triggers", new WebhookTrigger
            {
                Url = WebhookUrl,
                Events = new[] { SkyBellEventTypes.VideoReady },
                HeaderName = "x-selftest",
                HeaderValue = "1"
            });
            await SaveAsync(folder, "trigger_create", "POST " + path + "/triggers", raw);

            var triggers = await client.GetTriggersAsync(id);
            await SaveAsync(folder, "trigger_list", "GET " + path + "/triggers",
                await client.GetRawAsync(path + "/triggers"));
            var created = triggers.FirstOrDefault(t =>
                t.Extra is not null && JsonSerializer.Serialize(t.Extra).Contains("skybellsharp-selftest", StringComparison.Ordinal))
                ?? triggers.FirstOrDefault(t => t.Events.SequenceEqual(new[] { SkyBellEventTypes.VideoReady }));
            if (created is null)
                throw new InvalidOperationException($"created trigger not found among {triggers.Count}");

            await client.DeleteTriggerAsync(id, created.TriggerId);
            var left = await client.GetTriggersAsync(id);
            return left.Any(t => t.TriggerId == created.TriggerId)
                ? throw new InvalidOperationException("trigger still listed after delete")
                : $"created and deleted trigger (type {created.Type}, events {string.Join(",", created.Events)})";
        });

        // 4. Location: create (raw), find (typed), delete (typed).
        await StepAsync(results, "location", async () =>
        {
            var raw = await client.SendRawAsync(HttpMethod.Post, "/api/v5/locations", new LocationDetails
            {
                Location = new GeoPosition { Latitude = "0", Longitude = "0", Uncertainty = "-1" },
                BuildingInformation = new BuildingInformation
                {
                    Name = LocationName, Street1 = "", Street2 = "", City = "", State = "", ZipCode = "",
                    Description = "Created and deleted by the SkyBellSharp self-test"
                },
                Contact1FullName = "", Contact1PhoneNumber = "", Contact1Note = "",
                Contact2FullName = "", Contact2PhoneNumber = "", Contact2Note = ""
            });
            await SaveAsync(folder, "location_create", "POST /api/v5/locations", raw);

            var locations = await client.GetLocationsAsync();
            await SaveAsync(folder, "location_list", "GET /api/v5/locations", await client.GetRawAsync("/api/v5/locations"));
            var created = locations.FirstOrDefault(l => l.Detail?.BuildingInformation?.Name == LocationName)
                          ?? throw new InvalidOperationException($"created location not found among {locations.Count}");

            await client.DeleteLocationAsync(created.Id);
            var left = await client.GetLocationsAsync();
            return left.Any(l => l.Id == created.Id)
                ? throw new InvalidOperationException("location still listed after delete")
                : "created and deleted test location";
        });

        // 5. Test tone: audible, so only on confirmation.
        Console.Write("Play the current button tone on the doorbell (it will ring outside)? [y/N] ");
        if (string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            await StepAsync(results, "test tone", async () =>
            {
                var current = (await client.GetDeviceAsync(id)).Settings?.ChimeFile;
                var file = string.IsNullOrEmpty(current) ? "1.wav" : current;
                var raw = await client.SendRawAsync(HttpMethod.Post, path + "/testtone", new { file_name = file });
                await SaveAsync(folder, "testtone", "POST " + path + "/testtone", raw);
                return $"played {file}";
            });
        }
        else results.Add(("test tone", true, "skipped"));

        // Summary
        Console.WriteLine();
        foreach (var (step, ok, detail) in results)
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {step,-10} {detail}");
        await File.WriteAllTextAsync(Path.Combine(folder, "summary.txt"),
            string.Join(Environment.NewLine, results.Select(r => $"{(r.Ok ? "OK" : "FAIL")}\t{r.Step}\t{r.Detail}")));
        Console.WriteLine("\nDone. Send the JSON files and summary.txt (not snapshot.jpg).");
    }

    private static async Task StepAsync(List<(string, bool, string)> results, string name, Func<Task<string>> action)
    {
        Console.WriteLine($"- {name}…");
        try
        {
            results.Add((name, true, await action()));
        }
        catch (SkyBellApiException ex)
        {
            results.Add((name, false, $"HTTP {(int?)ex.StatusCode}: {ex.ApiMessage ?? ex.Message}"));
            if (ex.ResponseBody is not null)
                await File.WriteAllTextAsync(Path.Combine(Path.GetFullPath("skybell-selftest"), name.Replace(' ', '_') + "_error.json"),
                    Dump.Scrub(ex.ResponseBody));
        }
        catch (Exception ex)
        {
            results.Add((name, false, ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static Task SaveAsync(string folder, string name, string request, JsonNode? response) =>
        File.WriteAllTextAsync(Path.Combine(folder, name + ".json"),
            new JsonObject { ["request"] = Dump.Scrub(request), ["response"] = Dump.Redact(response) }.ToJsonString(Indented));
}
