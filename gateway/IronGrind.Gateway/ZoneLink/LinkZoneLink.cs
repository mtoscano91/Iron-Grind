using System.Collections.Concurrent;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IronGrind.Gateway.ZoneLink;

/// <summary>
/// The real <see cref="IZoneLink"/> (Gateway Story 004, ADR-016 Decision 3): sends Admit / Evict / Reconnect through the
/// link sender with a fresh request id, matches the zone's replies, and fails requests on timeout, cancellation or link
/// loss. Also the link-event handler for replies (Story 009 fans listener events out to it). A pending entry is
/// completed by whoever removes it from the pending table, so exactly one path (reply, timeout, cancel, link loss) wins.
/// Tokens are never logged.
/// </summary>
public sealed class LinkZoneLink : IZoneLink, IZoneLinkEvents
{
    private const int MAX_EXPIRED_ENTRIES = 1024;

    private readonly IZoneLinkSender _sender;
    private readonly IAccountZoneMap _map;
    private readonly GatewayOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LinkZoneLink> _logger;

    private readonly ConcurrentDictionary<uint, Pending> _pending = new();

    private readonly object _expiredLock = new();
    private readonly Dictionary<uint, ExpiredAdmit> _expired = new();
    private readonly Queue<uint> _expiredOrder = new();

    private int _requestCounter;

    /// <summary>Creates the link.</summary>
    /// <param name="sender">Outbound side of the zone link.</param>
    /// <param name="map">Account to zone map; receives the entry when an admission is accepted.</param>
    /// <param name="options">Gateway options (request and evict timeouts).</param>
    /// <param name="time">Clock and timer source (a fake in tests).</param>
    /// <param name="logger">Logger.</param>
    public LinkZoneLink(
        IZoneLinkSender sender,
        IAccountZoneMap map,
        IOptions<GatewayOptions> options,
        TimeProvider time,
        ILogger<LinkZoneLink> logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _sender = sender;
        _map = map;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    // ---------------------------------------------------------------- IZoneLink

    /// <inheritdoc />
    public async Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(token);
        object? result = await RequestAsync(
            RequestKind.Admit,
            zone,
            account,
            id => GatewayLinkCodec.Encode(new AdmitMessage(id, account, character, token)),
            _options.ZoneRequestTimeoutMs,
            ct).ConfigureAwait(false);
        return (bool)result!;
    }

