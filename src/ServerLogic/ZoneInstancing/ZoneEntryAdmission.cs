using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Admission half of player entry (<c>zone-instancing.md</c> CR-ZI-8 steps 3-5, CR-ZI-7, EC-ZI-1, EC-ZI-4,
    /// AC-ZI-4, AC-ZI-27). Zone Instancing Story 009.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BeginEntry"/> runs on the tick thread with an already-decoded <see cref="ZoneEntryRequest"/>. Order
    /// of checks: (0) a connection the directory already maps is ignored with one warning
    /// (<c>EntryHandshakeAlreadyAdmitted</c>); (1) a load already pending for the connection is ignored with one warning
    /// (<c>EntryHandshakeDuplicate</c>); (2) wrong zone -> <c>ZoneDoesNotExist</c>; (3) token rejected -> <c>Other</c>,
    /// connection kept open; (4) pre-check: zone not Active -> <c>ZoneDoesNotExist</c>, at cap -> <c>ZoneFull</c> with
    /// <see cref="ZONE_FULL_RETRY_AFTER_SECONDS"/>; (5) <c>LoadCharacter</c> is started and passed to
    /// <see cref="ITickCompletionQueue.Track{T}"/> in the same statement. A reply in (2)-(4) starts no load. Replies
    /// carry the zone id the client sent.
    /// </para>
    /// <para>
    /// The completion callback (in <c>Drain</c>, tick thread) re-checks state and capacity, allocates the slot,
    /// applies the loaded record in the same callback and hands the player to <see cref="IZoneEntryContinuation"/>.
    /// If <c>ApplyLoadedRecord</c> fails the slot is released and no persistence call follows. A connection removed
    /// while its load is pending is marked dropped: the callback sends nothing and allocates nothing. The service
    /// owns the <see cref="CancellationTokenSource"/> of each load and disposes it in the callback.
    /// </para>
    /// <para>
    /// Source lifetime: disposed in the callback; disposed when <c>LoadCharacter</c> throws synchronously; cancelled
    /// but NOT disposed when <see cref="ITickCompletionQueue.Track{T}"/> throws (the running task holds the token and
    /// is orphaned on purpose, a failing Track is a programming error); NOT disposed for a task still incomplete at
    /// <c>DrainOnShutdown</c> (no callback runs).
    /// </para>
    /// <para>
    /// Failure cleanup: when <c>ApplyLoadedRecord</c> fails or throws, only the pool slot is released. Stat / Leveling
    /// writes already made by <c>ApplyLoadedRecord</c> are not rolled back (see the remarks of
    /// <c>CharacterPersistenceService.ApplyLoadedRecord</c>).
    /// </para>
    /// </remarks>
    public sealed class ZoneEntryAdmission : IClientConnectionListener
    {
        /// <summary>Default retry hint sent with <c>ZoneFull</c>.</summary>
        public const byte ZONE_FULL_RETRY_AFTER_SECONDS = 30;

        private const string LOG_PREFIX = "[ZoneEntryAdmission]";

        private sealed class PendingEntry
        {
            public bool Dropped;
        }

        private readonly ZoneID _zone;
        private readonly ZoneSlotPool _pool;
        private readonly ZoneLifecycle _lifecycle;
        private readonly ICharacterPersistence _persistence;
        private readonly ILoadedRecordApplier _applier;
        private readonly ITickCompletionQueue _queue;
        private readonly IZoneEntryTokenValidator _validator;
        private readonly IClientMessageOutbox _outbox;
        private readonly IZoneEntryContinuation _continuation;
        private readonly IConnectionCharacterDirectory _directory;
        private readonly HashSet<uint> _admittedWarned = new HashSet<uint>();
        private readonly byte _retryAfterSeconds;
        private readonly Dictionary<uint, PendingEntry> _pending = new Dictionary<uint, PendingEntry>();
        private readonly HashSet<uint> _duplicateWarned = new HashSet<uint>();
        private readonly byte[] _replyBuffer = new byte[ZoneFullResponse.BodySize];

        /// <summary>Creates the service for one zone.</summary>
        /// <param name="zone">The zone this process hosts; not <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="pool">The slot pool.</param>
        /// <param name="lifecycle">Read for <see cref="ZoneLifecycle.AcceptsPlayers"/>.</param>
        /// <param name="persistence">Starts the character load.</param>
        /// <param name="applier">Applies the loaded record.</param>
        /// <param name="queue">Hands the load result back on the tick.</param>
        /// <param name="validator">Checks the session token.</param>
        /// <param name="outbox">Receives the <see cref="ZoneFullResponse"/> replies.</param>
        /// <param name="continuation">Receives an admitted player.</param>
        /// <param name="directory">Tells which connections already play a character (Story 010); a handshake from one is ignored.</param>
        /// <param name="retryAfterSeconds">Retry hint of <c>ZoneFull</c>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is invalid.</exception>
        public ZoneEntryAdmission(ZoneID zone, ZoneSlotPool pool, ZoneLifecycle lifecycle, ICharacterPersistence persistence,
            ILoadedRecordApplier applier, ITickCompletionQueue queue, IZoneEntryTokenValidator validator,
            IClientMessageOutbox outbox, IZoneEntryContinuation continuation, IConnectionCharacterDirectory directory,
            byte retryAfterSeconds = ZONE_FULL_RETRY_AFTER_SECONDS)
        {
            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            _zone = zone;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _continuation = continuation ?? throw new ArgumentNullException(nameof(continuation));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _retryAfterSeconds = retryAfterSeconds;
        }

        /// <summary>Number of connections with a load in flight.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Starts one entry. Tick thread only. See the class remarks for the order of checks.
        /// </summary>
        /// <param name="request">The decoded handshake.</param>
        /// <exception cref="ArgumentNullException">Thrown by <see cref="ITickCompletionQueue.Track{T}"/> for a null argument; also any exception
        /// <c>LoadCharacter</c> throws synchronously. The pending entry is cleaned up first and the client gets no reply.</exception>
        /// <exception cref="InvalidOperationException">Called off the tick thread (thrown by <see cref="ITickCompletionQueue.Track{T}"/>); the pending entry is cleaned up first.</exception>
        public void BeginEntry(in ZoneEntryRequest request)
        {
            uint clientId = request.ClientId;
            if (_directory.TryGetCharacterId(clientId, out _))
            {
                if (_admittedWarned.Add(clientId))
                {
                    Debug.LogWarning($"{LOG_PREFIX} EntryHandshakeAlreadyAdmitted: client {clientId} already plays a character; handshake ignored.");
                }

                return;
            }

            if (_pending.ContainsKey(clientId))
            {
                if (_duplicateWarned.Add(clientId))
                {
                    Debug.LogWarning($"{LOG_PREFIX} EntryHandshakeDuplicate: client {clientId} sent a handshake while its load is pending; ignored.");
                }

                return;
            }

            if (request.TargetZone != _zone)
            {
                Reply(clientId, request.TargetZone, JoinRejectedReason.ZoneDoesNotExist, 0);
                return;
            }

            if (!_validator.IsValid(in request))
            {
                Reply(clientId, request.TargetZone, JoinRejectedReason.Other, 0);
                return;
            }

            if (!TryPreCheck(clientId, request.TargetZone))
            {
                return;
            }

            var entry = new PendingEntry();
            var cts = new CancellationTokenSource();
            _pending[clientId] = entry;
            ZoneID targetZone = request.TargetZone;
            var character = request.Character;
            var account = request.Account;

            Task<CharacterLoadResult> load;
            try
            {
                load = _persistence.LoadCharacter(character, account, cts.Token);
            }
            catch (Exception)
            {
                _pending.Remove(clientId);
                cts.Dispose();
                throw;
            }

            // Nothing may run between LoadCharacter and Track (ADR-011: started and tracked in the same statement).
            try
            {
                _queue.Track(load, cts, result => OnLoadCompleted(clientId, targetZone, character, account, entry, cts, result));
            }
            catch (Exception)
            {
                _pending.Remove(clientId);
                // Cancelled, not disposed: the running task holds the token. The task is orphaned on purpose; a
                // failing Track is a programming error.
                cts.Cancel();
                throw;
            }
        }

        /// <inheritdoc/>
        public void AddClient(uint clientId)
        {
            // Nothing to prepare: a handshake after AddClient is a fresh entry because RemoveClient cleared the table.
        }

        /// <inheritdoc/>
        public void RemoveClient(uint clientId)
        {
            _duplicateWarned.Remove(clientId);
            _admittedWarned.Remove(clientId);
            if (_pending.TryGetValue(clientId, out PendingEntry entry))
            {
                entry.Dropped = true;
                _pending.Remove(clientId);
            }
        }

        // Zone not Active or at cap -> reply and report false.
        private bool TryPreCheck(uint clientId, ZoneID sentZone)
        {
            if (!_lifecycle.AcceptsPlayers)
            {
                Reply(clientId, sentZone, JoinRejectedReason.ZoneDoesNotExist, 0);
                return false;
            }

            if (_pool.OccupiedPlayerCount >= _pool.MaxPlayers)
            {
                Reply(clientId, sentZone, JoinRejectedReason.ZoneFull, _retryAfterSeconds);
                return false;
            }

            return true;
        }

        private void OnLoadCompleted(uint clientId, ZoneID targetZone, CharacterID character, AccountID account,
            PendingEntry entry, CancellationTokenSource cts, TickTaskResult<CharacterLoadResult> result)
        {
            cts.Dispose();
            if (_pending.TryGetValue(clientId, out PendingEntry current) && ReferenceEquals(current, entry))
            {
                _pending.Remove(clientId);
                _duplicateWarned.Remove(clientId);
            }

            if (entry.Dropped)
            {
                return;
            }

            if (result.Status != TickTaskStatus.Completed || result.Value.Code != CharacterLoadCode.Success || result.Value.Record == null)
            {
                string why = result.Status != TickTaskStatus.Completed
                    ? result.Status.ToString()
                    : result.Value.Code == CharacterLoadCode.Success ? "NullRecord" : result.Value.Code.ToString();
                Debug.LogWarning($"{LOG_PREFIX} LoadCharacter failed for client {clientId} ({why}); entry rejected.");
                Reply(clientId, targetZone, JoinRejectedReason.LoadCharacterFailed, 0);
                return;
            }

            // Re-check (EC-ZI-1, AC-ZI-27, EC-ZI-4): the loaded record is discarded on any refusal.
            if (!TryPreCheck(clientId, targetZone))
            {
                return;
            }

            if (!_pool.TryAllocatePlayer(_zone, character, account, clientId, out byte slot, out EntityID entityId))
            {
                Debug.LogError($"{LOG_PREFIX} TryAllocatePlayer failed below the cap for client {clientId} in {_zone} (EntityID counter exhausted?).");
                Reply(clientId, targetZone, JoinRejectedReason.Other, 0);
                return;
            }

            CharacterLoadCode applied;
            try
            {
                applied = _applier.ApplyLoadedRecord(entityId, result.Value.Record);
            }
            catch (Exception ex)
            {
                _pool.ReleasePlayer(entityId);
                Debug.LogError($"{LOG_PREFIX} ApplyLoadedRecord threw for client {clientId}; slot released.\n{ex}");
                Reply(clientId, targetZone, JoinRejectedReason.LoadCharacterFailed, 0);
                return;
            }

            if (applied != CharacterLoadCode.Success)
            {
                Debug.LogWarning($"{LOG_PREFIX} ApplyLoadedRecord returned {applied} for client {clientId}; slot released.");
                _pool.ReleasePlayer(entityId);
                Reply(clientId, targetZone, JoinRejectedReason.LoadCharacterFailed, 0);
                return;
            }

            _continuation.OnPlayerAdmitted(clientId, slot, entityId);
        }

        private void Reply(uint clientId, ZoneID sentZone, JoinRejectedReason reason, byte retryAfterSeconds)
        {
            var reply = new ZoneFullResponse(sentZone, reason, retryAfterSeconds);
            int size = ZoneFullResponseCodec.WriteBody(_replyBuffer, in reply);
            _outbox.Enqueue(clientId, ZoneFullResponse.MessageTypeId, new ReadOnlySpan<byte>(_replyBuffer, 0, size), isCapExempt: false);
        }
    }
}
