using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.HitDetection
{
    /// <summary>
    /// Sends each <c>CheckHit</c> result to the one player involved through the R-U batch (design/gdd/hit-detection.md
    /// CR-HD-10, CR-HD-11, AC-HD-11a, AC-HD-11b). Hit Detection Story 004.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Routing.</b> The attacker's client when the attacker holds a player slot (so a player attacking a player
    /// notifies only the attacker); otherwise the target's client when the target holds a player slot; otherwise
    /// nothing. Only the player pool is consulted, so a mob despawned mid-swing still reports to its target. The
    /// session state is not checked: a ghost player keeps its slot, and <see cref="RUBatchOutboundStep"/> drops the
    /// message for a client that is gone.
    /// </para>
    /// <para>
    /// Tick thread only. Reads the tick at call time. Allocates nothing on the success path. An
    /// <see cref="EntityID.Invalid"/> id would make the wire codec throw, so it is dropped, with at most one error
    /// per tick.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var emitter = new HitResultEmitter(pool, ruBatch, tickLoop);
    /// emitter.Emit(attackerId, targetId, HitResult.Hit);
    /// </code>
    /// </example>
    public sealed class HitResultEmitter : IHitResultEmitter
    {
        private const string LOG_PREFIX = "[HitResultEmitter]";

        private readonly ZoneSlotPool _pool;
        private readonly IRUBatchCollector _collector;
        private readonly IServerTickSource _tick;
        private bool _loggedInvalid;
        private uint _loggedInvalidTick;

        /// <summary>Creates the emitter.</summary>
        /// <param name="pool">The zone's slot pool; resolves an entity to a player client.</param>
        /// <param name="collector">The R-U batch intake.</param>
        /// <param name="tick">Source of the tick stamped on each result.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public HitResultEmitter(ZoneSlotPool pool, IRUBatchCollector collector, IServerTickSource tick)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        }

        /// <inheritdoc />
        public void Emit(EntityID attacker, EntityID target, HitResult result)
        {
            uint tick = _tick.ServerTickNumber;
            if (attacker == EntityID.Invalid || target == EntityID.Invalid)
            {
                LogInvalid(attacker, target, tick);
                return;
            }

            uint clientId;
            if (_pool.TryGetPlayer(attacker, out ZonePlayerSlot slot))
            {
                clientId = slot.ClientId;
            }
            else if (_pool.TryGetPlayer(target, out slot))
            {
                clientId = slot.ClientId;
            }
            else
            {
                return;
            }

            var message = new HitCheckResult(attacker, target, result, tick);
            Span<byte> body = stackalloc byte[HitCheckResult.BodySize];
            BatchSubMessageCodec.WriteHitCheckResultBody(body, in message);
            _collector.Enqueue(clientId, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body);
        }

        // At most one error per tick: mobs attack too, so a caller bug is not bounded by player count.
        private void LogInvalid(EntityID attacker, EntityID target, uint tick)
        {
            if (_loggedInvalid && _loggedInvalidTick == tick)
            {
                return;
            }

            _loggedInvalid = true;
            _loggedInvalidTick = tick;
            Debug.LogError($"{LOG_PREFIX} InvalidEntityId: attacker={attacker}, target={target} - result not queued; further drops this tick are not logged.");
        }
    }
}
