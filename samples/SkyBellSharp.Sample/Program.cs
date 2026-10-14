// Usage:
//   dotnet run --project samples/SkyBellSharp.Sample -- <api-key>                 summary
//   dotnet run --project samples/SkyBellSharp.Sample -- <email> <password>        summary
//   dotnet run --project samples/SkyBellSharp.Sample -- <api-key> dump            raw responses -> ./skybell-dump (redacted)
//   dotnet run --project samples/SkyBellSharp.Sample -- <api-key> watch           live events (UDP + API)
//   dotnet run --project samples/SkyBellSharp.Sample -- <api-key> selftest        reversible write tests -> ./skybell-selftest
//   dotnet run --project samples/SkyBellSharp.Sample -- udp                       raw UDP broadcasts
using SkyBellSharp;
using SkyBellSharp.Local;
using SkyBellSharp.Models;
using SkyBellSharp.Sample;

if (args.Length == 1 && args[0] == "udp")
{
    using var udp = new SkyBellUdpListener { ReportUnknownPackets = true };
    udp.EventReceived += (_, e) =>
        Console.WriteLine($"{e.ReceivedAt:T}  {e.Kind,-15} {e.Source.Address,-15} {Convert.ToHexString(e.Payload)}");
    udp.Start();
    Console.WriteLine($"Listening on UDP {SkyBellUdpListener.DefaultPort}… (Enter to quit)");
    Console.ReadLine();
    return;
}

var command = args.Length is 2 or 3 && args[^1] is "dump" or "watch" or "selftest" ? args[^1] : "summary";
var credentials = command == "summary" ? args : args[..^1];

using var anonymous = SkyBellClient.Anonymous();
var health = await anonymous.GetHealthAsync();
Console.WriteLine($"SkyBell API {health.ApiVersion} ({health.Environment})");

SkyBellClient client = credentials.Length switch
{
    1 => SkyBellClient.WithApiKey(credentials[0]),
    2 => await SkyBellClient.LoginAsync(credentials[0], credentials[1]),
    _ => throw new ArgumentException("Pass an API key, or an email and a password.")
};

using (client)
{
    switch (command)
    {
        case "dump":
            await Dump.RunAsync(client, Path.GetFullPath("skybell-dump"));
            break;

        case "selftest":
            await SelfTest.RunAsync(client, Path.GetFullPath("skybell-selftest"));
            break;

        case "watch":
            using (var monitor = new SkyBellEventMonitor(client))
            {
                monitor.EventDetected += (_, e) =>
                    Console.WriteLine($"{e.Time.ToLocalTime():T}  {e.EventType,-9} via {e.Source,-8} {e.Activity?.DeviceName} {e.RemoteAddress}");
                monitor.CheckFailed += (_, ex) => Console.WriteLine("check failed: " + ex.Message);
                monitor.Start();
                Console.WriteLine(monitor.IsUdpActive
                    ? "Watching (UDP + API every 60 s)… Enter to quit"
                    : $"Watching (API every 60 s, UDP unavailable: {monitor.UdpError?.Message})… Enter to quit");
                Console.ReadLine();
            }
            break;

        default:
            var user = await client.GetUserAsync();
            Console.WriteLine($"User: {user.Email} ({user.PremiumStatus})");

            foreach (var d in await client.GetDevicesAsync())
                Console.WriteLine($"- {d}  fw {d.Firmware}  online={d.IsOnline}  wifi={d.Telemetry?.SignalLevel} dBm");

            var page = await client.GetActivitiesAsync(new ActivityQuery { Limit = 10 });
            Console.WriteLine($"Last {page.Rows.Count} of {page.Count} events:");
            foreach (var a in page.Rows)
                Console.WriteLine($"  {a.EventTime?.ToLocalTime():g}  {a.EventType,-8} {a.DeviceName}");
            break;
    }
}
