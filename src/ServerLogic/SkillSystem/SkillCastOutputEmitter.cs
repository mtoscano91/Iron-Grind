using System;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Sends the cast outcome over the R-U batch (skill-system.md CR-SK-9, CR-SK-12, CR-SK-23; Skill System Story 009).
    /// A rejection goes to the caster only; an accepted cast goes to the caster plus every other zone client that
    /// targets the caster or the target, or is in the party of either. The caster also gets one
    /// <see cref="SkillCooldownUpdate"/> (enqueued right after the caster's result) after an accepted cast and after an <see cref="SkillCastRejectionCode.OnCooldown"/>
    /// rejection, never for any other code and never when a cooldown expires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>targetID</c> on the wire is <see cref="SkillCastRequest.TargetEntityId"/> as submitted (the seam carries no
    /// resolved target); for a Self skill that may be 0. Each client is sent a message at most once, using a
    /// de-duplication buffer allocated in the constructor. Bodies are encoded into stack buffers, so a call allocates
    /// nothing. A caster with no client slot (not a player) gets nothing. A fault is logged once and swallowed.
    /// Tick thread only. Never throws.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var emitter = new SkillCastOutputEmitter(pool, zone, targets, partySource, ruBatch, service);
    /// var executor = new SkillCastExecutor(mana, config, service, emitter, execution);
    /// </code>
    /// </example>
    public sealed class SkillCastOutputEmitter : ISkillCastOutput
    {
        private const string LOG_PREFIX = "[SkillCastOutputEmitter]";

        private readonly ZoneSlotPool _pool;
        private readonly ZoneID _zone;
        private readonly TargetSlotTracker _targets;
        private readonly ISkillCastPartySource _party;
        private readonly IRUBatchCollector _collector;
        private readonly SkillSystemService _cooldowns;
        private readonly uint[] _recipients;
        private bool _loggedFault;

        /// <summary>Creates the emitter and allocates its recipient buffer (one entry per zone player slot).</summary>
        /// <param name="pool">The zone's slot pool; lists the zone's players and maps them to client ids.</param>
        /// <param name="zone">The zone whose players may receive results.</param>
        /// <param name="targets">Each client's current hostile target.</param>
        /// <param name="party">Party membership for the recipient rule.</param>
        /// <param name="collector">The R-U batch intake.</param>
        /// <param name="cooldowns">Source of the caster's current cooldown expiry.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public SkillCastOutputEmitter(
            ZoneSlotPool pool,
            ZoneID zone,
            TargetSlotTracker targets,
            ISkillCastPartySource party,
            IRUBatchCollector collector,
            SkillSystemService cooldowns)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _zone = zone;
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));
            _recipients = new uint[pool.MaxPlayers];
        }

        /// <inheritdoc />
        public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
        {
            try
            {
                if (!_pool.TryGetPlayer(caster, out ZonePlayerSlot casterSlot))
                {
                    return;
                }

                SkillCastResult result = SkillCastResult.Rejected(request.SkillId, caster.RawValue, request.TargetEntityId, code);
                Span<byte> body = stackalloc byte[SkillCastResult.BodySize];
                SkillCastResultCodec.WriteBody(body, in result);
                EnqueueResult(casterSlot.ClientId, body);
                if (code == SkillCastRejectionCode.OnCooldown)
                {
                    EnqueueCooldownUpdate(casterSlot.ClientId, caster, request.SkillId);
                }
            }
            catch (Exception ex)
            {
                LogFault("SendRejection", ex);
            }
        }

        /// <inheritdoc />
        public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks, in SkillExecutionOutcome outcome)
        {
            try
            {
                if (!_pool.TryGetPlayer(caster, out ZonePlayerSlot casterSlot))
                {
                    return;
                }

                var target = new EntityID(request.TargetEntityId);
                SkillCastResult result = SkillCastResult.Accepted(
                    request.SkillId, caster.RawValue, request.TargetEntityId,
                    outcome.DamageDealt, outcome.HealAmount, outcome.IsCrit, outcome.IsKill, newCooldownTicks);

                // Encoded once; the same bytes go to every recipient.
                Span<byte> body = stackalloc byte[SkillCastResult.BodySize];
                SkillCastResultCodec.WriteBody(body, in result);

                int count = 0;
                _recipients[count++] = casterSlot.ClientId;
                EnqueueResult(casterSlot.ClientId, body);
                // Sent before the roster walk so a fault while resolving other recipients cannot cost the caster its
                // update. The wire order is still fixed by the RUBatchWriter categories, not by enqueue order.
                EnqueueCooldownUpdate(casterSlot.ClientId, caster, request.SkillId);

                bool hasTarget = target.RawValue != 0u;
                var players = _pool.GetPlayerEntityIDsInZone(_zone);
                int total = players.Count;
                for (int i = 0; i < total && count < _recipients.Length; i++)
                {
                    EntityID other = players[i];
                    if (other == caster || !_pool.TryGetPlayer(other, out ZonePlayerSlot slot) || Contains(count, slot.ClientId))
                    {
                        continue;
                    }

                    if (!IsRelevant(slot.ClientId, other, caster, target, hasTarget))
                    {
                        continue;
                    }

                    _recipients[count++] = slot.ClientId;
                    EnqueueResult(slot.ClientId, body);
                }
            }
            catch (Exception ex)
            {
                LogFault("SendAccepted", ex);
            }
        }

        private bool IsRelevant(uint clientId, EntityID other, EntityID caster, EntityID target, bool hasTarget)
        {
            // RFR-1: every client's relevance set holds its own entity (self slot), so a player target always receives it.
            if (hasTarget && other == target)
            {
                return true;
            }

            EntityID current = _targets.GetTarget(clientId);
            if (current == caster || (hasTarget && current == target))
            {
                return true;
            }

            return _party.AreInSameParty(other, caster) || (hasTarget && _party.AreInSameParty(other, target));
        }

        // De-duplicates: a client matched by more than one rule (target, targeter, party) is sent once; also guards a
        // client listed twice in the roster.
        private bool Contains(int count, uint clientId)
        {
            for (int i = 0; i < count; i++)
            {
                if (_recipients[i] == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnqueueResult(uint clientId, ReadOnlySpan<byte> body)
        {
            _collector.Enqueue(clientId, RUBatchCategory.SkillCastResult, SkillCastResult.MessageTypeId, body);
        }

        // Sends the skill's current expiry; nothing when the caster has no such skill.
        private void EnqueueCooldownUpdate(uint clientId, EntityID caster, uint skillId)
        {
            if (!_cooldowns.TryGetCooldownExpiry(caster, skillId, out uint expiry))
            {
                return;
            }

            var update = new SkillCooldownUpdate(skillId, expiry);
            Span<byte> body = stackalloc byte[SkillCooldownUpdate.BodySize];
            SkillCooldownUpdateCodec.WriteBody(body, in update);
            _collector.Enqueue(clientId, RUBatchCategory.SkillCooldownUpdate, SkillCooldownUpdate.MessageTypeId, body);
        }

        private void LogFault(string where, Exception ex)
        {
            if (_loggedFault)
            {
                return;
            }

            _loggedFault = true;
            Debug.LogError($"{LOG_PREFIX} {where}Threw: {ex.GetType().Name}: {ex.Message} (logged once).");
        }
    }
}
