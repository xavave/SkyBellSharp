using SkyBellSharp.Auth;
using SkyBellSharp.Models;

namespace SkyBellSharp.Tests;

/// <summary>Shapes observed on the production API (October 2026), which differ from the docs.</summary>
public class RealResponseTests
{
    private static (SkyBellClient Client, FakeHandler Handler) Create(params string[] responses)
    {
        var handler = new FakeHandler();
        foreach (var r in responses) handler.Enqueue(r);
        return (new SkyBellClient(new HttpClient(handler), new ApiKeyAuthenticator("adb4424f-09cf-42de-bec9-2a34f8f1d417")), handler);
    }

    [Fact]
    public async Task Activity_day_is_a_list_of_counts()
    {
        var (client, _) = Create("""
            {"error":false,"status":200,"message":"OK","data":[
              {"activity_day":"2026-10-13","event_count":"16"},
              {"activity_day":"2026-10-14","event_count":"17"}]}
            """);

        var days = await client.GetActivityDaysAsync();

        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 14), days[0].Day);
        Assert.Equal(17, days[0].EventCount);
    }

    [Fact]
    public async Task Activity_limit_and_offset_are_applied_when_the_server_ignores_them()
    {
        var rows = string.Join(",", Enumerable.Range(0, 10).Select(i =>
            $$"""{"activity_id":"a{{i}}","event_type":"motion","event_time":{{1791997104335 - i * 1000}},"video_url":"/activity/a{{i}}/video","video_ready":true,"video_size":42,"edge_tags":["motion"]}"""));
        var body = $$$"""
            {"error":false,"status":200,"message":"Requested multiple days. NOT returning images.",
             "data":{"start":1791401499402,"end":1792006329402,"count":10,"rows":[{{{rows}}}],"offset":0,"limit":100000,"imagesIncluded":false}}
            """;
        var (client, _) = Create(body, body);

        var page = await client.GetActivitiesAsync(new ActivityQuery { Limit = 3, Offset = 4 });

        Assert.Equal(10, page.Count);
        Assert.Equal(new[] { "a4", "a5", "a6" }, page.Rows.Select(r => r.ActivityId));
        Assert.False(page.ImagesIncluded);
        Assert.True(page.Rows[0].VideoReady);
        Assert.Equal("motion", Assert.Single(page.Rows[0].EdgeTags!));

        var all = new List<SkyBellActivity>();
        await foreach (var a in client.EnumerateActivitiesAsync(pageSize: 100)) all.Add(a);
        Assert.Equal(10, all.Count);
    }

    [Fact]
    public async Task Device_rules_and_default_rule_sets()
    {
        var (client, _) = Create(
            """
            {"error":false,"message":"Got rules.","data":{"rules":[
              {"rule_num":1,"internal":false,"triggers":[{"event":"PIRMotion","count":1}],"window":0,"motion_rule":true,
               "actions":{"notify":null,"pre_roll":true,"pre_roll_duration":10},"match_count":1945}]}}
            """,
            """
            {"error":false,"message":"Currently featured rules","data":{
              "Base Rules":[{"triggers":[{"event":"PIRMotion","count":1}],"window":0,"actions":{"pre_roll":true}}],
              "Documented shape":{"rules":[{"triggers":[{"event":"FDMotion","count":1}],"window":10,"actions":{"record":true}}]}}}
            """);

        var rules = await client.GetDeviceRulesAsync("dev");
        var sets = await client.GetDefaultRuleSetsAsync();

        var r = Assert.Single(rules);
        Assert.Equal(1945, r.MatchCount);
        Assert.Equal("PIRMotion", r.Triggers[0].Event);
        Assert.True(r.Actions!["pre_roll"]!.GetValue<bool>());
        Assert.Equal(2, sets.Count);
        Assert.Equal("FDMotion", sets["Documented shape"][0].Triggers[0].Event);
    }

    [Fact]
    public async Task Device_with_string_coordinates_and_extended_telemetry()
    {
        var (client, _) = Create("""
            {"error":false,"message":"OK","data":{"device_id":"d","hardware":"1:SKYBELL_MICRO_1000020","firmware":"2.0.13",
             "last_event":"1791997104364","lat":"48.8796357334326","lon":"2.3198012838774","shared":false,"using_rules":false,
             "basic_motion":{"motion_notify":true,"motion_record":true,"fd_notify":false},
             "settings":{"led_color":"","brightness":2,"pir_sensitivity":816,"motion_sensitivity":846,"device_lat":48.87,
                         "basic_motion":{"motion_notify":true},"time_zone_info":{"place":"x"}},
             "device_settings":{"model_rev":"SKYBELL_MICRO_1000020","firmware_minor_release":0},
             "telemetry":{"ip_address":"192.168.1.3","battery_voltage":4419,"wifi_noise":"","boot_time":1788721450,"timestamp":1792003470389}}}
            """);

        var d = await client.GetDeviceAsync("d");

        Assert.Equal(48.8796357334326, d.Latitude!.Value, 6);
        Assert.False(d.IsShared);
        Assert.True(d.BasicMotion!.MotionNotify);
        Assert.Equal(816.0, d.Settings!.PirSensitivity);
        Assert.Equal("192.168.1.3", d.Telemetry!.IpAddress);
        Assert.Equal(4419, d.Telemetry.BatteryVoltage);
    }

    [Fact]
    public async Task Api_key_management_requires_a_jwt_session()
    {
        var (client, handler) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetApiKeyAsync());
        Assert.Empty(handler.Requests);
    }
}
