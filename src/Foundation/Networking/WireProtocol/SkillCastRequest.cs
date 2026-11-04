namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request to cast a skill (skill-system.md CR-SK-1; networking-wire-protocol.md
    /// SkillCastRequest; Skill System Story 004). U-U: a dropped request is not retransmitted, the client may
    /// re-submit next beat.
    /// </summary>
    /// <remarks>
    /// Wire layout (little-endian, 16-byte body): <c>uint casterEntityID</c>, <c>uint skillID</c>,
    /// <c>uint targetEntityID</c>, <c>uint clientTickNumber</c>. The fields are raw <c>uint</c> values, as in the other
    /// wire structs; the server validates them (V-1 caster, V-2 staleness, V-3..V-8 in phase 3). Codec:
    /// <see cref="SkillCastRequestCodec"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var request = new SkillCastRequest(casterEntityId: 1001, skillId: 7, targetEntityId: 2001, clientTickNumber: 500);
    /// Span&lt;byte&gt; body = stackalloc byte[SkillCastRequest.BodySize];
    /// SkillCastRequestCodec.WriteBody(body, request);
    /// </code>
    /// </example>
    public readonly struct SkillCastRequest
    {
        /// <summary>Wire size of the body: 16 bytes.</summary>
        public const int BodySize = 16;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="SkillCastRequest"/> (skill block 0x0308-0x030A: request U-U; result and cooldown update R-U).</summary>
        public const ushort MessageTypeId = 0x0308;

        /// <summary>The caster claimed by the client; must equal the connection's sender entity (V-1).</summary>
        public readonly uint CasterEntityId;

        /// <summary>The skill to cast; 0 is invalid and rejected at V-3.</summary>
        public readonly uint SkillId;

        /// <summary>The cast target.</summary>
        public readonly uint TargetEntityId;

        /// <summary>The client's tick when it sent the request; used for the staleness check (V-2).</summary>
        public readonly uint ClientTickNumber;

        /// <summary>Initializes a new <see cref="SkillCastRequest"/>.</summary>
        /// <param name="casterEntityId">Claimed caster entity id.</param>
        /// <param name="skillId">Skill id.</param>
        /// <param name="targetEntityId">Target entity id.</param>
        /// <param name="clientTickNumber">Client tick at send time.</param>
        public SkillCastRequest(uint casterEntityId, uint skillId, uint targetEntityId, uint clientTickNumber)
        {
            CasterEntityId = casterEntityId;
            SkillId = skillId;
            TargetEntityId = targetEntityId;
            ClientTickNumber = clientTickNumber;
        }
    }
}
