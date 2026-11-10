using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Config;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IronGrind.Gateway.ZoneLink;

/// <summary>
/// Zone-link listener (Gateway Story 003, ADR-016 Decision 3): a <see cref="TcpListener"/> on 127.0.0.1 only. Each
/// connection's first frame must be <c>RegisterZone</c>; it then is that zone's link. A newer <c>RegisterZone</c> for
/// the same zone closes the older link without reporting a loss. Every other end of a registered link (peer close,
/// read/write error, oversized, zero-length or unknown-type frame, malformed body, a second RegisterZone on the same
/// link, gateway stop) reports <see cref="IZoneLinkEvents.OnLinkLost"/> exactly once. Writes are serialized per
/// connection. Start binds the port before it returns, so <see cref="LocalEndpoint"/> is valid afterwards.
/// A connection's read loop is the only place that finalizes it (removes it from the link table, logs the close,
/// raises link lost); senders and stop only record a reason and close the socket, so every event for a link comes
/// from that link's read loop, in order. The listener is single use: it cannot be started again after stop.
/// </summary>
public sealed class ZoneLinkListener : IHostedService, IZoneLinkSender
{
    private static readonly TimeSpan STOP_TIMEOUT = TimeSpan.FromSeconds(2);

    private readonly GatewayOptions _options;
    private readonly IZoneLinkEvents _events;
    private readonly ILogger<ZoneLinkListener> _logger;
    private readonly ConcurrentDictionary<ZoneID, Connection> _links = new();
    private readonly ConcurrentDictionary<Connection, byte> _connections = new();
    private readonly object _registerLock = new();
    private readonly CancellationTokenSource _stoppingSource = new();
    private readonly CancellationToken _stopping;
    private TcpListener? _listener;
    private Task _acceptLoop = Task.CompletedTask;
    private int _started;
    private int _stopped;

    /// <summary>Creates the listener. The port comes from <see cref="GatewayOptions.ZoneLinkPort"/> (0 = any free port; tests only, the validator rejects 0).</summary>
    /// <param name="options">Gateway knobs (zone-link port, max frame bytes).</param>
    /// <param name="events">Receives registrations, messages and link loss.</param>
    /// <param name="logger">Logger.</param>
    public ZoneLinkListener(IOptions<GatewayOptions> options, IZoneLinkEvents events, ILogger<ZoneLinkListener> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stopping = _stoppingSource.Token;
    }

