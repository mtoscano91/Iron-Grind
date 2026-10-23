using System;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Handles <see cref="ZoneSnapshotRequest"/>, the client's retransmit request for a zone state snapshot
    /// (<c>zone-instancing.md</c> CR-ZI-9, EC-ZI-6, EC-ZI-7, AC-ZI-7). Zone Instancing Story 008.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Record in the callback, serve on the tick.</b> <see cref="OnConnectionMessage"/> runs inside the receive
    /// callback (ADR-014 Decision 2 step 5): it decodes the body and records the request, nothing else. It never
    /// enqueues to the outbox, never calls the <see cref="ZoneSnapshotBuilder"/> and never logs. <see cref="ServePending"/> runs
    /// on the tick (ADR-014 Decision 6), last in step 3 (Simulation), so a snapshot reflects tick T after every
    /// system ran; the fragments and any <see cref="ZoneFullResponse"/> flush in step 4 of the same tick.
    /// Wiring into <c>ZoneTickPipeline</c> is Story 014.
    /// </para>
    /// <para>
    /// <b>Order of checks</b> (at serve time): decode (in the callback) → zone mismatch or <see cref="ZoneState.Closed"/>
    /// → identity → rate limit → serve. A mismatch or Closed zone answers
    /// <see cref="ZoneFullResponse"/>(<see cref="JoinRejectedReason.ZoneDoesNotExist"/>, retry 0) carrying the
    /// <c>zoneId</c> the client sent (so the client can match it to its request); no snapshot is sent. Draining is
    /// honoured. In Loading no player slot exists, so every request ends in the identity drop.
    /// </para>
    /// <para>
    /// <b>Identity.</b> A request is attributed only if <c>senderEntityId</c> holds a player slot and that slot's
    /// <c>ClientId</c> equals the connection the message arrived on. It is checked in the callback (to pick the
    /// per-slot entry) and again at serve time (the slot may have been released since). A request that fails the
    /// callback check is kept in a small preallocated foreign list, one entry per connection (a later request from
    /// the same connection overwrites the sender and zone, last wins), so that the zone-mismatch / Closed reply and
    /// the identity drop still happen at serve time in the order above; when that list is full the overflow is
    /// counted and reported inside the identity warning. A request recorded as foreign is not re-checked against the
    /// pool at serve time: if its slot is allocated later in the same tick it is still dropped and the client retries.
    /// </para>
    /// <para>
    /// <b>Rate limit and coalescing.</b> A slot is served when it has never been served or when
    /// <c>currentTick - lastServedTick >= window</c> (modular uint arithmetic, so a tick regression fails open and
    /// serves). A dropped request does not move <c>lastServedTick</c>. The first drop in a window logs one
    /// <c>SnapshotRequestRateLimited</c> warning; later drops are counted, and when the slot is next served and more
    /// than one drop was counted one info log reports the count of the window that ended.
    /// Requests recorded for one slot before a <see cref="ServePending"/> collapse into one (not drops); the last
    /// recorded zoneId wins, so a valid request followed by a wrong-zone one in the same tick gets only the reply
    /// (harmless, the sender is authenticated). Per-slot state is tagged with the occupant's <see cref="EntityID"/>
    /// (never reused), so a new occupant of a reused slot starts fresh; a reconnect that keeps the slot and EntityID
    /// (Story 011) inherits the window. <c>snapshotVersion</c> in the request is ignored (EC-ZI-7): the snapshot
    /// always carries the serve tick.
    /// </para>
    /// <para>
    /// <b>Logging bounds per <see cref="ServePending"/> call</b>, in emit order: at most one
    /// <c>SnapshotRequestMalformed</c> warning (all malformed bodies since the last call, with count and last
    /// client); then, during the slot walk, at most one rate-limit warning per slot per window and at most one
    /// rate-limit summary (info) per slot per serve; last, at most one <c>SnapshotRequestIdentityMismatch</c>
    /// warning aggregating every identity drop of the call (foreign drops, foreign-list overflow and serve-time
    /// re-check failures) with the drop count, the overflow count and the first dropped client and entity.
    /// </para>
    /// <para>
    /// Main thread only. All storage is allocated in the constructor; recording and serving allocate nothing unless
    /// they log.
    /// </para>
    /// </remarks>
    public sealed class ZoneSnapshotRequestService : IConnectionMessageSink
    {
        /// <summary>Default rate-limit window: <c>FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS</c> 10 x <c>TICK_RATE_HZ</c> 20 ticks.</summary>
        public const uint SNAPSHOT_REQUEST_RATE_LIMIT_TICKS = 200;

        /// <summary>Default capacity of the list holding requests whose sender has no matching player slot.</summary>
        public const int FOREIGN_REQUEST_CAPACITY = 16;

        private const string LOG_PREFIX = "[ZoneSnapshotRequestService]";

        /// <summary>The connection-level descriptor for <see cref="ZoneSnapshotRequest"/>: 8-byte body, carries the sender EntityID.</summary>
        public static readonly ConnectionMessageDescriptor Descriptor = new ConnectionMessageDescriptor(
            ZoneSnapshotRequest.MessageTypeId, maxBodyBytes: ZoneSnapshotRequest.BodySize, carriesSenderEntityId: true);

        private struct SlotEntry
        {
            public EntityID Occupant;
            public uint ClientId;
            public ZoneID ZoneId;
            public bool Pending;
            public bool HasServed;
            public uint LastServedTick;
            public bool RateLimitLogged;
            public uint DroppedInWindow;
        }

        private readonly ZoneID _zone;
        private readonly ZoneSlotPool _pool;
        private readonly ZoneLifecycle _lifecycle;
        private readonly ZoneSnapshotBuilder _builder;
        private readonly IClientMessageOutbox _outbox;
        private readonly uint _window;
        private readonly SlotEntry[] _slots;
        private readonly uint[] _foreignClient;
        private readonly EntityID[] _foreignSender;
        private readonly ZoneID[] _foreignZone;
        private readonly byte[] _replyBuffer;
        private int _foreignCount;
        private int _foreignOverflow;
        private int _malformedCount;
        private uint _malformedLastClient;
        private int _identityDrops;
        private uint _identityFirstClient;
        private EntityID _identityFirstEntity;

        /// <summary>Creates the service for one zone.</summary>
        /// <param name="zone">The zone this process hosts; must not be <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="pool">The slot pool used for the identity check; its <see cref="ZoneSlotPool.MaxPlayers"/> sizes the per-slot storage.</param>
        /// <param name="lifecycle">Read for <see cref="ZoneLifecycle.State"/> (Closed check).</param>
        /// <param name="builder">Sends the snapshot to a served connection.</param>
        /// <param name="outbox">Receives the <see cref="ZoneFullResponse"/> replies.</param>
        /// <param name="rateLimitWindowTicks">Minimum ticks between two served snapshots of one slot; must be positive.</param>
        /// <param name="foreignCapacity">Capacity of the list of requests without a matching slot; must be at least 1.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is invalid, the window is 0 or the capacity is below 1.</exception>
        public ZoneSnapshotRequestService(ZoneID zone, ZoneSlotPool pool, ZoneLifecycle lifecycle,
            ZoneSnapshotBuilder builder, IClientMessageOutbox outbox,
            uint rateLimitWindowTicks = SNAPSHOT_REQUEST_RATE_LIMIT_TICKS, int foreignCapacity = FOREIGN_REQUEST_CAPACITY)
        {
            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (lifecycle == null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            if (rateLimitWindowTicks == 0u)
            {
                throw new ArgumentException($"{LOG_PREFIX} rateLimitWindowTicks must be positive.", nameof(rateLimitWindowTicks));
            }

            if (foreignCapacity < 1)
            {
                throw new ArgumentException($"{LOG_PREFIX} foreignCapacity must be at least 1 (was {foreignCapacity}).", nameof(foreignCapacity));
            }

            _zone = zone;
            _pool = pool;
            _lifecycle = lifecycle;
            _builder = builder;
            _outbox = outbox;
            _window = rateLimitWindowTicks;
            _slots = new SlotEntry[pool.MaxPlayers];
            _foreignClient = new uint[foreignCapacity];
            _foreignSender = new EntityID[foreignCapacity];
            _foreignZone = new ZoneID[foreignCapacity];
            _replyBuffer = new byte[ZoneFullResponse.BodySize];
        }

        /// <summary>
        /// Registers <see cref="Descriptor"/> with <paramref name="service"/> as its sink. Does not seal the dispatcher.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="service">The sink that records the requests.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Register(IInboundRequestDispatcher dispatcher, ZoneSnapshotRequestService service)
        {
            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }

            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            dispatcher.RegisterConnectionLevel(Descriptor, service);
        }

        /// <summary>
        /// Records one request (receive callback; records only, never logs). A body that does not decode is counted
        /// and reported by one warning at the next <see cref="ServePending"/>. The <c>snapshotVersion</c> field is
        /// ignored (EC-ZI-7).
        /// </summary>
        /// <param name="clientId">The connection the message arrived on.</param>
        /// <param name="messageTypeId">Always <see cref="ZoneSnapshotRequest.MessageTypeId"/>; not inspected.</param>
        /// <param name="senderEntityId">The sender EntityID from the envelope.</param>
        /// <param name="body">The 8-byte body; not valid after return.</param>
        public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body)
        {
            if (!ZoneSnapshotRequestCodec.TryReadBody(body, out ZoneSnapshotRequest request))
            {
                if (_malformedCount < int.MaxValue)
                {
                    _malformedCount++;
                }

                _malformedLastClient = clientId;
                return;
            }

            if (_pool.TryGetPlayerSlot(senderEntityId, out byte slot)
                && slot < _slots.Length
                && _pool.TryGetPlayer(senderEntityId, out ZonePlayerSlot player)
                && player.ClientId == clientId)
            {
                ref SlotEntry entry = ref _slots[slot];
                if (entry.Occupant != senderEntityId)
                {
                    entry = default;
                    entry.Occupant = senderEntityId;
                }

                entry.Pending = true;
                entry.ClientId = clientId;
                entry.ZoneId = request.ZoneId;
                return;
            }

            for (int i = 0; i < _foreignCount; i++)
            {
                if (_foreignClient[i] == clientId)
                {
                    _foreignSender[i] = senderEntityId;
                    _foreignZone[i] = request.ZoneId;
                    return;
                }
            }

            if (_foreignCount < _foreignClient.Length)
            {
                _foreignClient[_foreignCount] = clientId;
                _foreignSender[_foreignCount] = senderEntityId;
                _foreignZone[_foreignCount] = request.ZoneId;
                _foreignCount++;
            }
            else if (_foreignOverflow < int.MaxValue)
            {
                _foreignOverflow++;
            }
        }

        /// <summary>
        /// Serves every recorded request on tick <paramref name="currentTick"/> and clears the records. Call it last
        /// in step 3 of the tick (Story 014 wires this). See the class remarks for the order of checks and the
        /// logging bounds.
        /// </summary>
        /// <param name="currentTick">The tick being run; the snapshot version of every snapshot sent.</param>
        public void ServePending(uint currentTick)
        {
            if (_malformedCount > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} SnapshotRequestMalformed: {_malformedCount} request body(ies) shorter than {ZoneSnapshotRequest.BodySize} bytes dropped since the last tick; last from client {_malformedLastClient}.");
                _malformedCount = 0;
            }

            ServeForeign();
            ServeSlots(currentTick);
            ReportIdentityDrops();
        }

        private void ServeForeign()
        {
            for (int i = 0; i < _foreignCount; i++)
            {
                if (!TrySendZoneDoesNotExist(_foreignClient[i], _foreignZone[i]))
                {
                    NoteIdentityDrop(_foreignClient[i], _foreignSender[i]);
                }
            }

            _foreignCount = 0;
        }

        private void ServeSlots(uint currentTick)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref SlotEntry entry = ref _slots[i];
                if (!entry.Pending)
                {
                    continue;
                }

                entry.Pending = false;

                if (TrySendZoneDoesNotExist(entry.ClientId, entry.ZoneId))
                {
                    continue;
                }

                if (!_pool.TryGetPlayer(entry.Occupant, out ZonePlayerSlot player) || player.ClientId != entry.ClientId)
                {
                    NoteIdentityDrop(entry.ClientId, entry.Occupant);
                    continue;
                }

                // Modular uint subtraction: a tick regression yields a huge difference, so it fails open (serves).
                if (!entry.HasServed || unchecked(currentTick - entry.LastServedTick) >= _window)
                {
                    if (entry.DroppedInWindow > 1u)
                    {
                        Debug.Log($"{LOG_PREFIX} SnapshotRequestRateLimited: client {entry.ClientId} (entity {entry.Occupant}) had {entry.DroppedInWindow} drops in the window that ended at tick {currentTick}.");
                    }

                    _builder.SendSnapshot(entry.ClientId);
                    entry.LastServedTick = currentTick;
                    entry.HasServed = true;
                    entry.RateLimitLogged = false;
                    entry.DroppedInWindow = 0u;
                    continue;
                }

                if (entry.DroppedInWindow < uint.MaxValue)
                {
                    entry.DroppedInWindow++;
                }

                if (!entry.RateLimitLogged)
                {
                    entry.RateLimitLogged = true;
                    Debug.LogWarning($"{LOG_PREFIX} SnapshotRequestRateLimited: client {entry.ClientId} (entity {entry.Occupant}) asked for a snapshot at tick {currentTick}, last served at {entry.LastServedTick} (window {_window}); dropped. Further drops in this window are counted, and summarised when the slot is next served.");
                }
            }
        }

        private void NoteIdentityDrop(uint clientId, EntityID entity)
        {
            if (_identityDrops == 0)
            {
                _identityFirstClient = clientId;
                _identityFirstEntity = entity;
            }

            if (_identityDrops < int.MaxValue)
            {
                _identityDrops++;
            }
        }

        private void ReportIdentityDrops()
        {
            if (_identityDrops == 0 && _foreignOverflow == 0)
            {
                return;
            }

            string first = _identityDrops > 0
                ? $"first client {_identityFirstClient} entity {_identityFirstEntity}"
                : "none identified";
            Debug.LogWarning($"{LOG_PREFIX} SnapshotRequestIdentityMismatch: {_identityDrops} drops, {_foreignOverflow} overflow (foreign list capacity {_foreignClient.Length}) in {_zone}; requests had no player slot for the sending connection; {first}.");
            _identityDrops = 0;
            _foreignOverflow = 0;
        }

        // Zone mismatch or Closed: reply ZoneDoesNotExist (with the zone id the client sent) and report true.
        private bool TrySendZoneDoesNotExist(uint clientId, ZoneID sentZone)
        {
            if (sentZone == _zone && _lifecycle.State != ZoneState.Closed)
            {
                return false;
            }

            var reply = new ZoneFullResponse(sentZone, JoinRejectedReason.ZoneDoesNotExist, 0);
            int size = ZoneFullResponseCodec.WriteBody(_replyBuffer, in reply);
            _outbox.Enqueue(clientId, ZoneFullResponse.MessageTypeId, new ReadOnlySpan<byte>(_replyBuffer, 0, size), isCapExempt: false);
            return true;
        }
    }
}
