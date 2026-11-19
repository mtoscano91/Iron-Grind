using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.DeathAndRespawn;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The Death and Respawn seam into the Party System (CR-DR-5, CR-DR-12, AC-DR-13; Party System Story 004).
    /// Dead / Online are written to the registry; the member's slot is never touched. An unknown entity or an
    /// out-of-range status is a silent no-op.
    /// </summary>
    public sealed class PartyMemberStatusSink : IPartyMemberStatusSink
    {
        private readonly PartyRegistry _registry;

        /// <summary>Creates the sink.</summary>
        /// <param name="registry">The zone's party registry.</param>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
        public PartyMemberStatusSink(PartyRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status)
        {
            PartyMemberStatus mapped;
            switch (status)
            {
                case PartyMemberDeathStatus.Dead:
                    mapped = PartyMemberStatus.Dead;
                    break;
                case PartyMemberDeathStatus.Online:
                    mapped = PartyMemberStatus.Online;
                    break;
                default:
                    return;
            }

            if (_registry.TryGetCharacter(entity, out CharacterID character))
            {
                _registry.SetMemberStatus(character, mapped);
            }
        }
    }
}