    /// <inheritdoc />
    public async Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct)
    {
        await RequestAsync(
            RequestKind.Evict,
            zone,
            account,
            id => GatewayLinkCodec.Encode(new EvictMessage(id, account)),
            _options.EvictTimeoutMs,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(token);
        object? result = await RequestAsync(
            RequestKind.Reconnect,
            zone,
            account,
            id => GatewayLinkCodec.Encode(new ReconnectMessage(id, account, token)),
            _options.ZoneRequestTimeoutMs,
            ct).ConfigureAwait(false);
        return (ReconnectOutcome)result!;
    }

    // ---------------------------------------------------------------- IZoneLinkEvents

    /// <inheritdoc />
    public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message)
    {
        // Registration is handled by the map (Story 005).
    }

    /// <inheritdoc />
    public void OnLinkLost(ZoneID zone)
    {
        try
        {
            foreach (KeyValuePair<uint, Pending> entry in _pending)
            {
                if (!entry.Value.Zone.Equals(zone))
                {
                    continue;
                }

                if (_pending.TryRemove(entry.Key, out Pending? pending))
                {
                    pending.Tcs.TrySetException(new ZoneLinkDownException(
                        $"{pending.Kind} failed: link to zone {zone.RawValue} was lost while the request was pending."));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failing pending requests for zone {ZoneId} after link loss threw.", zone);
        }
    }

    /// <inheritdoc />
    public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message)
    {
        try
        {
            switch (message.Type)
            {
                case GatewayLinkMessageType.AdmitResult:
                    HandleAdmitResult(zone, message.AdmitResult);
                    break;
                case GatewayLinkMessageType.EvictResult:
                    HandleEvictResult(zone, message.EvictResult);
                    break;
                case GatewayLinkMessageType.ReconnectResult:
                    HandleReconnectResult(zone, message.ReconnectResult);
                    break;
                default:
                    // PlayerCount, SessionReleased, ZoneDraining belong to Stories 005 and 006.
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling {MessageType} from zone {ZoneId} threw.", message.Type, zone);
        }
    }

    // ---------------------------------------------------------------- Reply handling

    private void HandleAdmitResult(ZoneID zone, AdmitResultMessage reply)
    {
        ReplyMatch match = TryTakeLive(reply.RequestId, zone, RequestKind.Admit, out Pending? pending);
        if (match == ReplyMatch.Taken)
        {
            CompleteAdmit(pending!, zone, reply.Accepted);
            return;
        }

        // Missing includes losing the race to a timeout or cancel: Settle records the expired Admit before it removes
        // the pending entry, so a late accept always finds one of the two.
        if (match == ReplyMatch.Missing && reply.Accepted && TryTakeExpired(reply.RequestId, zone, out AccountID lateAccount))
        {
            _logger.LogWarning(
                "Late AdmitResult(accepted) {RequestId} for account {AccountId} from zone {ZoneId}; sending Evict.",
                reply.RequestId,
                lateAccount,
                zone);
            StartLateEvict(zone, lateAccount);
            return;
        }

        LogDropped(match, RequestKind.Admit, reply.RequestId, zone);
    }

    private void CompleteAdmit(Pending pending, ZoneID zone, bool accepted)
    {
        if (!accepted)
        {
            pending.Tcs.TrySetResult(false);
            return;
        }

        try
        {
            // AC-6: the map entry exists before the caller's task completes.
            _map.SetSession(pending.Account, zone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting the session for account {AccountId} in zone {ZoneId} failed.", pending.Account, zone);
            pending.Tcs.TrySetException(ex);
            return;
        }

        pending.Tcs.TrySetResult(true);
    }

    private void HandleEvictResult(ZoneID zone, EvictResultMessage reply)
    {
        ReplyMatch match = TryTakeLive(reply.RequestId, zone, RequestKind.Evict, out Pending? pending);
        if (match == ReplyMatch.Taken)
        {
            pending!.Tcs.TrySetResult(null);
            return;
        }

        LogDropped(match, RequestKind.Evict, reply.RequestId, zone);
    }

    private void HandleReconnectResult(ZoneID zone, ReconnectResultMessage reply)
    {
        ReplyMatch match = TryTakeLive(reply.RequestId, zone, RequestKind.Reconnect, out Pending? pending);
        if (match == ReplyMatch.Taken)
        {
            pending!.Tcs.TrySetResult(new ReconnectOutcome(reply.Accepted, reply.Accepted ? reply.NewToken : null));
            return;
        }

        LogDropped(match, RequestKind.Reconnect, reply.RequestId, zone);
    }

    /// <summary>Removes the live entry for a reply if its kind and zone match; a failed removal counts as missing.</summary>
    private ReplyMatch TryTakeLive(uint requestId, ZoneID zone, RequestKind kind, out Pending? pending)
    {
        pending = null;
        if (!_pending.TryGetValue(requestId, out Pending? live))
        {
            return ReplyMatch.Missing;
        }

        if (live.Kind != kind || !live.Zone.Equals(zone))
        {
            return ReplyMatch.Mismatch;
        }

        return _pending.TryRemove(requestId, out pending) ? ReplyMatch.Taken : ReplyMatch.Missing;
    }

    private void LogDropped(ReplyMatch match, RequestKind kind, uint requestId, ZoneID zone)
    {
        if (match == ReplyMatch.Mismatch)
        {
            _logger.LogWarning("{Kind} reply {RequestId} from zone {ZoneId} does not match the live request; dropped.", kind, requestId, zone);
        }
        else
        {
            _logger.LogInformation("{Kind} reply {RequestId} from zone {ZoneId} has no live request; dropped.", kind, requestId, zone);
        }
    }

    private void StartLateEvict(ZoneID zone, AccountID account)
    {
        Task evict = EvictAsync(zone, account, CancellationToken.None);
        evict.ContinueWith(
            t => _logger.LogWarning(t.Exception?.GetBaseException(), "Evict for late-admitted account {AccountId} in zone {ZoneId} failed.", account, zone),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- Request core

    /// <summary>
    /// Registers the request, arms its timeout at registration (so a slow write counts against it), starts the send
    /// and waits for whichever completion path wins. Every failure, the send's included, reaches the caller through the
    /// pending task, so no task is left with an unobserved exception.
    /// </summary>
    private async Task<object?> RequestAsync(
        RequestKind kind,
        ZoneID zone,
        AccountID account,
        Func<uint, byte[]> encode,
        int timeoutMs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_sender.IsLinkUp(zone))
        {
            throw new ZoneLinkDownException($"{kind} not sent: zone {zone.RawValue} has no live link.");
        }

        uint id = NextRequestId();
        byte[] frame = encode(id);
        Pending pending = new(kind, zone, account);
        _pending[id] = pending;

        using ITimer timer = _time.CreateTimer(
            _ => Settle(id, new TimeoutException($"{kind} to zone {zone.RawValue} timed out after {timeoutMs} ms."), expire: true),
            null,
            TimeSpan.FromMilliseconds(timeoutMs),
            Timeout.InfiniteTimeSpan);
        using CancellationTokenRegistration registration = ct.Register(() => Settle(id, new OperationCanceledException(ct), expire: true));

        // Not awaited: SendFrameAsync never throws, and a timeout must not wait for a slow write.
        _ = SendFrameAsync(id, kind, zone, frame, ct);
        return await pending.Tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Writes the frame unless the request already ended; a failed or thrown write settles the request.</summary>
    private async Task SendFrameAsync(uint id, RequestKind kind, ZoneID zone, byte[] frame, CancellationToken ct)
    {
        if (!_pending.ContainsKey(id))
        {
            return;
        }

        bool sent;
        try
        {
            sent = await _sender.SendAsync(zone, frame, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The frame may have reached the zone, so an Admit is remembered for a late accept.
            Exception failure = ex is OperationCanceledException && ct.IsCancellationRequested ? new OperationCanceledException(ct) : ex;
            Settle(id, failure, expire: true);
            return;
        }

        if (!sent)
        {
            Settle(id, new ZoneLinkDownException($"{kind} not sent: write to zone {zone.RawValue} failed."), expire: false);
        }
    }

    /// <summary>
    /// Fails the entry if no other path has completed it. Only one settle runs per entry. An Admit that is remembered
    /// goes into the expired table BEFORE it leaves the pending table, so a reply racing this call finds it in one of
    /// the two; if the reply wins the removal, the expired record is taken back out.
    /// </summary>
    private void Settle(uint id, Exception failure, bool expire)
    {
        if (!_pending.TryGetValue(id, out Pending? pending) || !pending.TryBeginSettle())
        {
            return;
        }

        bool remember = expire && pending.Kind == RequestKind.Admit;
        if (remember)
        {
            AddExpired(id, pending);
        }

        if (!_pending.TryRemove(id, out _))
        {
            if (remember)
            {
                RemoveExpired(id);
            }

            return;
        }

        pending.Tcs.TrySetException(failure);
    }

    private uint NextRequestId()
    {
        uint id;
        do
        {
            id = unchecked((uint)Interlocked.Increment(ref _requestCounter));
        }
        while (id == 0);

        return id;
    }

    // ---------------------------------------------------------------- Expired admits

    private void AddExpired(uint id, Pending pending)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_expiredLock)
        {
            PruneExpired(now);
            while (_expired.Count >= MAX_EXPIRED_ENTRIES && _expiredOrder.Count > 0)
            {
                _expired.Remove(_expiredOrder.Dequeue());
            }

            _expired[id] = new ExpiredAdmit(pending.Zone, pending.Account, now.AddMilliseconds(_options.EvictTimeoutMs));
            _expiredOrder.Enqueue(id);
        }
    }

    private bool TryTakeExpired(uint id, ZoneID zone, out AccountID account)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_expiredLock)
        {
            PruneExpired(now);
            if (_expired.TryGetValue(id, out ExpiredAdmit entry) && entry.Zone.Equals(zone))
            {
                _expired.Remove(id);
                account = entry.Account;
                return true;
            }
        }

        account = default;
        return false;
    }

    private void RemoveExpired(uint id)
    {
        lock (_expiredLock)
        {
            _expired.Remove(id);
        }
    }

    // Caller holds _expiredLock. Entries are queued in expiry order, so pruning only looks at the head.
    private void PruneExpired(DateTimeOffset now)
    {
        while (_expiredOrder.Count > 0)
        {
            uint head = _expiredOrder.Peek();
            if (!_expired.TryGetValue(head, out ExpiredAdmit entry))
            {
                _expiredOrder.Dequeue();
                continue;
            }

            if (entry.ExpiresAt > now)
            {
                break;
            }

            _expired.Remove(head);
            _expiredOrder.Dequeue();
        }
    }

    // ---------------------------------------------------------------- Types

    private enum RequestKind
    {
        Admit,
        Evict,
        Reconnect,
    }

    private enum ReplyMatch
    {
        Taken,
        Missing,
        Mismatch,
    }

    private sealed class Pending
    {
        private int _settling;

        public Pending(RequestKind kind, ZoneID zone, AccountID account)
        {
            Kind = kind;
            Zone = zone;
            Account = account;
        }

        public RequestKind Kind { get; }

        public ZoneID Zone { get; }

        public AccountID Account { get; }

        public TaskCompletionSource<object?> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>True for the first timeout / cancel / send-failure path only.</summary>
        public bool TryBeginSettle() => Interlocked.Exchange(ref _settling, 1) == 0;
    }

    private readonly record struct ExpiredAdmit(ZoneID Zone, AccountID Account, DateTimeOffset ExpiresAt);
}
