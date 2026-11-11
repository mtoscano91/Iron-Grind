using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Connection-level sink for <see cref="SessionHandshake"/> (0xE136): decodes the body in the receive callback and
    /// hands the request to <see cref="ZoneEntryAdmission.BeginEntry"/> on the tick (<see cref="Drain"/>). Gateway
    /// Story 012, CR-ZI-8 steps 2-3, CR-TOK-9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Record in the callback, act on the tick.</b> <see cref="OnConnectionMessage"/> only records (a request, or a
    /// "malformed: close" mark): no logging, no <c>BeginEntry</c>, no disconnect. <see cref="Drain"/> runs once per tick
    /// after <see cref="ZoneEntryReclamation.OnTick"/>, in arrival order: a malformed mark closes the connection through
    /// <see cref="IInboundTransportDisconnector"/> with no message; a request goes to <c>BeginEntry</c>.
    /// </para>
    /// <para>
    /// <b>Malformed</b> = body length other than 24, <c>characterId == 0</c> or an all-zero token. The decoder never
    /// compares tokens and never checks the zone: a wrong <c>targetZoneId</c> is answered by admission
    /// (<c>ZoneDoesNotExist</c>).
    /// </para>
    /// <para>
    /// <b>Bound.</b> At most <see cref="Capacity"/> records wait for a drain; further ones are dropped and counted, with
    /// one warning at the next drain. <see cref="RemoveClient"/> discards what is recorded for a connection. Same
    /// thread discipline as <see cref="ZoneEntryReclamation"/> (no locking).
    /// </para>
    /// <para>
    /// <b>One record per client per drain.</b> A client that already has a record waiting (valid or malformed) has
    /// further handshakes ignored until the next <see cref="Drain"/>; they are counted in
    /// <see cref="DuplicateIgnoredCount"/> and reported by one warning per drain. A client may send again after the drain.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var intake = new SessionHandshakeIntake(admission, disconnector, maxPlayers * 2);
    /// SessionHandshakeIntake.Register(dispatcher, intake);
    /// // each tick, after reclamation.OnTick: intake.Drain(tick);
    /// </code>
    /// </example>
    public sealed class SessionHandshakeIntake : IConnectionMessageSink, IClientConnectionListener
    {
        private const string LOG_PREFIX = "[SessionHandshakeIntake]";

        /// <summary>
        /// Largest body the dispatcher lets through to this sink: every body that fits one inbound message, not just
        /// <see cref="SessionHandshake.BodySize"/>. The dispatcher drops a body longer than its type's bound without
        /// closing the connection, so a bound of 24 would leave an oversized handshake open; with this bound every
        /// wrong-length handshake reaches <see cref="OnConnectionMessage"/> and is closed (CR-TOK-9).
        /// </summary>
        public const ushort MAX_ACCEPTED_BODY_BYTES = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ServerMessageEnvelope.WireSize;

        /// <summary>
        /// The connection-level descriptor for <see cref="SessionHandshake"/>: no sender EntityID, body bound
        /// <see cref="MAX_ACCEPTED_BODY_BYTES"/> (the 24-byte length itself is checked by the codec in the sink).
        /// </summary>
        public static readonly ConnectionMessageDescriptor Descriptor = new ConnectionMessageDescriptor(
            SessionHandshake.MessageTypeId, maxBodyBytes: MAX_ACCEPTED_BODY_BYTES, carriesSenderEntityId: false);

        private struct Recorded
        {
            public uint ClientId;
            public bool Malformed;
            public CharacterID Character;
            public ZoneID Zone;
            public byte[] Token;
        }

        private readonly ZoneEntryAdmission _admission;
        private readonly IInboundTransportDisconnector _disconnector;
        private readonly int _capacity;
        private readonly List<Recorded> _recorded;
        private readonly HashSet<uint> _queuedClients;
        private int _overflowSinceWarning;
        private int _overflowTotal;
        private int _duplicateSinceWarning;
        private int _duplicateTotal;

        /// <summary>Creates the intake for one zone.</summary>
        /// <param name="admission">Receives the well-formed requests.</param>
        /// <param name="disconnector">Closes the connection of a malformed handshake.</param>
        /// <param name="capacity">Largest number of records waiting for a drain (at least 1).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public SessionHandshakeIntake(ZoneEntryAdmission admission, IInboundTransportDisconnector disconnector, int capacity)
        {
            _admission = admission ?? throw new ArgumentNullException(nameof(admission));
            _disconnector = disconnector ?? throw new ArgumentNullException(nameof(disconnector));
            _capacity = Math.Max(1, capacity);
            _recorded = new List<Recorded>(_capacity);
            _queuedClients = new HashSet<uint>(_capacity);
        }

        /// <summary>Total handshakes ignored because the same client already had one recorded for the next drain (never reset).</summary>
        public int DuplicateIgnoredCount => _duplicateTotal;

        /// <summary>Largest number of records waiting for a drain.</summary>
        public int Capacity => _capacity;

        /// <summary>Records waiting for the next <see cref="Drain"/>.</summary>
        public int PendingCount => _recorded.Count;

        /// <summary>Total records dropped because the queue was full (never reset).</summary>
        public int OverflowDroppedCount => _overflowTotal;

        /// <summary>Registers <see cref="Descriptor"/> with <paramref name="intake"/> as its sink. Does not seal the dispatcher.</summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="intake">The sink.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Register(IInboundRequestDispatcher dispatcher, SessionHandshakeIntake intake)
        {
            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }

            if (intake == null)
            {
                throw new ArgumentNullException(nameof(intake));
            }

            dispatcher.RegisterConnectionLevel(Descriptor, intake);
        }

        /// <summary>
        /// Records one handshake (receive callback; records only, never logs, never calls admission). A body that is
        /// not 24 bytes, has <c>characterId == 0</c> or an all-zero token is recorded as "malformed: close".
        /// </summary>
        /// <param name="clientId">The connection the message arrived on.</param>
        /// <param name="messageTypeId">Always <see cref="SessionHandshake.MessageTypeId"/>; not inspected.</param>
        /// <param name="senderEntityId">Not carried by this type.</param>
        /// <param name="body">The body; not valid after return.</param>
        public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body)
        {
            if (_queuedClients.Contains(clientId))
            {
                _duplicateSinceWarning++;
                _duplicateTotal++;
                return;
            }

            if (_recorded.Count >= _capacity)
            {
                _overflowSinceWarning++;
                _overflowTotal++;
                return;
            }

            _queuedClients.Add(clientId);
            if (!SessionHandshakeCodec.TryReadBody(body, out SessionHandshake message)
                || message.CharacterId.RawValue == 0u || IsAllZero(message.SessionToken))
            {
                _recorded.Add(new Recorded { ClientId = clientId, Malformed = true });
                return;
            }

            _recorded.Add(new Recorded
            {
                ClientId = clientId,
                Character = message.CharacterId,
                Zone = message.TargetZoneId,
                Token = message.SessionToken,
            });
        }

        /// <summary>
        /// Runs once per tick (tick thread): closes the malformed connections and calls
        /// <see cref="ZoneEntryAdmission.BeginEntry"/> for each recorded request, in arrival order. One warning at most
        /// for the malformed count and one for the overflow count.
        /// </summary>
        /// <param name="currentTick">The tick being run (for the log only).</param>
        public void Drain(uint currentTick)
        {
            int malformed = 0;
            for (int i = 0; i < _recorded.Count; i++)
            {
                Recorded item = _recorded[i];
                if (item.Malformed)
                {
                    malformed++;
                    CloseMalformed(item.ClientId);
                    continue;
                }

                try
                {
                    var request = new ZoneEntryRequest(item.ClientId, item.Character, item.Zone, item.Token);
                    _admission.BeginEntry(in request);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} BeginEntry failed for client {item.ClientId}; the next handshake runs.\n{ex}");
                }
            }

            _recorded.Clear();
            _queuedClients.Clear();
            WarnCounts(currentTick, malformed);
        }

        private void CloseMalformed(uint clientId)
        {
            try
            {
                _disconnector.DisconnectClient(clientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} closing client {clientId} failed.\n{ex}");
            }
        }

        private void WarnCounts(uint currentTick, int malformed)
        {
            if (malformed > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} SessionHandshakeMalformed: {malformed} handshake(s) closed at tick {currentTick}.");
            }

            if (_overflowSinceWarning > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} SessionHandshakeOverflow: {_overflowSinceWarning} handshake(s) dropped, queue full ({_capacity}).");
                _overflowSinceWarning = 0;
            }

            if (_duplicateSinceWarning > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} SessionHandshakeDuplicate: {_duplicateSinceWarning} repeat handshake(s) ignored (one per client per tick).");
                _duplicateSinceWarning = 0;
            }
        }

        /// <inheritdoc/>
        public void AddClient(uint clientId)
        {
        }

        /// <summary>Discards everything recorded for <paramref name="clientId"/> (the connection is gone).</summary>
        /// <param name="clientId">The removed connection.</param>
        public void RemoveClient(uint clientId)
        {
            _queuedClients.Remove(clientId);
            int write = 0;
            for (int read = 0; read < _recorded.Count; read++)
            {
                if (_recorded[read].ClientId == clientId)
                {
                    continue;
                }

                if (write != read)
                {
                    _recorded[write] = _recorded[read];
                }

                write++;
            }

            if (write < _recorded.Count)
            {
                _recorded.RemoveRange(write, _recorded.Count - write);
            }
        }

        private static bool IsAllZero(byte[] token)
        {
            for (int i = 0; i < token.Length; i++)
            {
                if (token[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
