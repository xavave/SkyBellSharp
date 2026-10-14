using System.Net;
using SkyBellSharp.Auth;
using SkyBellSharp.Local;
using SkyBellSharp.Models;

namespace SkyBellSharp.Tests;

public class ClientTests
{
    private const string ApiKey = "adb4424f-09cf-42de-bec9-2a34f8f1d417";

    private static (SkyBellClient Client, FakeHandler Handler) Create()
    {
        var handler = new FakeHandler();
        var client = new SkyBellClient(new HttpClient(handler), new ApiKeyAuthenticator(ApiKey));
        return (client, handler);
    }

    [Fact]
    public async Task ApiKey_header_is_sent_and_no_app_header()
    {
        var (client, handler) = Create();
        handler.Enqueue("""{"error":false,"message":"OK","data":{"email":"a@b.c","account_id":"x","premium_status":"free"}}""");

        var user = await client.GetUserAsync();

        Assert.Equal("a@b.c", user.Email);
        var req = handler.Requests.Single();
        Assert.Equal(ApiKey, req.Headers.GetValues(ApiKeyAuthenticator.HeaderName).Single());
        Assert.False(req.Headers.Contains("x-skybell-app"));
        Assert.Equal("/api/v5/user", req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Devices_are_read_from_rows()
    {
        var (client, handler) = Create();
        handler.Enqueue("""
            {"error":false,"message":"OK","data":{"count":1,"rows":[{
              "device_id":"b14b2fa8","name":"Front Door","serial":"A3BAB00473","firmware":"0.5.11",
              "last_event":"1650578210829","last_connected":"2022-04-21T21:32:32.825Z","last_disconnected":"2022-04-21T18:25:36.569Z",
              "settings":{"led_color":"#0055CC","motion_zone_config":{"x":2,"y":1,"zones":[[{"ignore":false,"sensitivity":100},{"ignore":true,"sensitivity":50}]],"threshold":1}},
              "telemetry":{"signal_level":"-38","boot_time":1649773647,"timestamp":1649786322438}}]}}
            """);

        var devices = await client.GetDevicesAsync();

        var d = Assert.Single(devices);
        Assert.Equal("b14b2fa8", d.Id);
        Assert.Equal("#0055CC", d.Settings!.LedColor);
        Assert.True(d.Settings.MotionZoneConfig!.Zones[0][1].Ignore);
        Assert.True(d.IsOnline);
        Assert.Equal(2022, d.LastEvent!.Value.Year);
        Assert.Equal(2022, d.Telemetry!.BootTime!.Value.Year);
        Assert.Equal(2022, d.Telemetry.Timestamp!.Value.Year);
    }

    [Fact]
    public async Task Activity_query_string_and_parsing()
    {
        var (client, handler) = Create();
        handler.Enqueue("""
            {"error":false,"status":200,"message":"OK","data":{"count":1,"start":"2022-11-08T15:22:20.447Z","end":null,
             "rows":[{"event_time":1648493804113,"device_id":"dd76","device_name":"Front Door","activity_id":"a629",
                      "event_type":"doorbell","date":"03/28/2022","image":"AQID"}]}}
            """);

        var page = await client.GetActivitiesAsync(new ActivityQuery { DeviceId = "dd76", EventType = "doorbell", Limit = 5 });

        var q = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("limit=5", q);
        Assert.Contains("device=dd76", q);
        Assert.Contains("type=doorbell", q);
        Assert.Contains("nopreviews=true", q);
        var a = Assert.Single(page.Rows);
        Assert.True(a.IsDoorbell);
        Assert.Equal(new byte[] { 1, 2, 3 }, a.GetImageBytes());
        Assert.Equal(2022, a.EventTime!.Value.Year);
    }

    [Fact]
    public async Task Snapshot_decodes_base64()
    {
        var (client, handler) = Create();
        handler.Enqueue("""{"error":false,"message":"OK","data":{"date_time":"2022-04-12T18:18:31.000Z","preview":"/9j/AA=="}}""");

        var snap = await client.GetSnapshotAsync("dev");

        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }, snap.Jpeg);
        Assert.Equal(2022, snap.TakenAt!.Value.Year);
    }

