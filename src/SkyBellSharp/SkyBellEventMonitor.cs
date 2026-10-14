using System.Net;
using System.Net.Sockets;
using SkyBellSharp.Local;
using SkyBellSharp.Models;

namespace SkyBellSharp;

/// <summary>How an event reached the <see cref="SkyBellEventMonitor"/>.</summary>
public enum SkyBellEventSource
{
    /// <summary>A UDP broadcast, confirmed by the API (activity available).</summary>
    Udp,
    /// <summary>Periodic API check.</summary>
    Poll,
    /// <summary>A UDP broadcast the API did not confirm in time (no activity, type guessed).</summary>
    UdpOnly,
    /// <summary>Explicit <see cref="SkyBellEventMonitor.CheckNowAsync"/> call.</summary>
    Manual
}

/// <summary>Event raised by <see cref="SkyBellEventMonitor"/>.</summary>
public sealed class SkyBellEvent
{
    public SkyBellEvent(SkyBellActivity? activity, string eventType, DateTimeOffset time, SkyBellEventSource source,
        IPAddress? remoteAddress = null)
    {
        Activity = activity;
        EventType = eventType;
        Time = time;
        Source = source;
        RemoteAddress = remoteAddress;
    }

    /// <summary>The cloud activity (null for <see cref="SkyBellEventSource.UdpOnly"/>).</summary>
    public SkyBellActivity? Activity { get; }

    /// <summary>See <see cref="SkyBellEventTypes"/>.</summary>
    public string EventType { get; }

    public DateTimeOffset Time { get; }
    public SkyBellEventSource Source { get; }

    /// <summary>LAN address of the device that broadcast the UDP packet, if any.</summary>
    public IPAddress? RemoteAddress { get; }

    public bool IsDoorbell => string.Equals(EventType, SkyBellEventTypes.Doorbell, StringComparison.OrdinalIgnoreCase);
    public bool IsMotion => string.Equals(EventType, SkyBellEventTypes.Motion, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Time:u} {EventType} ({Source})";
}

/// <summary>Options of <see cref="SkyBellEventMonitor"/>.</summary>
public sealed class SkyBellEventMonitorOptions
{
    /// <summary>Only watch this device (null = every device of the account).</summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Interval of the fallback API check. SkyBell asks not to poll faster than once a minute,
    /// so values below 60 s are raised to 60 s. Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable polling.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Listen to local UDP broadcasts for near-instant detection (same LAN only).</summary>
    public bool UseUdp { get; set; } = true;

    public int UdpPort { get; set; } = SkyBellUdpListener.DefaultPort;

    /// <summary>
    /// Delays between API lookups after a UDP packet: the activity appears in the cloud a few
    /// seconds after the event. Default 2, 4, 6, 8 s (~20 s in total).
    /// </summary>
    public IReadOnlyList<TimeSpan> UdpConfirmationDelays { get; set; } =
        new[] { 2, 4, 6, 8 }.Select(s => TimeSpan.FromSeconds(s)).ToArray();

    /// <summary>Raise an <see cref="SkyBellEventSource.UdpOnly"/> event when the API does not confirm a UDP packet.</summary>
    public bool RaiseUnconfirmedUdpEvents { get; set; } = true;

    /// <summary>Ignore live-view activities ("lstream").</summary>
    public bool IgnoreLiveStreams { get; set; } = true;

    /// <summary>
    /// Max events raised by a single check (after a long disconnection only the most recent
    /// ones are raised).
    /// </summary>
    public int MaxEventsPerCheck { get; set; } = 3;

    /// <summary>
    /// How far back a check looks. Kept under a day so the API returns preview images.
    /// </summary>
    public TimeSpan LookbackWindow { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Request base64 previews with the activities (only returned for ranges within one day).</summary>
    public bool IncludePreviews { get; set; } = true;
}

/// <summary>
/// Detects doorbell presses and motion as they happen by combining local UDP broadcasts
/// (instant, same LAN) with API checks (confirmation, preview, and fallback polling every 60 s).
/// Only events newer than <see cref="Start"/> are raised; each activity is raised once.
/// </summary>
/// <example>
/// <code>
/// using var monitor = new SkyBellEventMonitor(client, new() { DeviceId = door.Id });
/// monitor.EventDetected += (_, e) => Console.WriteLine(e);
/// monitor.Start();
/// </code>
/// </example>
public sealed class SkyBellEventMonitor : IDisposable
{
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(60);

    private readonly SkyBellClient _client;
    private readonly SkyBellEventMonitorOptions _options;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private SkyBellUdpListener? _udp;
    private DateTimeOffset _lastSeen;
    private DateTimeOffset _lastUdp = DateTimeOffset.MinValue;

    public SkyBellEventMonitor(SkyBellClient client, SkyBellEventMonitorOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new SkyBellEventMonitorOptions();
    }

    /// <summary>New event (raised on a background thread: marshal to the UI thread if needed).</summary>
    public event EventHandler<SkyBellEvent>? EventDetected;

