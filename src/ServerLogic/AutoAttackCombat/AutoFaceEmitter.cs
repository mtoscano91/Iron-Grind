using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Applies the auto-face snap and tells the zone (auto-attack-combat.md Rules 20-22, CR-HD-7, AC-HD-18): the
    /// direction is flattened to XZ and normalised, handed to <see cref="IMovementSystem.InjectFacingOverride"/>, and
    /// an <see cref="AutoFaceEvent"/> goes through the R-U batch to every player client in the zone, the attacker
    /// included. Auto-Attack Combat Story 009.
    /// </summary>
    /// <remarks>
    /// A zero or non-finite XZ direction does nothing: no override, no event. The event is sent only when movement
    /// accepted the override (the entity has a movement slot). A fault is logged once and swallowed. The body is
    /// encoded once into a stack buffer, so a call allocates nothing. Tick thread only. Never throws.
    /// </remarks>
    /// <example>
    /// <code>
    /// var emitter = new AutoFaceEmitter(pool, zone, movement, ruBatch);
    /// emitter.Face(attackerId, targetPos - attackerPos);
    /// </code>
    /// </example>
    public sealed class AutoFaceEmitter : IAutoAttackFaceOutput
    {
        private const string LOG_PREFIX = "[AutoFaceEmitter]";

        private readonly ZoneSlotPool _pool;
        private readonly ZoneID _zone;
        private readonly IMovementSystem _movement;
        private readonly IRUBatchCollector _collector;
        private bool _loggedFault;

        /// <summary>Creates the emitter.</summary>
        /// <param name="pool">The zone's slot pool; lists the zone's players and maps them to client ids.</param>
        /// <param name="zone">The zone whose players receive the event.</param>
        /// <param name="movement">Receives the one-tick facing override.</param>
        /// <param name="collector">The R-U batch intake.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public AutoFaceEmitter(ZoneSlotPool pool, ZoneID zone, IMovementSystem movement, IRUBatchCollector collector)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _zone = zone;
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        }

        /// <inheritdoc />
        public void Face(EntityID attacker, Vector3 direction)
        {
            try
            {
                float x = direction.x;
                float z = direction.z;
                float lengthSq = x * x + z * z;
                if (float.IsNaN(lengthSq) || float.IsInfinity(lengthSq) || lengthSq <= 0f)
                {
                    return;
                }

                float inv = 1f / Mathf.Sqrt(lengthSq);
                var dir = new Vector3(x * inv, 0f, z * inv);
                if (float.IsNaN(dir.x) || float.IsNaN(dir.z) || float.IsInfinity(dir.x) || float.IsInfinity(dir.z))
                {
                    return;
                }

                if (!_movement.InjectFacingOverride(attacker, dir))
                {
                    return;
                }

                var evt = new AutoFaceEvent(attacker, Quaternion.LookRotation(dir, Vector3.up), dir);
                Span<byte> body = stackalloc byte[AutoFaceEvent.BodySize];
                BatchSubMessageCodec.WriteAutoFaceEventBody(body, in evt);

                IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_zone);
                int count = players.Count;
                for (int i = 0; i < count; i++)
                {
                    if (_pool.TryGetPlayer(players[i], out ZonePlayerSlot slot))
                    {
                        _collector.Enqueue(slot.ClientId, RUBatchCategory.AutoFaceEvent, AutoFaceEvent.MessageTypeId, body);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_loggedFault)
                {
                    _loggedFault = true;
                    Debug.LogError($"{LOG_PREFIX} FaceThrew: {ex.GetType().Name}: {ex.Message} (logged once).");
                }
            }
        }
    }
}
