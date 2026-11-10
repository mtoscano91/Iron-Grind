using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 003: zone link listener over real loopback sockets with a fake zone client
/// (first frame, registration, replacement, violations, concurrent sends, stop, loopback-only).
/// </summary>
[TestFixture]
internal sealed class Gateway_LinkListener_tests
{
    private const int MAX_FRAME_BYTES = 65536;
    private const uint ZONE = 7;
    private const uint OTHER_ZONE = 8;
    private const int CONCURRENT_SENDS = 50;
    private const byte UNKNOWN_TYPE = 0x55;
    private static readonly TimeSpan WAIT = TimeSpan.FromSeconds(10);

    private RecordingEvents _events = null!;
    private ZoneLinkListener _listener = null!;
    private readonly List<TcpClient> _clients = new();
    private bool _stopped;

    [SetUp]
    public async Task SetUp()
    {
        _events = new RecordingEvents();
        _stopped = false;
        _listener = NewListener(_events, MAX_FRAME_BYTES);
        await _listener.StartAsync(CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (TcpClient client in _clients)
        {
            client.Dispose();
        }

        _clients.Clear();
        if (!_stopped)
        {
            await _listener.StopAsync(CancellationToken.None);
        }
    }

    // ---------------------------------------------------------------- Fixtures

    private static ZoneLinkListener NewListener(IZoneLinkEvents events, int maxFrameBytes)
    {
        var options = new GatewayOptions { ZoneLinkPort = 0, LinkMaxFrameBytes = maxFrameBytes };
        return new ZoneLinkListener(Options.Create(options), events, NullLogger<ZoneLinkListener>.Instance);
    }

    private async Task<TcpClient> ConnectAsync()
    {
        var client = new TcpClient();
        _clients.Add(client);
        await client.ConnectAsync(_listener.LocalEndpoint!);
        return client;
    }

    private static byte[] RegisterFrame(uint zone) =>
        GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(zone), 1, 7777, 50, Array.Empty<AccountID>()));

    private static byte[] PlayerCountFrame(ushort count) => GatewayLinkCodec.Encode(new PlayerCountMessage(count));

    private static byte[] RawFrame(uint length, byte type, int bodyBytes)
    {
        var frame = new byte[GatewayLinkCodec.LENGTH_PREFIX_BYTES + 1 + bodyBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, length);
        frame[GatewayLinkCodec.LENGTH_PREFIX_BYTES] = type;
        return frame;
    }

    private static async Task SendAsync(TcpClient client, byte[] bytes)
    {
        await client.GetStream().WriteAsync(bytes);
    }

    private async Task<TcpClient> RegisterAsync(uint zone)
    {
        TcpClient client = await ConnectAsync();
        await SendAsync(client, RegisterFrame(zone));
        await _events.WaitForAsync($"reg:{zone}");
        return client;
    }

    /// <summary>Waits until the gateway closed the connection (EOF or reset) within the bound.</summary>
    private static async Task AssertClosedAsync(TcpClient client)
    {
        using var cts = new CancellationTokenSource(WAIT);
        var buffer = new byte[64];
        try
        {
            while (await client.GetStream().ReadAsync(buffer, cts.Token) > 0)
            {
            }
        }
        catch (IOException)
        {
            // A reset also means the gateway closed it.
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("The gateway did not close the connection in time.");
        }
    }

    /// <summary>Reads exactly <paramref name="count"/> whole gateway-to-zone frames.</summary>
    private static async Task<List<GatewayToZoneMessage>> ReadFramesAsync(TcpClient client, int count)
    {
        using var cts = new CancellationTokenSource(WAIT);
        var messages = new List<GatewayToZoneMessage>();
        var buffer = new byte[MAX_FRAME_BYTES * 2];
        int filled = 0;
        while (messages.Count < count)
        {
            int read = await client.GetStream().ReadAsync(buffer.AsMemory(filled), cts.Token);
            Assert.That(read, Is.GreaterThan(0), "connection closed before all frames arrived");
            filled += read;
            filled = Drain(buffer, filled, messages);
        }

        Assert.That(filled, Is.EqualTo(0), "no partial frame may be left over");
        return messages;
    }

    private static int Drain(byte[] buffer, int filled, List<GatewayToZoneMessage> messages)
    {
        int offset = 0;
        while (offset < filled)
        {
            GatewayLinkFrameStatus status = GatewayLinkCodec.TryReadFrame(
                buffer.AsSpan(offset, filled - offset), MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out int consumed);
            if (status == GatewayLinkFrameStatus.NeedMoreBytes)
            {
                break;
            }

            Assert.That(status, Is.EqualTo(GatewayLinkFrameStatus.Frame));
            Assert.That(GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out GatewayToZoneMessage message), Is.True);
            messages.Add(message);
            offset += consumed;
        }

        Buffer.BlockCopy(buffer, offset, buffer, 0, filled - offset);
        return filled - offset;
    }

    private async Task StopAsync()
    {
        _stopped = true;
        await _listener.StopAsync(CancellationToken.None);
    }

    // ---------------------------------------------------------------- AC-1 loopback / config

    [Test]
    public void test_listener_bound_to_loopback_only()
    {
        IPEndPoint endpoint = _listener.LocalEndpoint!;

        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
        Assert.That(endpoint.Address, Is.Not.EqualTo(IPAddress.Any));
    }

    // ---------------------------------------------------------------- AC-2 first frame

    [Test]
    public async Task test_first_frame_not_register_zone_closes_without_events()
    {
        TcpClient client = await ConnectAsync();

        await SendAsync(client, PlayerCountFrame(3));

        await AssertClosedAsync(client);
        await StopAsync();
        Assert.That(_events.Snapshot(), Is.Empty);
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.False);
    }

    [Test]
    public async Task test_first_frame_oversized_closes_without_events()
    {
        TcpClient client = await ConnectAsync();

        await SendAsync(client, RawFrame(MAX_FRAME_BYTES + 1, (byte)GatewayLinkMessageType.RegisterZone, 0));

        await AssertClosedAsync(client);
        await StopAsync();
        Assert.That(_events.Snapshot(), Is.Empty);
    }

    [Test]
    public async Task test_register_zone_registers_link_and_raises_event()
    {
        TcpClient client = await ConnectAsync();

        await SendAsync(client, RegisterFrame(ZONE));
        await _events.WaitForAsync("reg:7");

        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.True);
        Assert.That(_listener.IsLinkUp(new ZoneID(OTHER_ZONE)), Is.False);
        Assert.That(_events.Registered[0].UdpPort, Is.EqualTo((ushort)7777));
    }

    [Test]
    public async Task test_messages_after_register_are_delivered_in_order()
    {
        TcpClient client = await RegisterAsync(ZONE);

        await SendAsync(client, PlayerCountFrame(4));
        await SendAsync(client, GatewayLinkCodec.EncodeZoneDraining());
        await _events.WaitForAsync("msg:7:ZoneDraining");

        Assert.That(
            _events.Snapshot(),
            Is.EqualTo(new[] { "reg:7", "msg:7:PlayerCount", "msg:7:ZoneDraining" }));
        Assert.That(_events.PlayerCounts, Is.EqualTo(new[] { (ushort)4 }));
    }

    [Test]
    public async Task test_frames_split_across_writes_and_coalesced_decode()
    {
        TcpClient client = await ConnectAsync();
        byte[] register = RegisterFrame(ZONE);

        await SendAsync(client, register.AsSpan(0, 3).ToArray());
        await SendAsync(client, register.AsSpan(3, 6).ToArray());
        await SendAsync(client, register.AsSpan(9).ToArray());
        await _events.WaitForAsync("reg:7");
        await SendAsync(client, PlayerCountFrame(1).Concat(PlayerCountFrame(2)).ToArray());
        await _events.WaitForAsync(() => _events.PlayerCounts.Count == 2);

        Assert.That(_events.PlayerCounts, Is.EqualTo(new[] { (ushort)1, (ushort)2 }));
    }

    [Test]
    public async Task test_register_zone_at_exact_frame_cap_is_accepted()
    {
        const int maxAccounts = (MAX_FRAME_BYTES - 1 - GatewayLinkCodec.REGISTER_ZONE_FIXED_BODY_BYTES) / GatewayLinkCodec.REGISTER_ZONE_ACCOUNT_BYTES;
        var accounts = new AccountID[maxAccounts];
        for (int i = 0; i < maxAccounts; i++)
        {
            accounts[i] = new AccountID((uint)(i + 1));
        }

        byte[] frame = GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(ZONE), 1, 7777, 50, accounts));
        TcpClient client = await ConnectAsync();

        await SendAsync(client, frame);
        await _events.WaitForAsync("reg:7");

        Assert.That(frame.Length - GatewayLinkCodec.LENGTH_PREFIX_BYTES, Is.EqualTo(MAX_FRAME_BYTES));
        Assert.That(_events.Registered[0].LiveAccounts, Has.Length.EqualTo(maxAccounts));
    }

    // ---------------------------------------------------------------- AC-3 replacement

    [Test]
    public async Task test_second_register_replaces_older_link_without_link_lost()
    {
        TcpClient a = await RegisterAsync(ZONE);
        TcpClient b = await ConnectAsync();

        await SendAsync(b, RegisterFrame(ZONE));
        await _events.WaitForAsync(() => _events.Snapshot().Count(e => e == "reg:7") == 2);
        await AssertClosedAsync(a);

        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.True);
        bool sent = await _listener.SendAsync(
            new ZoneID(ZONE), GatewayLinkCodec.Encode(new EvictMessage(9, new AccountID(1))), CancellationToken.None);
        Assert.That(sent, Is.True);
        List<GatewayToZoneMessage> received = await ReadFramesAsync(b, 1);
        Assert.That(received[0].Evict.RequestId, Is.EqualTo(9u));

        b.Dispose();
        await _events.WaitForAsync("lost:7");
        await StopAsync();
        Assert.That(_events.Snapshot().Count(e => e == "lost:7"), Is.EqualTo(1), "only the newer link's loss is reported");
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.False);
    }

    // ---------------------------------------------------------------- AC-4 violations

    private async Task AssertViolationAsync(byte[] violatingBytes)
    {
        TcpClient client = await RegisterAsync(ZONE);

        await SendAsync(client, violatingBytes);

        await AssertClosedAsync(client);
        await _events.WaitForAsync("lost:7");
        await StopAsync();
        Assert.That(_events.Snapshot().Count(e => e == "lost:7"), Is.EqualTo(1));
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.False);
    }

    [Test]
    public Task test_oversized_frame_closes_link_with_one_link_lost() =>
        AssertViolationAsync(RawFrame(MAX_FRAME_BYTES + 1, (byte)GatewayLinkMessageType.PlayerCount, 0));

    [Test]
    public Task test_zero_length_frame_closes_link_with_one_link_lost() =>
        AssertViolationAsync(new byte[GatewayLinkCodec.LENGTH_PREFIX_BYTES]);

    [Test]
    public Task test_unknown_type_closes_link_with_one_link_lost() =>
        AssertViolationAsync(RawFrame(1 + 2, UNKNOWN_TYPE, 2));

    [Test]
    public Task test_short_body_closes_link_with_one_link_lost() =>
        AssertViolationAsync(RawFrame(1 + 1, (byte)GatewayLinkMessageType.PlayerCount, 1));

    [Test]
    public Task test_wrong_direction_type_closes_link_with_one_link_lost() =>
        AssertViolationAsync(RawFrame(1 + GatewayLinkCodec.EVICT_BODY_BYTES, (byte)GatewayLinkMessageType.Evict, GatewayLinkCodec.EVICT_BODY_BYTES));

    [Test]
    public Task test_second_register_zone_on_same_link_closes_link_with_one_link_lost() =>
        AssertViolationAsync(RegisterFrame(ZONE));

    [Test]
    public async Task test_peer_close_reports_one_link_lost()
    {
        TcpClient client = await RegisterAsync(ZONE);

        client.Dispose();
        await _events.WaitForAsync("lost:7");
        await StopAsync();

        Assert.That(_events.Snapshot().Count(e => e == "lost:7"), Is.EqualTo(1));
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.False);
    }

    // ---------------------------------------------------------------- AC-5 outbound

    [Test]
    public async Task test_concurrent_sends_write_whole_frames()
    {
        TcpClient client = await RegisterAsync(ZONE);
        var token = new byte[GatewayLinkCodec.TOKEN_LENGTH];

        Task<bool>[] sends = Enumerable.Range(0, CONCURRENT_SENDS)
            .Select(i => Task.Run(() => _listener.SendAsync(
                new ZoneID(ZONE),
                GatewayLinkCodec.Encode(new AdmitMessage((uint)i, new AccountID((uint)i + 1), new CharacterID(5), token)),
                CancellationToken.None)))
            .ToArray();
        bool[] results = await Task.WhenAll(sends);
        List<GatewayToZoneMessage> received = await ReadFramesAsync(client, CONCURRENT_SENDS);

        Assert.That(results, Has.All.True);
        Assert.That(
            received.Select(m => m.Admit.RequestId).OrderBy(id => id),
            Is.EqualTo(Enumerable.Range(0, CONCURRENT_SENDS).Select(i => (uint)i)));
        Assert.That(received.Select(m => m.Type), Has.All.EqualTo(GatewayLinkMessageType.Admit));
    }

    [Test]
    public async Task test_send_to_unlinked_zone_returns_false_without_throwing()
    {
        await RegisterAsync(ZONE);

        bool sent = await _listener.SendAsync(
            new ZoneID(OTHER_ZONE), GatewayLinkCodec.Encode(new EvictMessage(1, new AccountID(1))), CancellationToken.None);

        Assert.That(sent, Is.False);
    }

    [Test]
    public async Task test_send_after_link_lost_returns_false()
    {
        TcpClient client = await RegisterAsync(ZONE);
        client.Dispose();
        await _events.WaitForAsync("lost:7");

        bool sent = await _listener.SendAsync(
            new ZoneID(ZONE), GatewayLinkCodec.Encode(new EvictMessage(1, new AccountID(1))), CancellationToken.None);

        Assert.That(sent, Is.False);
    }

    // ---------------------------------------------------------------- AC-7 stop

    [Test]
    public async Task test_stop_closes_links_and_listener_within_two_seconds()
    {
        TcpClient registered = await RegisterAsync(ZONE);
        TcpClient unregistered = await ConnectAsync();
        IPEndPoint endpoint = _listener.LocalEndpoint!;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await StopAsync();
        watch.Stop();

        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        await AssertClosedAsync(registered);
        await AssertClosedAsync(unregistered);
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.False);
        using var late = new TcpClient();
        Assert.That(async () => await late.ConnectAsync(endpoint), Throws.InstanceOf<SocketException>());
    }

    // ---------------------------------------------------------------- Review additions

    [Test]
    public async Task test_register_zone_with_invalid_zone_id_closes_without_events()
    {
        TcpClient client = await ConnectAsync();

        await SendAsync(client, RegisterFrame(ZoneID.Invalid.RawValue));

        await AssertClosedAsync(client);
        await StopAsync();
        Assert.That(_events.Snapshot(), Is.Empty);
    }

    [Test]
    public async Task test_throwing_handlers_keep_link_up_and_later_messages_delivered()
    {
        _events.ThrowOnRegistered = true;
        _events.ThrowOnMessage = true;
        TcpClient client = await RegisterAsync(ZONE);

        await SendAsync(client, PlayerCountFrame(1));
        await SendAsync(client, PlayerCountFrame(2));
        await _events.WaitForAsync(() => _events.PlayerCounts.Count == 2);

        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.True);
        Assert.That(_events.PlayerCounts, Is.EqualTo(new[] { (ushort)1, (ushort)2 }));
        Assert.That(_events.Snapshot(), Does.Not.Contain("lost:7"));
    }

    [Test]
    public async Task test_send_cancelled_while_waiting_for_write_turn_throws_and_keeps_link_up()
    {
        TcpClient client = await RegisterAsync(ZONE);
        byte[] big = BigRegisterFrame(100, 16000);

        // The client does not read, so once the socket buffers are full a send stays in flight and holds the write turn.
        Task<bool>? blocked = null;
        for (int i = 0; i < 4000 && blocked == null; i++)
        {
            Task<bool> send = _listener.SendAsync(new ZoneID(ZONE), big, CancellationToken.None);
            if (await Task.WhenAny(send, Task.Delay(250)) != send)
            {
                blocked = send;
            }
        }

        Assert.That(blocked, Is.Not.Null, "the socket buffers never filled");
        using var cts = new CancellationTokenSource();
        Task<bool> waiting = _listener.SendAsync(new ZoneID(ZONE), big, cts.Token);
        Assert.That(waiting.IsCompleted, Is.False);
        cts.Cancel();
        Assert.That(async () => await waiting, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.True);

        using var drain = new CancellationTokenSource(WAIT);
        var sink = new byte[MAX_FRAME_BYTES];
        Task<int> pending = client.GetStream().ReadAsync(sink, drain.Token).AsTask();
        while (!blocked!.IsCompleted)
        {
            // Wait on the send or the next read, never on a read alone: the send can finish after the last byte was read.
            if (await Task.WhenAny(blocked, pending) == pending)
            {
                Assert.That(await pending, Is.GreaterThan(0));
                pending = client.GetStream().ReadAsync(sink, drain.Token).AsTask();
            }
        }

        drain.Cancel();
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
        }


        Assert.That(await blocked, Is.True);
        Assert.That(_events.Snapshot(), Does.Not.Contain("lost:7"));
    }

    [Test]
    public async Task test_write_failure_returns_false_and_reports_one_link_lost_after_registration()
    {
        TcpClient client = await RegisterAsync(ZONE);
        byte[] frame = GatewayLinkCodec.Encode(new EvictMessage(1, new AccountID(1)));

        client.LingerState = new LingerOption(true, 0);
        client.Dispose();
        using var cts = new CancellationTokenSource(WAIT);
        bool sent = true;
        while (sent)
        {
            sent = await _listener.SendAsync(new ZoneID(ZONE), frame, CancellationToken.None);
            cts.Token.ThrowIfCancellationRequested();
        }

        await _events.WaitForAsync("lost:7");
        await StopAsync();

        Assert.That(_events.Snapshot(), Is.EqualTo(new[] { "reg:7", "lost:7" }));
        Assert.That(_listener.IsLinkUp(new ZoneID(ZONE)), Is.False);
    }

    [Test]
    public async Task test_concurrent_sends_of_multi_kilobyte_frames_do_not_interleave()
    {
        const int accountsPerFrame = 2000;
        TcpClient client = await RegisterAsync(ZONE);
        Task<List<ZoneToGatewayMessage>> reader = ReadZoneToGatewayFramesAsync(client, CONCURRENT_SENDS);

        Task<bool>[] sends = Enumerable.Range(0, CONCURRENT_SENDS)
            .Select(i => Task.Run(() => _listener.SendAsync(new ZoneID(ZONE), BigRegisterFrame((uint)(100 + i), accountsPerFrame), CancellationToken.None)))
            .ToArray();
        bool[] results = await Task.WhenAll(sends);
        List<ZoneToGatewayMessage> received = await reader;

        Assert.That(results, Has.All.True);
        Assert.That(received.Select(m => m.RegisterZone.ZoneId.RawValue).OrderBy(id => id), Is.EqualTo(Enumerable.Range(100, CONCURRENT_SENDS).Select(i => (uint)i)));
        Assert.That(received.Select(m => m.RegisterZone.LiveAccounts.Length), Has.All.EqualTo(accountsPerFrame));
    }

    private static byte[] BigRegisterFrame(uint zone, int accounts)
    {
        var list = new AccountID[accounts];
        for (int i = 0; i < accounts; i++)
        {
            list[i] = new AccountID((uint)(i + 1));
        }

        return GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(zone), 1, 7777, 50, list));
    }

    private static async Task<List<ZoneToGatewayMessage>> ReadZoneToGatewayFramesAsync(TcpClient client, int count)
    {
        using var cts = new CancellationTokenSource(WAIT);
        var messages = new List<ZoneToGatewayMessage>();
        var buffer = new byte[MAX_FRAME_BYTES * 2];
        int filled = 0;
        while (messages.Count < count)
        {
            int read = await client.GetStream().ReadAsync(buffer.AsMemory(filled), cts.Token);
            Assert.That(read, Is.GreaterThan(0), "connection closed before all frames arrived");
            filled += read;
            int offset = 0;
            while (offset < filled)
            {
                GatewayLinkFrameStatus status = GatewayLinkCodec.TryReadFrame(
                    buffer.AsSpan(offset, filled - offset), MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out int consumed);
                if (status == GatewayLinkFrameStatus.NeedMoreBytes)
                {
                    break;
                }

                Assert.That(status, Is.EqualTo(GatewayLinkFrameStatus.Frame));
                Assert.That(GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out ZoneToGatewayMessage message), Is.True);
                messages.Add(message);
                offset += consumed;
            }

            Buffer.BlockCopy(buffer, offset, buffer, 0, filled - offset);
            filled -= offset;
        }

        Assert.That(filled, Is.EqualTo(0), "no partial frame may be left over");
        return messages;
    }

    // ---------------------------------------------------------------- Fake

    private sealed class RecordingEvents : IZoneLinkEvents
    {
        private readonly object _lock = new();
        private readonly List<string> _log = new();
        private readonly List<RegisterZoneMessage> _registered = new();
        private readonly List<ushort> _playerCounts = new();
        private readonly SemaphoreSlim _signal = new(0);

        public volatile bool ThrowOnRegistered;
        public volatile bool ThrowOnMessage;

        public IReadOnlyList<RegisterZoneMessage> Registered
        {
            get
            {
                lock (_lock)
                {
                    return _registered.ToList();
                }
            }
        }

        public IReadOnlyList<ushort> PlayerCounts
        {
            get
            {
                lock (_lock)
                {
                    return _playerCounts.ToList();
                }
            }
        }

        public List<string> Snapshot()
        {
            lock (_lock)
            {
                return _log.ToList();
            }
        }

        public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message)
        {
            Add($"reg:{zone.RawValue}", () => _registered.Add(message));
            if (ThrowOnRegistered)
            {
                throw new InvalidOperationException("test handler failure");
            }
        }

        public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message)
        {
            Add($"msg:{zone.RawValue}:{message.Type}", () =>
            {
                if (message.Type == GatewayLinkMessageType.PlayerCount)
                {
                    _playerCounts.Add(message.PlayerCount.Count);
                }
            });
        }

        public void OnLinkLost(ZoneID zone)
        {
            Add($"lost:{zone.RawValue}", () => { });
        }

        public Task WaitForAsync(string entry) => WaitForAsync(() => Snapshot().Contains(entry), entry);

        public async Task WaitForAsync(Func<bool> condition, string what = "condition")
        {
            using var cts = new CancellationTokenSource(WAIT);
            while (!condition())
            {
                try
                {
                    await _signal.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Assert.Fail($"Timed out waiting for {what}. Events: {string.Join(", ", Snapshot())}");
                }
            }
        }

        private void Add(string entry, Action extra)
        {
            lock (_lock)
            {
                _log.Add(entry);
                extra();
            }

            _signal.Release();
        }
    }
}
