using SkyBellSharp.Auth;
using SkyBellSharp.Local;

namespace SkyBellSharp.Tests;

public class MonitorTests
{
    private static string ActivityJson(string id, string type, DateTimeOffset time) => $$$"""
        {"error":false,"message":"OK","data":{"count":1,"rows":[
          {"activity_id":"{{{id}}}","device_id":"dev","device_name":"Front Door","event_type":"{{{type}}}","event_time":{{{time.ToUnixTimeMilliseconds()}}}}]}}
        """;

    [Fact]
    public async Task Raises_each_new_activity_once_and_skips_live_streams()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new FakeHandler()
            .Enqueue(ActivityJson("a1", "doorbell", now.AddSeconds(1)))
            .Enqueue(ActivityJson("a1", "doorbell", now.AddSeconds(1)))
            .Enqueue(ActivityJson("a2", "lstream", now.AddSeconds(2)));
        using var client = new SkyBellClient(new HttpClient(handler), new ApiKeyAuthenticator("adb4424f-09cf-42de-bec9-2a34f8f1d417"));
        using var monitor = new SkyBellEventMonitor(client, new SkyBellEventMonitorOptions
        {
            UseUdp = false,
            PollInterval = Timeout.InfiniteTimeSpan
        });
        var events = new List<SkyBellEvent>();
        monitor.EventDetected += (_, e) => events.Add(e);
        monitor.Start();

        Assert.Equal(1, await monitor.CheckNowAsync());
        Assert.Equal(0, await monitor.CheckNowAsync());
        Assert.Equal(0, await monitor.CheckNowAsync());

        var e = Assert.Single(events);
        Assert.True(e.IsDoorbell);
        Assert.Equal("a1", e.Activity!.ActivityId);
        Assert.Equal(SkyBellEventSource.Manual, e.Source);
        Assert.Contains("start=", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Ignores_activities_older_than_start()
    {
        var handler = new FakeHandler().Enqueue(ActivityJson("old", "doorbell", DateTimeOffset.UtcNow.AddMinutes(-10)));
        using var client = new SkyBellClient(new HttpClient(handler), new ApiKeyAuthenticator("adb4424f-09cf-42de-bec9-2a34f8f1d417"));
        using var monitor = new SkyBellEventMonitor(client, new() { UseUdp = false, PollInterval = Timeout.InfiniteTimeSpan });
        monitor.Start();

        Assert.Equal(0, await monitor.CheckNowAsync());
    }

    [Fact]
    public void Skybell_looking_packets_are_unidentified_not_unknown()
    {
        var payload = "U2FsdGVkX19AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n"u8.ToArray();
        var packet = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(packet, 0);
        payload.CopyTo(packet, 4);

        Assert.True(SkyBellUdpListener.LooksLikeSkyBell(packet));
        Assert.Equal(SkyBellUdpEventKind.Unidentified, SkyBellUdpListener.Classify(packet));
        Assert.True(SkyBellUdpListener.LooksLikeSkyBell(SkyBellUdpListener.ButtonPacket.Span));
    }
}
