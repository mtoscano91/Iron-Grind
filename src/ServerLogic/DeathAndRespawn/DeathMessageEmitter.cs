using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Production <see cref="IDeathMessageOutput"/>: enqueues the death and respawn wire messages on the
    /// <see cref="ClientMessageOutbox"/> R-OD priority path (death-and-respawn.md CR-DR-6, CR-DR-7, CR-DR-13;
    /// Death and Respawn Story 005). Zone-wide messages reach every client currently in the outbox; owner-only
    /// messages are skipped silently when the entity has no pool slot or its client is not in the outbox.
    /// </summary>
    /// <remarks>
    /// A fault is logged once per emitter and never thrown. The catch is defensive: <see cref="ClientMessageOutbox"/>
    /// buffers in <c>Enqueue</c> and contains its own faults (send faults surface at <c>Flush</c>), so it is not
    /// expected to fire. Tick thread only. Allocates nothing (stack buffers).
    /// Not wired into the zone composition here (Story 008).
    /// </remarks>
    /// <example>
    /// <code>
    /// var emitter = new DeathMessageEmitter(pool, outbox);
    /// emitter.SendDeath(player, 7);
    /// </code>
    /// </example>
    public sealed class DeathMessageEmitter : IDeathMessageOutput
    {
        private const string LOG_PREFIX = "[DeathMessageEmitter]";

        private readonly ZoneSlotPool _pool;
        private readonly ClientMessageOutbox _outbox;
        private bool _loggedFault;

        /// <summary>Creates the emitter.</summary>
        /// <param name="pool">The zone's slot pool; resolves an entity to its client.</param>
        /// <param name="outbox">The priority-path outbox.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public DeathMessageEmitter(ZoneSlotPool pool, ClientMessageOutbox outbox)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public void SendDeath(EntityID entity, byte respawnTimerSeconds)
        {
            try
            {
                if (TryGetOwnerClient(entity, out uint clientId))
                {
                    Span<byte> owned = stackalloc byte[DeathStateEntered.BodySize];
                    int ownedLength = DeathStateEnteredCodec.WriteBody(owned, entity.RawValue, respawnTimerSeconds);
                    _outbox.Enqueue(clientId, DeathStateEntered.MessageTypeId, owned.Slice(0, ownedLength), isCapExempt: false);
                }

                Span<byte> zone = stackalloc byte[EntityDied.BodySize];
                int zoneLength = EntityDiedCodec.WriteBody(zone, entity.RawValue);
                _outbox.EnqueueToAllClients(EntityDied.MessageTypeId, zone.Slice(0, zoneLength), isCapExempt: false);
            }
            catch (Exception ex)
            {
                LogFaultOnce("SendDeathThrew", ex);
            }
        }

        /// <inheritdoc />
        public void SendRespawn(EntityID entity, Vector3 position)
        {
            try
            {
                Span<byte> zone = stackalloc byte[EntityRespawned.BodySize];
                int zoneLength = EntityRespawnedCodec.WriteBody(zone, entity.RawValue, position);
                _outbox.EnqueueToAllClients(EntityRespawned.MessageTypeId, zone.Slice(0, zoneLength), isCapExempt: false);

                if (TryGetOwnerClient(entity, out uint clientId))
                {
                    Span<byte> owned = stackalloc byte[RespawnConfirmed.BodySize];
                    int ownedLength = RespawnConfirmedCodec.WriteBody(owned, entity.RawValue);
                    _outbox.Enqueue(clientId, RespawnConfirmed.MessageTypeId, owned.Slice(0, ownedLength), isCapExempt: false);
                }
            }
            catch (Exception ex)
            {
                LogFaultOnce("SendRespawnThrew", ex);
            }
        }

        private bool TryGetOwnerClient(EntityID entity, out uint clientId)
        {
            if (_pool.TryGetPlayer(entity, out ZonePlayerSlot slot) && _outbox.TryGetSequenceCounter(slot.ClientId, out _))
            {
                clientId = slot.ClientId;
                return true;
            }

            clientId = 0u;
            return false;
        }

        private void LogFaultOnce(string what, Exception ex)
        {
            if (_loggedFault)
            {
                return;
            }

            _loggedFault = true;
            Debug.LogError($"{LOG_PREFIX} {what}: {ex.GetType().Name}: {ex.Message} (logged once).");
        }
    }
}
