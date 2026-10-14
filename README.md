# SkyBellSharp

Unofficial .NET client for the **SkyBell Gen5** cloud API (`https://api.skybell.network`).

- Authentication: personal API key, email/password (SkyBell login endpoint or Amazon Cognito), automatic token refresh
- Devices: list, details (settings, hardware, telemetry), settings update, reboot, removal
- Live view: starts WebRTC sessions (Amazon Kinesis Video Streams signaling data, ICE servers, temporary credentials)
- Snapshots, activity history (paging, per-day previews, video download, deletion)
- Sharing, webhooks (triggers), tones (catalog, custom upload, test), locations, on-device rules, health
- Local network: UDP button/motion broadcasts and Wi-Fi provisioning of devices in AP mode

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

## Local events (UDP)

Devices broadcast a fixed packet on UDP port 5000 when the button is pressed or motion is detected.

```csharp
using SkyBellSharp.Local;

using var udp = new SkyBellUdpListener();
udp.EventReceived += (_, e) => Console.WriteLine($"{e.Kind} from {e.Source.Address}");
udp.Start(); // allow inbound UDP in the firewall
```

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

## Build & pack

```
dotnet build
dotnet test
dotnet pack src/SkyBellSharp -c Release -o artifacts
```

## License

MIT
