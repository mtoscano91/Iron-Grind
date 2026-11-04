using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// <see cref="IZoneEntryResponder"/> decorator that sends the real <see cref="SkillCooldownSnapshot"/> to the
    /// joining client over the R-OD priority path (skill-system.md CR-SK-12, CR-SK-23; Skill System Story 010).
    /// </summary>
    /// <remarks>
    /// <see cref="SendSessionReady"/> is delegated to the inner responder unchanged. The inner responder's
    /// <see cref="SendSkillCooldownSnapshot"/> is never called (it is the logging stub). Entries follow the store's slot
    /// (class-skill-list) order; a locked slot carries expiry 0, and slots past the class's skill count are padded with
    /// <c>(0, 0)</c>. An entity with no store record gets 10 zero entries; the warning for that is logged once per
    /// instance. A client the outbox does not know is skipped silently. Never throws: a fault in either method is logged
    /// once per method. Tick thread only.
    /// </remarks>
    public sealed class SkillCooldownSnapshotResponder : IZoneEntryResponder
    {
        private const string LOG_PREFIX = "[SkillCooldownSnapshotResponder]";

        private readonly IZoneEntryResponder _inner;
        private readonly SkillSystemService _skills;
        private readonly ClientMessageOutbox _outbox;
        private bool _loggedFault;
        private bool _loggedReadyFault;
        private bool _loggedNoRecord;

        /// <summary>Creates the decorator.</summary>
        /// <param name="inner">The wrapped responder; receives <c>SendSessionReady</c>.</param>
        /// <param name="skills">The skill store the snapshot is built from.</param>
        /// <param name="outbox">The priority-path outbox.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public SkillCooldownSnapshotResponder(IZoneEntryResponder inner, SkillSystemService skills, ClientMessageOutbox outbox)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public void SendSessionReady(uint clientId)
        {
            try
            {
                _inner.SendSessionReady(clientId);
            }
            catch (Exception ex)
            {
                if (!_loggedReadyFault)
                {
                    _loggedReadyFault = true;
                    Debug.LogError($"{LOG_PREFIX} SendSessionReadyThrew: {ex.GetType().Name}: {ex.Message} (logged once).");
                }
            }
        }

        /// <inheritdoc />
        public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId)
        {
            try
            {
                if (!_outbox.TryGetSequenceCounter(clientId, out _))
                {
                    return;
                }

                SkillCooldownSnapshot message = default;
                if (_skills.HasRecord(entityId))
                {
                    // The store already caps a record at MAX_SKILLS_PER_ENTITY (10); the clamp only guards SetEntry.
                    int count = Math.Min(_skills.GetSkillCount(entityId), SkillCooldownSnapshot.EntryCount);
                    for (int i = 0; i < count; i++)
                    {
                        if (_skills.TryGetSkill(entityId, i, out SkillInstance skill))
                        {
                            uint expiry = skill.IsUnlocked ? skill.CooldownExpiryTick : 0u;
                            message.SetEntry(i, new SkillCooldownEntry(skill.SkillID.Value, expiry));
                        }
                    }
                }
                else if (!_loggedNoRecord)
                {
                    _loggedNoRecord = true;
                    Debug.LogWarning($"{LOG_PREFIX} no skill store for {entityId} (client {clientId}); sending an all-zero SkillCooldownSnapshot.");
                }

                Span<byte> body = stackalloc byte[SkillCooldownSnapshot.BodySize];
                int written = SkillCooldownSnapshotCodec.WriteBody(body, in message);
                _outbox.Enqueue(clientId, SkillCooldownSnapshot.MessageTypeId, body.Slice(0, written), isCapExempt: false);
            }
            catch (Exception ex)
            {
                if (!_loggedFault)
                {
                    _loggedFault = true;
                    Debug.LogError($"{LOG_PREFIX} SendSkillCooldownSnapshotThrew: {ex.GetType().Name}: {ex.Message} (logged once).");
                }
            }
        }
    }
}
