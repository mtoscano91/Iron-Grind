using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Production <see cref="ISkillUnlockOutput"/>: sends a <see cref="SkillUnlockNotification"/> to the owning client
    /// over the R-OD priority path (skill-system.md CR-SK-21; Skill System Story 003).
    /// </summary>
    /// <remarks>
    /// A ghost or unmapped owner (no pool slot, or no outbox client) is skipped silently. A fault from the outbox is
    /// logged once and never thrown. Tick thread only. Allocates nothing on the success path.
    /// </remarks>
    public sealed class SkillUnlockOutputEmitter : ISkillUnlockOutput
    {
        private const string LOG_PREFIX = "[SkillUnlockOutputEmitter]";

        private readonly ZoneSlotPool _pool;
        private readonly ClientMessageOutbox _outbox;
        private bool _loggedFault;

        /// <summary>Creates the emitter.</summary>
        /// <param name="pool">The zone's slot pool; resolves an entity to its client.</param>
        /// <param name="outbox">The priority-path outbox.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public SkillUnlockOutputEmitter(ZoneSlotPool pool, ClientMessageOutbox outbox)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public void SendUnlock(EntityID owner, SkillID skillId)
        {
            try
            {
                if (!_pool.TryGetPlayer(owner, out ZonePlayerSlot slot))
                {
                    return;
                }

                uint clientId = slot.ClientId;
                if (!_outbox.TryGetSequenceCounter(clientId, out _))
                {
                    return;
                }

                var message = new SkillUnlockNotification(skillId);
                Span<byte> body = stackalloc byte[SkillUnlockNotification.BodySize];
                int written = SkillUnlockNotificationCodec.WriteBody(body, in message);
                _outbox.Enqueue(clientId, SkillUnlockNotification.MessageTypeId, body.Slice(0, written), isCapExempt: false);
            }
            catch (Exception ex)
            {
                if (!_loggedFault)
                {
                    _loggedFault = true;
                    Debug.LogError($"{LOG_PREFIX} SendUnlockThrew: {ex.GetType().Name}: {ex.Message} (logged once).");
                }
            }
        }
    }
}