    [Fact]
    public async Task VideoStream_parses_ice_urls_string_or_array()
    {
        var (client, handler) = Create();
        handler.Enqueue("""
            {"error":false,"message":"OK","data":{"state":true,
              "ice":[{"urls":"stun:a:443"},{"urls":["turn:b:443?transport=udp","turns:b:443?transport=tcp"],"username":"u","credential":"c"}],
              "endpoints":[{"Protocol":"HTTPS","ResourceEndpoint":"https://r"},{"Protocol":"WSS","ResourceEndpoint":"wss://v"}],
              "channelARN":"arn:aws:kinesisvideo:us-east-1:5261:channel/123_dev/1707"}}
            """);

        var s = await client.StartLiveStreamAsync("dev");

        Assert.Equal(2, s.IceServers.Count);
        Assert.Single(s.IceServers[0].Urls);
        Assert.Equal(2, s.IceServers[1].Urls.Count);
        Assert.Equal("wss://v", s.WssEndpoint);
        Assert.Equal("us-east-1", s.Region);
    }

    [Fact]
    public async Task Error_status_maps_to_exception()
    {
        var (client, handler) = Create();
        handler.Enqueue("""{"error":true,"message":"Device did not respond"}""", (HttpStatusCode)599);

        var ex = await Assert.ThrowsAsync<SkyBellApiException>(() => client.GetSnapshotAsync("dev"));

        Assert.True(ex.IsDeviceTimeout);
        Assert.Equal("Device did not respond", ex.ApiMessage);
    }

    [Fact]
    public async Task Error_flag_with_200_maps_to_exception()
    {
        var (client, handler) = Create();
        handler.Enqueue("""{"error":true,"message":"nope"}""");

        var ex = await Assert.ThrowsAsync<SkyBellApiException>(() => client.GetUserAsync());
        Assert.Equal("nope", ex.ApiMessage);
    }

    [Fact]
    public async Task Settings_update_sends_only_non_null_values()
    {
        var (client, handler) = Create();
        handler.Enqueue("""{"error":false,"message":"OK","data":{}}""");

        await client.UpdateSettingsAsync("dev", new DeviceSettingsUpdate
        {
            LedColor = "#FF33FF",
            Extra = new() { ["custom"] = 1 }
        });

        var body = handler.Bodies.Single()!;
        Assert.Contains("\"led_color\":\"#FF33FF\"", body);
        Assert.Contains("\"custom\":1", body);
        Assert.DoesNotContain("brightness", body);
    }

    [Theory]
    [InlineData("""{"api_key":"adb4424f-09cf-42de-bec9-2a34f8f1d417"}""")]
    [InlineData("\"adb4424f-09cf-42de-bec9-2a34f8f1d417\"")]
    [InlineData("""{"account_id":"11111111-1111-1111-1111-111111111111","access":{"user_key":"adb4424f-09cf-42de-bec9-2a34f8f1d417"}}""")]
    public void ApiKey_is_extracted_from_various_shapes(string data)
    {
        Assert.Equal(ApiKey, SkyBellClient.ExtractApiKey(System.Text.Json.Nodes.JsonNode.Parse(data)));
    }

    [Fact]
    public async Task Jwt_is_refreshed_once_after_401()
    {
        var handler = new FakeHandler()
            .Enqueue("{}", HttpStatusCode.Unauthorized)
            .Enqueue("""{"error":false,"message":"Refreshed","data":{"AccessToken":"a2","IdToken":"id2","ExpiresIn":3600}}""")
            .Enqueue("""{"error":false,"message":"OK","data":{"email":"a@b.c"}}""");
        var http = new HttpClient(handler);
        var tokens = new SkyBellTokens { IdToken = "id1", AccessToken = "a1", RefreshToken = "r", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        var auth = new SkyBellLoginAuthenticator(http, tokens);
        using var client = new SkyBellClient(http, auth);

        var user = await client.GetUserAsync();

        Assert.Equal("a@b.c", user.Email);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/api/v5/token", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("id2", handler.Requests[2].Headers.GetValues("authorization").Single());
        Assert.Equal("r", tokens.RefreshToken);
    }

    [Fact]
    public void Udp_packets_are_classified()
    {
        Assert.Equal(49, SkyBellUdpListener.ButtonPacket.Length);
        Assert.Equal(SkyBellUdpEventKind.ButtonPressed, SkyBellUdpListener.Classify(SkyBellUdpListener.ButtonPacket.Span));
        Assert.Equal(SkyBellUdpEventKind.MotionDetected, SkyBellUdpListener.Classify(SkyBellUdpListener.MotionPacket.Span));
        Assert.Equal(SkyBellUdpEventKind.Unknown, SkyBellUdpListener.Classify(new byte[] { 1, 2, 3 }));
    }
}
