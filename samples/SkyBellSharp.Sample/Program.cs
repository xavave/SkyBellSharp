// Usage:
//   dotnet run --project samples/SkyBellSharp.Sample -- <api-key>
//   dotnet run --project samples/SkyBellSharp.Sample -- <email> <password>
//   dotnet run --project samples/SkyBellSharp.Sample -- udp
using SkyBellSharp;
using SkyBellSharp.Local;
using SkyBellSharp.Models;

if (args.Length == 1 && args[0] == "udp")
{
    using var udp = new SkyBellUdpListener { ReportUnknownPackets = true };
    udp.EventReceived += (_, e) => Console.WriteLine($"{e.ReceivedAt:T}  {e.Kind,-15} {e.Source.Address}");
    udp.Start();
    Console.WriteLine($"Listening on UDP {SkyBellUdpListener.DefaultPort}… (Enter to quit)");
    Console.ReadLine();
    return;
}

using var anonymous = SkyBellClient.Anonymous();
var health = await anonymous.GetHealthAsync();
Console.WriteLine($"SkyBell API {health.ApiVersion} ({health.Environment})");

SkyBellClient client = args.Length switch
{
    1 => SkyBellClient.WithApiKey(args[0]),
    2 => await SkyBellClient.LoginAsync(args[0], args[1]),
    _ => throw new ArgumentException("Pass an API key, or an email and a password.")
};

using (client)
{
    var user = await client.GetUserAsync();
    Console.WriteLine($"User: {user.Email} ({user.PremiumStatus})");

    foreach (var d in await client.GetDevicesAsync())
    {
        Console.WriteLine($"- {d}  fw {d.Firmware}  online={d.IsOnline}  wifi={d.Telemetry?.SignalLevel} dBm");
    }

    var page = await client.GetActivitiesAsync(new ActivityQuery { Limit = 10 });
    Console.WriteLine($"Last {page.Rows.Count} of {page.Count} events:");
    foreach (var a in page.Rows)
        Console.WriteLine($"  {a.EventTime?.ToLocalTime():g}  {a.EventType,-8} {a.DeviceName}");
}
