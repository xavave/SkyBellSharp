# SkyBellSharp

[![CI](https://github.com/xavave/SkyBellSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/xavave/SkyBellSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SkyBellSharp.svg)](https://www.nuget.org/packages/SkyBellSharp)

Unofficial .NET client for the **SkyBell Gen5** cloud API (`https://api.skybell.network`).

- Authentication: personal API key, email/password (SkyBell login endpoint or Amazon Cognito), automatic token refresh
- Devices: list, details (settings, hardware, telemetry), settings update, reboot, removal
- Live view: starts WebRTC sessions (Amazon Kinesis Video Streams signaling data, ICE servers, temporary credentials)
- Snapshots, activity history (paging, per-day previews, video download, deletion)
- Sharing, webhooks (triggers), tones (catalog, custom upload, test), locations, on-device rules, health
- Real-time events: `SkyBellEventMonitor` combines local UDP broadcasts with API confirmation and polling
- Local network: UDP button/motion broadcasts and Wi-Fi provisioning of devices in AP mode
- Raw access (`GetRawAsync` / `SendRawAsync`) for anything not modelled yet

> **Unofficial project.** Not affiliated with, endorsed by, or supported by SkyBell Technologies, Inc.
> "SkyBell" is a trademark of its owner. Use it with your own account and respect SkyBell's terms of service.
> SkyBell asks API consumers **not to poll more often than once every 60 seconds**.

## Install

```
dotnet add package SkyBellSharp
```

Targets .NET 10.

## Quick start

```csharp
using SkyBellSharp;
using SkyBellSharp.Models;

// Personal API key: https://api.skybell.network/developers
using var client = SkyBellClient.WithApiKey("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx");

var user = await client.GetUserAsync();
var devices = await client.GetDevicesAsync();
var door = devices[0];

// Fresh picture (the device answers within ~30 s, otherwise SkyBellApiException.IsDeviceTimeout)
var snapshot = await client.GetSnapshotAsync(door.Id);
await File.WriteAllBytesAsync("door.jpg", snapshot.Jpeg);

// Last doorbell presses
var page = await client.GetActivitiesAsync(new ActivityQuery
{
    DeviceId = door.Id,
    EventType = SkyBellEventTypes.Doorbell,
    Limit = 20
});

// Download the latest clip
await using var mp4 = File.Create("last.mp4");
await client.DownloadActivityVideoAsync(page.Rows[0].ActivityId, mp4);

// Change settings (only non-null values are sent)
await client.UpdateSettingsAsync(door.Id, new DeviceSettingsUpdate { LedColor = "#0055CC", MotionSensitivity = 2 });
```

### Email / password

```csharp
using var client = await SkyBellClient.LoginAsync("me@example.com", "password");

// Get (or create) a personal API key to avoid storing the password
var apiKey = await client.GetOrCreateApiKeyAsync();
```

To reuse a session, persist `SkyBellLoginAuthenticator.Tokens` (raised by `TokensRefreshed`) and
recreate the authenticator with `new SkyBellLoginAuthenticator(http, tokens)`.
`CognitoAuthenticator` logs in directly against the SkyBell Cognito user pool like the mobile app
(requires the user-pool app client id).

### With IHttpClientFactory / DI

```csharp
services.AddHttpClient("skybell");
services.AddSingleton(sp => new SkyBellClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("skybell"),
    new ApiKeyAuthenticator(configuration["SkyBell:ApiKey"]!)));
```

## Live view (WebRTC)

`StartLiveStreamAsync` returns everything a **KVS WebRTC viewer** needs: channel ARN, signaling
endpoints (`WssEndpoint`), ICE servers and temporary AWS credentials. Feed them to the
[Kinesis Video Streams WebRTC SDK](https://github.com/awslabs/amazon-kinesis-video-streams-webrtc-sdk-js)
(e.g. in a WebView2) as a `VIEWER`. Call `StopLiveStreamAsync` when done: it also finalizes the
recording.

```csharp
var session = await client.StartLiveStreamAsync(door.Id);
// session.ChannelArn, session.WssEndpoint, session.IceServers, session.Credentials, session.Region
```

## Real-time events

`SkyBellEventMonitor` raises doorbell presses and motion as they happen:

- **UDP** (same LAN): a broadcast triggers immediate API lookups (2, 4, 6, 8 s) to get the
  confirmed activity, its type and preview; if the cloud does not confirm it, an
  `UdpOnly` event is still raised;
- **polling** every 60 s (the minimum allowed by SkyBell) as a fallback when away from home.

Each activity is raised once; events older than `Start()` are ignored; live views are skipped.

```csharp
using var monitor = new SkyBellEventMonitor(client, new SkyBellEventMonitorOptions { DeviceId = door.Id });
monitor.EventDetected += (_, e) =>
    Console.WriteLine($"{e.EventType} at {e.Time:T} via {e.Source}"); // e.Activity has the id, preview…
monitor.CheckFailed += (_, ex) => Console.WriteLine(ex.Message);
monitor.Start();
```

## Local events (UDP)

Devices broadcast a fixed packet on UDP port 5000 when the button is pressed or motion is detected.

```csharp
using SkyBellSharp.Local;

using var udp = new SkyBellUdpListener();
udp.EventReceived += (_, e) => Console.WriteLine($"{e.Kind} from {e.Source.Address}");
udp.Start(); // allow inbound UDP in the firewall
```

Packets matching the two samples of the SkyBell docs are reported as `ButtonPressed` /
`MotionDetected`; other SkyBell-formatted packets (encrypted payloads can differ) as `Unidentified`.

## Raw access

```csharp
JsonNode? shares = await client.GetRawAsync("/api/v5/shares");
```

Returns the whole JSON response (envelope included) for endpoints or fields the models do not cover.

## Error handling

Every API failure throws `SkyBellApiException` with `StatusCode`, `ApiMessage`, and helpers
`IsDeviceTimeout` (HTTP 599: the device did not answer within 30 s), `IsAuthenticationError`,
`IsNotFound`. JWT authenticators refresh the token before expiry and retry once after a 401.

## API coverage

| Area | Endpoints |
|---|---|
| Auth | `POST /api/v5/login`, `PUT /api/v5/token`, `GET /api/v5/logout`, Cognito `InitiateAuth`, API key header |
| User | `GET/POST/DELETE /api/v5/user`, `GET/POST/DELETE /api/v5/user/api_access` |
| Devices | `GET /api/v5/devices`, `GET/DELETE /api/v5/devices/{id}`, `POST …/settings`, `POST …/reboot`, `GET …/snapshot` |
| Live | `POST/DELETE /api/v5/devices/{id}/videostream` |
| Activity | `GET /api/v5/activity`, `GET/DELETE /api/v5/activity/day`, `GET /api/v5/activity/{id}/video`, `DELETE /api/v5/activity/{id}` |
| Sharing | `POST /api/v5/devices/{id}/share`, `GET /api/v5/shares`, `GET …/{id}/redeem`, `PUT/POST/DELETE /api/v5/shares/{id}` |
| Triggers | `GET/POST /api/v5/devices/{id}/triggers`, `DELETE …/triggers/{triggerId}` |
| Tones | `GET /api/v5/tones`, `GET /api/v5/tones/chime`, `POST/PUT /api/v5/devices/{id}/tone`, `POST …/testtone` |
| Locations | `GET/POST /api/v5/locations`, `POST/DELETE /api/v5/locations/{id}` |
| Rules | `GET /api/v5/rules`, `GET /api/v5/devices/{id}/rules` |
| Misc | `GET /health`, `POST/DELETE /api/v5/notification_token`, `GET /api/v5/devices/provision` |
| Local | UDP broadcasts (port 5000), `GET /ap/scan`, `POST /ap/provision` |

Not covered: legacy SkyBell HD CoAP provisioning, OAuth 2.0 partner flow.

## Sample app

```
dotnet run --project samples/SkyBellSharp.Sample -- <api-key>          # account, devices, last events
dotnet run --project samples/SkyBellSharp.Sample -- <api-key> watch    # real-time events
dotnet run --project samples/SkyBellSharp.Sample -- <api-key> dump     # raw responses -> ./skybell-dump
dotnet run --project samples/SkyBellSharp.Sample -- udp                # raw UDP broadcasts
```

`dump` only calls read-only endpoints and redacts secrets and personal data (tokens, keys,
emails, serials, MAC addresses, coordinates, ids, signed URLs). Attaching those files to an issue
helps fix models for undocumented responses — review them before sharing.

## Build & pack

```
dotnet build
dotnet test
dotnet pack src/SkyBellSharp -c Release -o artifacts
```

## Releasing

Pushing a `vX.Y.Z` tag runs `.github/workflows/release.yml`: build, tests, pack with that
version, push to nuget.org (secret `NUGET_API_KEY`) and GitHub release with the packages.

## License

MIT
