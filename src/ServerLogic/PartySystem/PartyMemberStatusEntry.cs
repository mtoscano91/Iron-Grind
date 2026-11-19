using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.PartySystem;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// One member slot as <see cref="PartyRegistry.GetPartyMembersWithStatus"/> reports it (Party System Story 004).
    /// An empty slot is CharacterID 0 / <c>EntityID.Invalid</c> / <see cref="PartyMemberStatus.Online"/>.
    /// </summary>
    public readonly struct PartyMemberStatusEntry
    {
        /// <summary>Builds an entry.</summary>
        /// <param name="character">The member, or <c>CharacterID.Invalid</c> for an empty slot.</param>
        /// <param name="entity">The member's entity, or <c>EntityID.Invalid</c> for an empty slot.</param>
        /// <param name="status">The member's status.</param>
        public PartyMemberStatusEntry(CharacterID character, EntityID entity, PartyMemberStatus status)
        {
            CharacterID = character;
            EntityID = entity;
            Status = status;
        }

        /// <summary>The member, or <c>CharacterID.Invalid</c> for an empty slot.</summary>
        public CharacterID CharacterID { get; }

        /// <summary>The member's entity, or <c>EntityID.Invalid</c> for an empty slot.</summary>
        public EntityID EntityID { get; }

        /// <summary>The member's status.</summary>
        public PartyMemberStatus Status { get; }
    }
}
