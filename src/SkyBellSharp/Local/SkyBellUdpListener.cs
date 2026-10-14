using System.Net;
using System.Net.Sockets;

namespace SkyBellSharp.Local;

/// <summary>Kind of local UDP broadcast.</summary>
public enum SkyBellUdpEventKind
{
    /// <summary>The documented "button pressed" packet.</summary>
    ButtonPressed,
    /// <summary>The documented "motion detected" packet.</summary>
    MotionDetected,
    /// <summary>
    /// A SkyBell packet (length header + encrypted "Salted__" payload) whose content differs from
    /// the two documented samples. Devices may send such packets for real events: confirm the
    /// type through the API (see <see cref="SkyBellEventMonitor"/>).
    /// </summary>
    Unidentified,
    /// <summary>Not a SkyBell packet (only reported when <see cref="SkyBellUdpListener.ReportUnknownPackets"/> is true).</summary>
    Unknown
}

public sealed class SkyBellUdpEventArgs : EventArgs
{
    public SkyBellUdpEventArgs(SkyBellUdpEventKind kind, IPEndPoint source, byte[] payload)
    {
        Kind = kind;
        Source = source;
        Payload = payload;
        ReceivedAt = DateTimeOffset.Now;
    }

    public SkyBellUdpEventKind Kind { get; }

    /// <summary>IP of the SkyBell device that broadcast the packet.</summary>
    public IPEndPoint Source { get; }

    public byte[] Payload { get; }
    public DateTimeOffset ReceivedAt { get; }
}

/// <summary>
/// Listens to the UDP broadcasts SkyBell devices send on the local network (port 5000) when the
/// button is pressed or motion is detected. Near-instant and cloud-free, but only works on the
/// same LAN; the packets carry no device id (use <see cref="SkyBellUdpEventArgs.Source"/>).
/// On Windows the firewall must allow inbound UDP for the host process.
/// </summary>
public sealed class SkyBellUdpListener : IDisposable
{
    public const int DefaultPort = 5000;

    /// <summary>"Button pressed" packet, as published in the SkyBell API docs (49 bytes).</summary>
    public static ReadOnlyMemory<byte> ButtonPacket { get; } = Packet("U2FsdGVkX19I2qbfYVjEx55dE0IwT1MGJQO7+10uYsQ=");

    /// <summary>"Motion detected" packet, as published in the SkyBell API docs (49 bytes).</summary>
    public static ReadOnlyMemory<byte> MotionPacket { get; } = Packet("U2FsdGVkX19D5MkIkoMdNaaRL9ndCFYnMVKpi/ylBq8=");

    private readonly int _port;
    private readonly TimeSpan _debounce;
    private readonly Dictionary<(SkyBellUdpEventKind, string), DateTimeOffset> _last = new();
    private readonly object _sync = new();

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <param name="port">UDP port (5000).</param>
    /// <param name="debounce">Ignore identical events from the same IP within this window (devices repeat broadcasts). Default 3 s.</param>
    public SkyBellUdpListener(int port = DefaultPort, TimeSpan? debounce = null)
    {
        _port = port;
        _debounce = debounce ?? TimeSpan.FromSeconds(3);
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>Also report packets that do not look like SkyBell packets at all.</summary>
    public bool ReportUnknownPackets { get; set; }

    /// <summary>Raised on a thread-pool thread.</summary>
    public event EventHandler<SkyBellUdpEventArgs>? EventReceived;

    /// <summary>Receive loop stopped because of an error.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Starts listening. Throws <see cref="SocketException"/> if the port cannot be bound.</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning) return;

            var udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false, EnableBroadcast = true };
            try
            {
                // Share the port with other listeners (Home Assistant, scripts…).
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
            }
            catch
            {
                udp.Dispose();
                throw;
            }

            _udp = udp;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => ReceiveLoopAsync(udp, token));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            _udp?.Dispose();
            _cts?.Dispose();
            _udp = null;
            _cts = null;
            _loop = null;
        }
    }

    public void Dispose() => Stop();

    /// <summary>Identifies a SkyBell broadcast payload.</summary>
    public static SkyBellUdpEventKind Classify(ReadOnlySpan<byte> packet)
    {
        var p = TrimNewline(packet);
        if (p.SequenceEqual(TrimNewline(ButtonPacket.Span))) return SkyBellUdpEventKind.ButtonPressed;
        if (p.SequenceEqual(TrimNewline(MotionPacket.Span))) return SkyBellUdpEventKind.MotionDetected;
        return LooksLikeSkyBell(packet) ? SkyBellUdpEventKind.Unidentified : SkyBellUdpEventKind.Unknown;
    }

    /// <summary>
    /// SkyBell packet layout: 32-bit little-endian length of the rest, then a base64 OpenSSL
    /// payload starting with "Salted__" ("U2FsdGVk"), then '\n'.
    /// </summary>
    public static bool LooksLikeSkyBell(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 12) return false;
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(packet);
        return length == packet.Length - 4 && packet[4..].StartsWith("U2FsdGVk"u8);
    }

    /// <summary>
    /// Broadcasts a SkyBell packet on the LAN: SkyBell chimes play their tone, and listeners
    /// (including this one) receive it — handy for testing.
    /// </summary>
    /// <param name="kind"><see cref="SkyBellUdpEventKind.ButtonPressed"/> or <see cref="SkyBellUdpEventKind.MotionDetected"/>.</param>
    /// <param name="port">Destination UDP port (5000).</param>
    /// <param name="localAddress">Local interface to send from (null = let the OS choose).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task BroadcastAsync(SkyBellUdpEventKind kind, int port = DefaultPort,
        IPAddress? localAddress = null, CancellationToken cancellationToken = default)
    {
        var packet = kind switch
        {
            SkyBellUdpEventKind.ButtonPressed => ButtonPacket,
            SkyBellUdpEventKind.MotionDetected => MotionPacket,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var client = new UdpClient(new IPEndPoint(localAddress ?? IPAddress.Any, 0)) { EnableBroadcast = true };
        await client.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, port), cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try
            {
                r = await udp.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue; // ICMP port unreachable reported by Windows: harmless
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) Faulted?.Invoke(this, ex);
                break;
            }

            var kind = Classify(r.Buffer);
            if (kind == SkyBellUdpEventKind.Unknown && !ReportUnknownPackets) continue;
            if (IsDuplicate(kind, r.RemoteEndPoint)) continue;

            try { EventReceived?.Invoke(this, new SkyBellUdpEventArgs(kind, r.RemoteEndPoint, r.Buffer)); }
            catch { /* a faulty subscriber must not stop the loop */ }
        }
    }

    private bool IsDuplicate(SkyBellUdpEventKind kind, IPEndPoint source)
    {
        var key = (kind, source.Address.ToString());
        var now = DateTimeOffset.UtcNow;
        lock (_last)
        {
            if (_last.TryGetValue(key, out var previous) && now - previous < _debounce) return true;
            _last[key] = now;
            return false;
        }
    }

    private static ReadOnlySpan<byte> TrimNewline(ReadOnlySpan<byte> s)
    {
        while (s.Length > 0 && (s[^1] == (byte)'\n' || s[^1] == (byte)'\r' || s[^1] == 0)) s = s[..^1];
        return s;
    }

    /// <summary>Header 2D 00 00 00 + ASCII payload + '\n'.</summary>
    private static byte[] Packet(string payload)
    {
        var ascii = System.Text.Encoding.ASCII.GetBytes(payload + "\n");
        var bytes = new byte[4 + ascii.Length];
        bytes[0] = 0x2D;
        ascii.CopyTo(bytes, 4);
        return bytes;
    }
}
