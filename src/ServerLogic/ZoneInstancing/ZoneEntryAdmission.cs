using System;
using System.Collections.Generic;
using System.Threading;
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
    /// of checks: (1) a load already pending for the connection is ignored with one warning
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
        /// <param name="retryAfterSeconds">Retry hint of <c>ZoneFull</c>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is invalid.</exception>
        public ZoneEntryAdmission(ZoneID zone, ZoneSlotPool pool, ZoneLifecycle lifecycle, ICharacterPersistence persistence,
            ILoadedRecordApplier applier, ITickCompletionQueue queue, IZoneEntryTokenValidator validator,
            IClientMessageOutbox outbox, IZoneEntryContinuation continuation,
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
            _retryAfterSeconds = retryAfterSeconds;
        }

        /// <summary>Number of connections with a load in flight.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Starts one entry. Tick thread only. See the class remarks for the order of checks.
        /// </summary>
        /// <param name="request">The decoded handshake.</param>
        /// <exception cref="InvalidOperationException">Called off the tick thread (thrown by <see cref="ITickCompletionQueue.Track{T}"/>); the pending entry is cleaned up first.</exception>
        public void BeginEntry(in ZoneEntryRequest request)
        {
            uint clientId = request.ClientId;
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
            try
            {
                ZoneEntryRequest captured = request;
                _queue.Track(_persistence.LoadCharacter(request.Character, request.Account, cts.Token), cts,
                    result => OnLoadCompleted(captured, entry, cts, result));
            }
            catch (Exception)
            {
                _pending.Remove(clientId);
                cts.Dispose();
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

        private void OnLoadCompleted(ZoneEntryRequest request, PendingEntry entry, CancellationTokenSource cts,
            TickTaskResult<CharacterLoadResult> result)
        {
            cts.Dispose();
            uint clientId = request.ClientId;
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
                string why = result.Status != TickTaskStatus.Completed ? result.Status.ToString() : result.Value.Code.ToString();
                Debug.LogWarning($"{LOG_PREFIX} LoadCharacter failed for client {clientId} ({why}); entry rejected.");
                Reply(clientId, request.TargetZone, JoinRejectedReason.LoadCharacterFailed, 0);
                return;
            }

            // Re-check (EC-ZI-1, AC-ZI-27, EC-ZI-4): the loaded record is discarded on any refusal.
            if (!TryPreCheck(clientId, request.TargetZone))
            {
                return;
            }

            if (!_pool.TryAllocatePlayer(_zone, request.Character, request.Account, clientId, out byte slot, out EntityID entityId))
            {
                Debug.LogError($"{LOG_PREFIX} TryAllocatePlayer failed below the cap for client {clientId} in {_zone} (EntityID counter exhausted?).");
                Reply(clientId, request.TargetZone, JoinRejectedReason.Other, 0);
                return;
            }

            CharacterLoadCode applied = _applier.ApplyLoadedRecord(entityId, result.Value.Record);
            if (applied != CharacterLoadCode.Success)
            {
                Debug.LogWarning($"{LOG_PREFIX} ApplyLoadedRecord returned {applied} for client {clientId}; slot released.");
                _pool.ReleasePlayer(entityId);
                Reply(clientId, request.TargetZone, JoinRejectedReason.LoadCharacterFailed, 0);
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
