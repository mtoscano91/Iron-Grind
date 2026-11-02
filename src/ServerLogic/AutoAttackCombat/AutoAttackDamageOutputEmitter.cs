using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Sends the damage output of one auto-attack hit (design/gdd/auto-attack-combat.md Rule 10 Step 2; AC-CCR-04,
    /// AC-MCR-02): a <see cref="SelfDamageEvent"/> to the attacker's client alone over the R-OD priority path, and a
    /// <see cref="DamageEvent"/> through the R-U batch to every other zone client. Auto-Attack Combat Story 007.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attacker is the singleton recipient of the self message (never taken from the broadcast list) and is skipped
    /// in the broadcast loop, so the attacker never gets both for one hit. A result with no damage sends nothing. A
    /// <see cref="DamageContext"/> with no wire damage type is logged once and not sent. A ghost player keeps a pool
    /// slot but has no outbox client: the self message is skipped before the outbox sees it and the batch step drops
    /// the broadcast one silently. <c>KillEvent</c> is not sent here (Enemy AI epic).
    /// </para>
    /// <para>
    /// A fault (an exception from the outbox or collector) aborts the remaining sends of that one Emit, so a fault in
    /// the self send also skips the broadcast. It is logged once as <c>EmitThrew</c>; later faults are silent.
    /// </para>
    /// <para>Tick thread only. Allocates nothing on the success path. Never throws.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var emitter = new AutoAttackDamageOutputEmitter(pool, zone, outbox, ruBatch);
    /// emitter.Emit(attackerId, targetId, in result);
    /// </code>
    /// </example>
    public sealed class AutoAttackDamageOutputEmitter : IAutoAttackDamageOutput
    {
        private const string LOG_PREFIX = "[AutoAttackDamageOutputEmitter]";

        private readonly ZoneSlotPool _pool;
        private readonly ZoneID _zone;
        private readonly ClientMessageOutbox _outbox;
        private readonly IRUBatchCollector _collector;
        private bool _loggedUnmapped;
        private bool _loggedFault;

        /// <summary>Creates the emitter.</summary>
        /// <param name="pool">The zone's slot pool; resolves an entity to a client and lists the zone's players.</param>
        /// <param name="zone">The zone whose players receive the broadcast.</param>
        /// <param name="outbox">The priority-path outbox for the self message.</param>
        /// <param name="collector">The R-U batch intake for the broadcast message.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public AutoAttackDamageOutputEmitter(ZoneSlotPool pool, ZoneID zone, ClientMessageOutbox outbox, IRUBatchCollector collector)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _zone = zone;
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        }

        /// <inheritdoc />
        public void Emit(EntityID attacker, EntityID target, in DamageResult result)
        {
            try
            {
                if (result.FinalDamage <= 0)
                {
                    return;
                }

                if (!TryMapDamageType(result.DamageContext, out DamageType damageType))
                {
                    if (!_loggedUnmapped)
                    {
                        _loggedUnmapped = true;
                        Debug.LogError($"{LOG_PREFIX} UnmappedDamageContext: context={result.DamageContext} - no damage type on the wire; output not sent (logged once).");
                    }

                    return;
                }

                var evt = new DamageEvent(attacker, target, result.FinalDamage, result.IsCrit, damageType);
                SendSelf(attacker, in evt);
                SendBroadcast(attacker, in evt);
            }
            catch (Exception ex)
            {
                if (!_loggedFault)
                {
                    _loggedFault = true;
                    Debug.LogError($"{LOG_PREFIX} EmitThrew: {ex.GetType().Name}: {ex.Message} (logged once).");
                }
            }
        }

        private static bool TryMapDamageType(DamageContext context, out DamageType damageType)
        {
            if (context == DamageContext.PhysicalAuto)
            {
                damageType = DamageType.Physical;
                return true;
            }

            damageType = default;
            return false;
        }

        private void SendSelf(EntityID attacker, in DamageEvent evt)
        {
            if (!_pool.TryGetPlayer(attacker, out ZonePlayerSlot slot))
            {
                return;
            }

            uint clientId = slot.ClientId;
            if (!_outbox.TryGetSequenceCounter(clientId, out _))
            {
                return;
            }

            Span<byte> body = stackalloc byte[DamageEvent.BodySize];
            int written = SelfDamageEventDispatcher.WriteBodyForAttacker(body, attacker, in evt);
            _outbox.Enqueue(clientId, SelfDamageEvent.MessageTypeId, body.Slice(0, written), isCapExempt: false);
        }

        private void SendBroadcast(EntityID attacker, in DamageEvent evt)
        {
            IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_zone);
            int count = players.Count;
            for (int i = 0; i < count; i++)
            {
                EntityID player = players[i];
                if (player == attacker)
                {
                    continue;
                }

                if (_pool.TryGetPlayer(player, out ZonePlayerSlot slot))
                {
                    _collector.EnqueueDamageEvent(slot.ClientId, in evt);
                }
            }
        }
    }
}