    /// <summary>The bound loopback endpoint after start, otherwise null.</summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopped) != 0 || Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The zone link listener is single use: it was already started or stopped.");
        }

        var listener = new TcpListener(IPAddress.Loopback, _options.ZoneLinkPort);
        listener.Start();
        _listener = listener;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener));
        _logger.LogInformation("Zone link listening on {Endpoint}", listener.LocalEndpoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var deadline = System.Diagnostics.Stopwatch.StartNew();
        _stoppingSource.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }

        // The accept loop ends first so no connection can appear after the snapshot below.
        await WaitBoundedAsync(_acceptLoop, deadline);
        Connection[] connections = _connections.Keys.ToArray();
        foreach (Connection connection in connections)
        {
            Close(connection, "gateway stopping");
        }

        await WaitBoundedAsync(Task.WhenAll(connections.Select(c => c.Finished)), deadline);
        _stoppingSource.Dispose();
    }

    /// <inheritdoc/>
    public bool IsLinkUp(ZoneID zone) => _links.TryGetValue(zone, out Connection? connection) && Volatile.Read(ref connection.Closed) == 0;

    /// <inheritdoc/>
    public async Task<bool> SendAsync(ZoneID zone, byte[] frame, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!_links.TryGetValue(zone, out Connection? connection))
        {
            return false;
        }

        await connection.WriteLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref connection.Closed) != 0)
            {
                return false;
            }

            try
            {
                await connection.Stream.WriteAsync(frame, ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                Close(connection, "send cancelled mid-frame");
                throw;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                Close(connection, "write failed: " + ex.GetType().Name);
                return false;
            }
        }
        finally
        {
            connection.WriteLock.Release();
        }
    }

    private async Task WaitBoundedAsync(Task work, System.Diagnostics.Stopwatch deadline)
    {
        TimeSpan remaining = STOP_TIMEOUT - deadline.Elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        Task finished = await Task.WhenAny(work, Task.Delay(remaining));
        if (finished != work)
        {
            _logger.LogWarning("Zone link loops did not finish within {Seconds} s of stop", STOP_TIMEOUT.TotalSeconds);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stopping);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (!_stopping.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Zone link accept loop ended unexpectedly");
                }

                return;
            }

            if (_stopping.IsCancellationRequested)
            {
                client.Dispose();
                return;
            }

            client.NoDelay = true;
            var connection = new Connection(client);
            _connections[connection] = 0;
            _ = Task.Run(() => ReadLoopAsync(connection));
        }
    }

    private async Task ReadLoopAsync(Connection connection)
    {
        string fallbackReason;
        try
        {
            fallbackReason = await ReadUntilEndAsync(connection);
        }
        catch (Exception ex)
        {
            fallbackReason = "read ended: " + ex.GetType().Name;
        }

        FinalizeLink(connection, fallbackReason);
    }

    /// <summary>Reads and dispatches frames until the link ends; returns the reason if nobody recorded one earlier.</summary>
    private async Task<string> ReadUntilEndAsync(Connection connection)
    {
        int max = _options.LinkMaxFrameBytes;
        var buffer = new byte[GatewayLinkCodec.LENGTH_PREFIX_BYTES + Math.Max(max, 1)];
        int filled = 0;
        while (true)
        {
            int read = await connection.Stream.ReadAsync(buffer.AsMemory(filled), _stopping);
            if (read == 0)
            {
                return "peer closed";
            }

            filled += read;
            if (!ProcessBuffer(connection, buffer, ref filled))
            {
                return "closed by gateway";
            }
        }
    }

    /// <summary>Decodes every complete frame in the buffer. Returns false once the connection was closed.</summary>
    private bool ProcessBuffer(Connection connection, byte[] buffer, ref int filled)
    {
        int offset = 0;
        while (offset < filled)
        {
            GatewayLinkFrameStatus status = GatewayLinkCodec.TryReadFrame(
                buffer.AsSpan(offset, filled - offset),
                _options.LinkMaxFrameBytes,
                out byte type,
                out ReadOnlySpan<byte> body,
                out int consumed);
            if (status == GatewayLinkFrameStatus.NeedMoreBytes)
            {
                break;
            }

            if (status == GatewayLinkFrameStatus.ProtocolViolation)
            {
                Close(connection, "protocol violation: bad frame header");
                return false;
            }

            if (!GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out ZoneToGatewayMessage message))
            {
                Close(connection, $"protocol violation: bad or non zone-to-gateway message type 0x{type:X2}");
                return false;
            }

            offset += consumed;
            if (!Dispatch(connection, message))
            {
                return false;
            }
        }

        if (offset > 0)
        {
            Buffer.BlockCopy(buffer, offset, buffer, 0, filled - offset);
            filled -= offset;
        }

        return true;
    }

    private bool Dispatch(Connection connection, ZoneToGatewayMessage message)
    {
        bool registered = connection.Zone != ZoneID.Invalid;
        if (message.Type == GatewayLinkMessageType.RegisterZone)
        {
            if (registered)
            {
                Close(connection, "protocol violation: second RegisterZone on the same link");
                return false;
            }

            return Register(connection, message.RegisterZone);
        }

        if (!registered)
        {
            Close(connection, "protocol violation: first frame was not RegisterZone");
            return false;
        }

        Invoke(connection, () => _events.OnMessageReceived(connection.Zone, message));
        return true;
    }

    private bool Register(Connection connection, RegisterZoneMessage message)
    {
        ZoneID zone = message.ZoneId;
        if (zone == ZoneID.Invalid)
        {
            Close(connection, "protocol violation: RegisterZone with the invalid zone id");
            return false;
        }

        Connection? older;
        lock (_registerLock)
        {
            if (_stopping.IsCancellationRequested || Volatile.Read(ref connection.Closed) != 0)
            {
                Close(connection, "closed while registering");
                return false;
            }

            connection.Zone = zone;
            _links.TryGetValue(zone, out older);
            _links[zone] = connection;
        }

        if (older != null)
        {
            // The older link is no longer in _links, so its read loop finalizes it without OnLinkLost.
            Close(older, $"replaced by a newer link from {connection.Remote}");
        }

        _logger.LogInformation("Zone {Zone} link registered from {Remote}", zone.RawValue, connection.Remote);
        Invoke(connection, () => _events.OnZoneRegistered(zone, message));
        return true;
    }

    private void Invoke(Connection connection, Action handler)
    {
        try
        {
            handler();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zone link event handler threw for zone {Zone} from {Remote}", connection.Zone.RawValue, connection.Remote);
        }
    }

    /// <summary>
    /// Records the close reason (first wins) and closes the socket. Safe from any thread. Does not touch the link table
    /// or raise events: the connection's read loop sees the closed socket and finalizes.
    /// </summary>
    private static void Close(Connection connection, string reason)
    {
        Interlocked.CompareExchange(ref connection.CloseReason, reason, null);
        if (Interlocked.Exchange(ref connection.Closed, 1) == 0)
        {
            connection.Dispose();
        }
    }

    /// <summary>Read-loop only: removes the link, logs the close and raises OnLinkLost if it was still its zone's current link.</summary>
    private void FinalizeLink(Connection connection, string fallbackReason)
    {
        Close(connection, fallbackReason);
        ZoneID zone = connection.Zone;
        bool wasCurrent = zone != ZoneID.Invalid && _links.TryRemove(new KeyValuePair<ZoneID, Connection>(zone, connection));
        _connections.TryRemove(connection, out _);
        _logger.LogInformation(
            "Zone link closed: zone {Zone} from {Remote}: {Reason}{LostNote}",
            zone.RawValue,
            connection.Remote,
            connection.CloseReason,
            wasCurrent ? string.Empty : " (not current link; no link-lost event)");
        try
        {
            if (wasCurrent)
            {
                Invoke(connection, () => _events.OnLinkLost(zone));
            }
        }
        finally
        {
            connection.MarkFinished();
        }
    }

    private sealed class Connection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private uint _zoneRaw = ZoneID.Invalid.RawValue;

        public Connection(TcpClient client)
        {
            _client = client;
            Stream = client.GetStream();
            Remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        }

        public NetworkStream Stream { get; }

        public string Remote { get; }

        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        /// <summary>Completes after the read loop finalized this connection.</summary>
        public Task Finished => _finished.Task;

        /// <summary>Set once (under the register lock) by the read loop before the connection becomes a link; Invalid until registered.</summary>
        public ZoneID Zone
        {
            get => new(Volatile.Read(ref _zoneRaw));
            set => Volatile.Write(ref _zoneRaw, value.RawValue);
        }

        /// <summary>First recorded close reason, or null.</summary>
        public string? CloseReason;

        public int Closed;

        public void MarkFinished() => _finished.TrySetResult();

        public void Dispose() => _client.Dispose();
    }
}
