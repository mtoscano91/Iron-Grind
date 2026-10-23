using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>What <see cref="ZoneEntryReclamation.PrepareHandshake"/> tells <see cref="ZoneEntryAdmission"/> to do.</summary>
    public enum HandshakeDisposition
    {
        /// <summary>Admission continues (any stale entering slot of the character was reclaimed first).</summary>
        Proceed = 0,

        /// <summary>The character holds an acked (or otherwise settled) slot on another connection; ignore the handshake.</summary>
        CharacterAlreadyInZone = 1,
    }

    /// <summary>
    /// Entering-phase tracking, the entry stall timer, <c>ZoneEntryAck</c> handling, stale slot reclamation cases
    /// (a) stall timeout, (b) duplicate handshake, (c) transport drop, and the shared leave path
    /// (<c>zone-instancing.md</c> CR-ZI-8 step 11, CR-ZI-17, F-ZI-2, EC-ZI-13, EC-ZI-14). Zone Instancing Story 011.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Entering.</b> <see cref="ZoneEntryAdmission"/> calls <see cref="RegisterEntering"/> when it hands an admitted
    /// player to the continuation; the allocation tick is read from the injected tick source. A slot stays entering
    /// until a <see cref="ZoneEntryAck"/> naming this zone arrives from its connection. Steps 6-10 of entry do not
    /// wait for the ack.
    /// </para>
    /// <para>
    /// <b>Record in the callback, act on the tick.</b> <see cref="OnConnectionMessage"/> (receive callback) and
    /// <see cref="NoteConnectionRemoved"/> (transport callback) only record; they never mutate a slot and never log.
    /// <see cref="OnTick"/> applies recorded acks, then recorded removals (case c), then scans for stalled slots
    /// (case a). <b>Tick hook:</b> <see cref="OnTick"/> belongs to the tick step that precedes the close steps
    /// (step 3, Simulation, before teardown, EC-ZI-15); Story 014 wires it. The scan is O(entering slots) and
    /// allocates nothing once the record lists have reached their working size.
    /// </para>
    /// <para>
    /// <b>Leave path.</b> <see cref="LeaveZone"/> is the one entry point for every case and for Story 013's
    /// <c>ReleasePlayer</c>: roster off, <c>PlayerLeftZone</c> to the other connected clients, party exit notice,
    /// slot freed with its directory entry. It contains no save logic.
    /// </para>
    /// <para>
    /// <b>Close seam.</b> The server-side close uses <see cref="IInboundTransportDisconnector"/> with the client id
    /// widened to <c>ulong</c> (the same seam the inbound transport adapter uses). Tick thread only.
    /// </para>
    /// </remarks>
    public sealed class ZoneEntryReclamation : IConnectionMessageSink
    {
        /// <summary>Default <c>ZONE_ENTRY_STALL_TIMEOUT_SECONDS</c> (GDD tuning knob).</summary>
        public const int ZONE_ENTRY_STALL_TIMEOUT_SECONDS = 35;

        /// <summary>Lower bound of the <c>ZONE_ENTRY_STALL_TIMEOUT_SECONDS</c> range.</summary>
        public const int MIN_STALL_TIMEOUT_SECONDS = 31;

        /// <summary>Upper bound of the <c>ZONE_ENTRY_STALL_TIMEOUT_SECONDS</c> range.</summary>
        public const int MAX_STALL_TIMEOUT_SECONDS = 60;

        private const string LOG_PREFIX = "[ZoneEntryReclamation]";

        /// <summary>The connection-level descriptor for <see cref="ZoneEntryAck"/>: 8-byte body, no sender EntityID.</summary>
        public static readonly ConnectionMessageDescriptor Descriptor = new ConnectionMessageDescriptor(
            ZoneEntryAck.MessageTypeId, maxBodyBytes: ZoneEntryAck.BodySize, carriesSenderEntityId: false);

        private enum Cause
        {
            Stall,
            DuplicateHandshake,
            TransportDrop,
        }

        private struct Entering
        {
            public uint ClientId;
            public EntityID Entity;
            public CharacterID Character;
            public uint AllocTick;
        }

        private struct RecordedAck
        {
            public uint ClientId;
            public ZoneID Zone;
            public uint SnapshotVersion;
        }

        private readonly ZoneID _zone;
        private readonly ZoneSlotPool _pool;
        private readonly ZoneConnectionCharacterDirectory _directory;
        private readonly ZoneTickRoster _roster;
        private readonly IPartyZonePresence _party;
        private readonly IClientMessageOutbox _outbox;
        private readonly IInboundTransportDisconnector _disconnector;
        private readonly IZoneEntryReclaimSessionSink _sessionSink;
        private readonly IServerTickSource _ticks;
        private readonly uint _stallTicks;
        private readonly List<Entering> _entering;
        private readonly Dictionary<uint, EntityID> _acked;
        private readonly List<RecordedAck> _recordedAcks = new List<RecordedAck>(8);
        private readonly List<uint> _recordedRemovals = new List<uint>(8);
        private readonly HashSet<uint> _ackWarned = new HashSet<uint>();
        private readonly byte[] _leftBody = new byte[PlayerLeftZone.BodySize];
        private readonly byte[] _fullBody = new byte[ZoneFullResponse.BodySize];
        private int _malformedCount;

        /// <summary>Creates the service for one zone.</summary>
        /// <param name="zone">The zone this process hosts; not <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="pool">The slot pool.</param>
        /// <param name="directory">Connection-to-character map; entries are removed by the leave path.</param>
        /// <param name="roster">The tick roster; the leave path deactivates the entity.</param>
        /// <param name="party">The party seam; <c>NotifyZoneExit</c> is called only for a formal party.</param>
        /// <param name="outbox">Receives <c>PlayerLeftZone</c> and <c>ZoneFullResponse</c>.</param>
        /// <param name="disconnector">The transport close seam.</param>
        /// <param name="sessionSink">The session-layer seam (TD-077 stand-in is the composition default).</param>
        /// <param name="ticks">Gives the allocation tick of an entering slot.</param>
        /// <param name="stallTimeoutSeconds"><c>ZONE_ENTRY_STALL_TIMEOUT_SECONDS</c>, in [31, 60].</param>
        /// <param name="tickRateHz">Ticks per second; the timeout in ticks is computed once from it.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The timeout is outside [31, 60] or the tick rate is below 1.</exception>
        public ZoneEntryReclamation(ZoneID zone, ZoneSlotPool pool, ZoneConnectionCharacterDirectory directory,
            ZoneTickRoster roster, IPartyZonePresence party, IClientMessageOutbox outbox,
            IInboundTransportDisconnector disconnector, IZoneEntryReclaimSessionSink sessionSink, IServerTickSource ticks,
            int stallTimeoutSeconds = ZONE_ENTRY_STALL_TIMEOUT_SECONDS, int tickRateHz = ServerTickLoop.TICK_RATE_HZ)
        {
            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            if (stallTimeoutSeconds < MIN_STALL_TIMEOUT_SECONDS || stallTimeoutSeconds > MAX_STALL_TIMEOUT_SECONDS)
            {
                throw new ArgumentOutOfRangeException(nameof(stallTimeoutSeconds), stallTimeoutSeconds,
                    $"{LOG_PREFIX} stallTimeoutSeconds must be in [{MIN_STALL_TIMEOUT_SECONDS}, {MAX_STALL_TIMEOUT_SECONDS}].");
            }

            if (tickRateHz < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRateHz), tickRateHz, $"{LOG_PREFIX} tickRateHz must be at least 1.");
            }

            _zone = zone;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _disconnector = disconnector ?? throw new ArgumentNullException(nameof(disconnector));
            _sessionSink = sessionSink ?? throw new ArgumentNullException(nameof(sessionSink));
            _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
            _stallTicks = (uint)(stallTimeoutSeconds * tickRateHz);
            _entering = new List<Entering>(pool.MaxPlayers);
            _acked = new Dictionary<uint, EntityID>(pool.MaxPlayers);
        }

        /// <summary>The stall timeout in ticks (<c>ZONE_ENTRY_STALL_TIMEOUT_SECONDS x TICK_RATE_HZ</c>).</summary>
        public uint StallTicks => _stallTicks;

        /// <summary>Number of slots waiting for their ack.</summary>
        public int EnteringCount => _entering.Count;

        /// <summary>
        /// Registers <see cref="Descriptor"/> with <paramref name="service"/> as its sink. Does not seal the dispatcher.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="service">The sink that records the acks.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Register(IInboundRequestDispatcher dispatcher, ZoneEntryReclamation service)
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

        /// <summary>True when the player's slot is waiting for its ack.</summary>
        /// <param name="entityId">The player's EntityID.</param>
        public bool IsEntering(EntityID entityId)
        {
            for (int i = 0; i < _entering.Count; i++)
            {
                if (_entering[i].Entity == entityId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the connection's slot has been acked and is still held.</summary>
        /// <param name="clientId">The connection.</param>
        public bool IsAcked(uint clientId)
        {
            return _acked.TryGetValue(clientId, out EntityID entity)
                && _pool.TryGetPlayer(entity, out ZonePlayerSlot slot) && slot.ClientId == clientId;
        }

        /// <summary>
        /// Marks a freshly admitted slot as entering and records the allocation tick. Called by
        /// <see cref="ZoneEntryAdmission"/> right before the continuation; a slot not in the pool is ignored.
        /// </summary>
        /// <param name="clientId">The slot's connection.</param>
        /// <param name="entityId">The slot's EntityID.</param>
        public void RegisterEntering(uint clientId, EntityID entityId)
        {
            if (!_pool.TryGetPlayer(entityId, out ZonePlayerSlot slot))
            {
                return;
            }

            _acked.Remove(clientId);
            _entering.Add(new Entering { ClientId = clientId, Entity = entityId, Character = slot.Character, AllocTick = _ticks.ServerTickNumber });
        }

        /// <summary>Records a removed connection for the next <see cref="OnTick"/> (case c). Never mutates a slot.</summary>
        /// <param name="clientId">The removed connection.</param>
        public void NoteConnectionRemoved(uint clientId)
        {
            _recordedRemovals.Add(clientId);
        }

        /// <summary>
        /// Records one <see cref="ZoneEntryAck"/> (receive callback; records only, never logs). A body that does not
        /// decode is counted and reported by one warning at the next <see cref="OnTick"/>.
        /// </summary>
        /// <param name="clientId">The connection the message arrived on.</param>
        /// <param name="messageTypeId">Always <see cref="ZoneEntryAck.MessageTypeId"/>; not inspected.</param>
        /// <param name="senderEntityId">Not carried by this type.</param>
        /// <param name="body">The 8-byte body; not valid after return.</param>
        public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body)
        {
            if (!ZoneEntryAckCodec.TryReadBody(body, out ZoneEntryAck ack))
            {
                if (_malformedCount < int.MaxValue)
                {
                    _malformedCount++;
                }

                return;
            }

            _recordedAcks.Add(new RecordedAck { ClientId = clientId, Zone = ack.ZoneId, SnapshotVersion = ack.SnapshotVersion });
        }

        /// <summary>
        /// Runs once per tick (see class remarks for the tick step): applies recorded acks, reclaims the slots of
        /// removed connections (case c), then reclaims every entering slot whose allocation is
        /// <see cref="StallTicks"/> ticks old (case a).
        /// </summary>
        /// <param name="currentTick">The tick being run.</param>
        public void OnTick(uint currentTick)
        {
            if (_malformedCount > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} ZoneEntryAckMalformed: {_malformedCount} ack body(ies) shorter than {ZoneEntryAck.BodySize} bytes dropped.");
                _malformedCount = 0;
            }

            for (int i = 0; i < _recordedAcks.Count; i++)
            {
                ApplyAck(_recordedAcks[i]);
            }

            _recordedAcks.Clear();

            for (int i = 0; i < _recordedRemovals.Count; i++)
            {
                uint clientId = _recordedRemovals[i];
                _ackWarned.Remove(clientId);
                int index = FindEntering(clientId);
                if (index >= 0)
                {
                    Reclaim(index, Cause.TransportDrop);
                }
            }

            _recordedRemovals.Clear();

            for (int i = _entering.Count - 1; i >= 0; i--)
            {
                // Modular uint subtraction: a tick regression fails open (no reclaim).
                if (i < _entering.Count && unchecked(currentTick - _entering[i].AllocTick) >= _stallTicks
                    && unchecked(currentTick - _entering[i].AllocTick) < uint.MaxValue / 2u)
                {
                    Reclaim(i, Cause.Stall);
                }
            }
        }

        /// <summary>
        /// Case (b) check, called by <see cref="ZoneEntryAdmission.BeginEntry"/> after the token check and before the
        /// capacity pre-check. If another connection holds an entering slot for <paramref name="character"/> it is
        /// reclaimed (leave path, closed with no message, session sink with <c>deleteTokenIfOwned: false</c>) and
        /// admission proceeds; if that slot is acked (or otherwise settled) nothing changes and the handshake is
        /// refused (session stealing ends the earlier session first, B-NP-7, TD-076).
        /// </summary>
        /// <param name="character">The character in the new handshake.</param>
        /// <param name="newClientId">The connection that sent it.</param>
        /// <returns>What admission does next.</returns>
        public HandshakeDisposition PrepareHandshake(CharacterID character, uint newClientId)
        {
            if (!_directory.TryGetClientId(character, out uint oldClient) || oldClient == newClientId)
            {
                return HandshakeDisposition.Proceed;
            }

            int index = FindEntering(oldClient);
            if (index >= 0 && _entering[index].Character == character)
            {
                Reclaim(index, Cause.DuplicateHandshake);
                return HandshakeDisposition.Proceed;
            }

            return HandshakeDisposition.CharacterAlreadyInZone;
        }

        /// <summary>
        /// The shared leave path (CR-ZI-17 points 1-4; Story 013 reuses it and adds its save ordering): (1) entity off
        /// the tick roster or the pending list; (2) <c>PlayerLeftZone</c> to every other connected zone client;
        /// (3) <see cref="IPartyZonePresence.NotifyZoneExit"/> only when the character is in a formal party; (4) slot
        /// zeroed and freed, directory entry removed, occupancy change raised by the pool. Never saves. An entity
        /// that holds no slot changes nothing and returns false. A throwing outbox or party seam is logged and the
        /// slot is freed anyway.
        /// </summary>
        /// <param name="entityId">The leaving player's EntityID.</param>
        /// <param name="disconnectType">What <c>PlayerLeftZone</c> reports.</param>
        /// <returns>True when a slot was freed.</returns>
        public bool LeaveZone(EntityID entityId, DisconnectType disconnectType)
        {
            if (!_pool.TryGetPlayer(entityId, out ZonePlayerSlot slot))
            {
                return false;
            }

            _roster.Deactivate(entityId);
            RemoveTracking(entityId);

            try
            {
                SendPlayerLeftZone(entityId, slot.ClientId, disconnectType);
                if (_party.TryGetFormalParty(slot.Character, out PartyID party))
                {
                    _party.NotifyZoneExit(party, entityId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} leave path of {entityId} (client {slot.ClientId}) failed before the slot was freed; freeing it anyway.\n{ex}");
            }

            if (_directory.TryGetCharacterId(slot.ClientId, out CharacterID mapped) && mapped == slot.Character)
            {
                _directory.Remove(slot.ClientId);
            }

            _acked.Remove(slot.ClientId);
            return _pool.ReleasePlayer(entityId);
        }

        private void ApplyAck(in RecordedAck ack)
        {
            int index = FindEntering(ack.ClientId);
            if (index >= 0 && ack.Zone == _zone)
            {
                Entering entry = _entering[index];
                RemoveEnteringAt(index);
                _acked[entry.ClientId] = entry.Entity;
                Debug.Log($"{LOG_PREFIX} ZoneEntryAck from client {ack.ClientId} for {ack.Zone} (snapshotVersion {ack.SnapshotVersion}); slot {entry.Entity} acked.");
                return;
            }

            if (_ackWarned.Add(ack.ClientId))
            {
                string why = index >= 0 ? $"it names {ack.Zone}, not {_zone}" : $"the client has no entering slot in {_zone}";
                Debug.LogWarning($"{LOG_PREFIX} UnexpectedZoneEntryAck: client {ack.ClientId} sent an ack, but {why}; ignored. Further acks from this connection are not logged.");
            }
        }

        private void Reclaim(int index, Cause cause)
        {
            Entering entry = _entering[index];
            RemoveEnteringAt(index);
            LeaveZone(entry.Entity, DisconnectType.Timeout);

            if (cause == Cause.Stall)
            {
                var reply = new ZoneFullResponse(_zone, JoinRejectedReason.Other, 0);
                int size = ZoneFullResponseCodec.WriteBody(_fullBody, in reply);
                // Cap-exempt: the close below removes the client, which would drop a reply the cap deferred to a later tick.
                _outbox.Enqueue(entry.ClientId, ZoneFullResponse.MessageTypeId, new ReadOnlySpan<byte>(_fullBody, 0, size), isCapExempt: true);
            }

            if (cause != Cause.TransportDrop)
            {
                try
                {
                    _disconnector.DisconnectClient(entry.ClientId);
                }
                catch (Exception ex)
                {
                    // A shut-down transport throws; the session still ends and the scan goes on.
                    Debug.LogError($"{LOG_PREFIX} closing client {entry.ClientId} after reclamation ({cause}) failed; the session is expired anyway.\n{ex}");
                }
            }

            _sessionSink.ExpireEnteringSession(entry.ClientId, deleteTokenIfOwned: cause != Cause.DuplicateHandshake);
        }

        private void SendPlayerLeftZone(EntityID leaving, uint leavingClient, DisconnectType disconnectType)
        {
            var message = new PlayerLeftZone(leaving, disconnectType);
            int size = PlayerLeftZoneCodec.WriteBody(_leftBody, in message);
            IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_zone);
            for (int i = 0; i < players.Count; i++)
            {
                EntityID other = players[i];
                if (other == leaving || !_pool.TryGetPlayer(other, out ZonePlayerSlot slot) || slot.ClientId == leavingClient
                    || !_directory.TryGetCharacterId(slot.ClientId, out _))
                {
                    continue;
                }

                _outbox.Enqueue(slot.ClientId, PlayerLeftZone.MessageTypeId, new ReadOnlySpan<byte>(_leftBody, 0, size), isCapExempt: false);
            }
        }

        private int FindEntering(uint clientId)
        {
            for (int i = 0; i < _entering.Count; i++)
            {
                if (_entering[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveTracking(EntityID entityId)
        {
            for (int i = 0; i < _entering.Count; i++)
            {
                if (_entering[i].Entity == entityId)
                {
                    RemoveEnteringAt(i);
                    return;
                }
            }
        }

        private void RemoveEnteringAt(int index)
        {
            int last = _entering.Count - 1;
            _entering[index] = _entering[last];
            _entering.RemoveAt(last);
        }
    }
}