    /// <summary>A check failed (network, auth…). The monitor keeps running.</summary>
    public event EventHandler<Exception>? CheckFailed;

    /// <summary>True while UDP listening is active.</summary>
    public bool IsUdpActive => _udp?.IsRunning == true;

    /// <summary>Why UDP listening could not start (port in use, firewall…), if it failed.</summary>
    public Exception? UdpError { get; private set; }

    public bool IsRunning => _cts is not null;

    /// <summary>Starts UDP listening (if enabled) and polling. Events before this call are ignored.</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            _lastSeen = DateTimeOffset.UtcNow.AddSeconds(-5);

            if (_options.UseUdp) StartUdp();

            var interval = _options.PollInterval;
            if (interval != Timeout.InfiniteTimeSpan)
            {
                if (interval < MinPollInterval) interval = MinPollInterval;
                _ = PollLoopAsync(interval, _cts.Token);
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _cts?.Dispose();
            _cts = null;
            _udp?.Dispose();
            _udp = null;
        }
    }

    public void Dispose() => Stop();

    /// <summary>Checks the API now; returns the number of events raised.</summary>
    public Task<int> CheckNowAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(SkyBellEventSource.Manual, cancellationToken);

    // ------------------------------------------------------------------

    private void StartUdp()
    {
        var udp = new SkyBellUdpListener(_options.UdpPort, debounce: TimeSpan.Zero);
        udp.EventReceived += OnUdpPacket;
        try
        {
            udp.Start();
            _udp = udp;
            UdpError = null;
        }
        catch (SocketException ex)
        {
            udp.Dispose();
            UdpError = ex;
        }
    }

    private void OnUdpPacket(object? sender, SkyBellUdpEventArgs e)
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        if (ct.IsCancellationRequested) return;

        // One press can produce several packets (possibly from several devices).
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (now - _lastUdp < TimeSpan.FromSeconds(4)) return;
            _lastUdp = now;
        }

        var hint = e.Kind switch
        {
            SkyBellUdpEventKind.ButtonPressed => SkyBellEventTypes.Doorbell,
            SkyBellUdpEventKind.MotionDetected => SkyBellEventTypes.Motion,
            _ => null
        };
        _ = ConfirmUdpAsync(hint, e.Source.Address, ct);
    }

    private async Task ConfirmUdpAsync(string? hint, IPAddress from, CancellationToken ct)
    {
        foreach (var delay in _options.UdpConfirmationDelays)
        {
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (await CheckAsync(SkyBellEventSource.Udp, ct, from).ConfigureAwait(false) > 0) return;
        }

        if (_options.RaiseUnconfirmedUdpEvents && !ct.IsCancellationRequested)
            Raise(new SkyBellEvent(null, hint ?? SkyBellEventTypes.Doorbell, DateTimeOffset.Now,
                SkyBellEventSource.UdpOnly, from));
    }

    private async Task PollLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await CheckAsync(SkyBellEventSource.Poll, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task<int> CheckAsync(SkyBellEventSource source, CancellationToken ct, IPAddress? from = null)
    {
        // A check is already running: its result will cover this one.
        if (!await _checkGate.WaitAsync(0, ct).ConfigureAwait(false)) return 0;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var floor = now - _options.LookbackWindow;
            var start = _lastSeen > floor ? _lastSeen : floor;

            var page = await _client.GetActivitiesAsync(new ActivityQuery
            {
                DeviceId = _options.DeviceId,
                Start = start,
                End = now.AddSeconds(5),
                Limit = 50,
                IncludePreviews = _options.IncludePreviews
            }, ct).ConfigureAwait(false);

            var fresh = page.Rows
                .Where(a => a.EventTime is { } t && t > _lastSeen && !_seen.Contains(a.ActivityId))
                .Where(a => !_options.IgnoreLiveStreams ||
                            !string.Equals(a.EventType, SkyBellEventTypes.LiveStream, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.EventTime)
                .ToList();

            foreach (var a in page.Rows)
            {
                if (a.EventTime is { } t && t > _lastSeen) _lastSeen = t;
                if (!string.IsNullOrEmpty(a.ActivityId)) _seen.Add(a.ActivityId);
            }

            foreach (var a in fresh.TakeLast(Math.Max(1, _options.MaxEventsPerCheck)))
                Raise(new SkyBellEvent(a, a.EventType ?? SkyBellEventTypes.Doorbell, a.EventTime ?? now, source,
                    source == SkyBellEventSource.Udp ? from : null));

            return fresh.Count;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            try { CheckFailed?.Invoke(this, ex); } catch { /* subscriber error */ }
            return 0;
        }
        finally { _checkGate.Release(); }
    }

    private void Raise(SkyBellEvent e)
    {
        try { EventDetected?.Invoke(this, e); }
        catch { /* a faulty subscriber must not stop the monitor */ }
    }
}
