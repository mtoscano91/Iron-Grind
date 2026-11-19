using System;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.PartySystem;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// XP eligibility of a party member (CR-PS-5; TR-party-010; Party System Story 004): a member of the party, Online,
    /// and within <c>LEVEL_RANGE_GATE</c> levels (inclusive) of the party's current leader. Parties are zone-local at
    /// MVP, so "same zone" is covered by the Online check. Queries allocate nothing.
    /// </summary>
    public sealed class PartyXpEligibility
    {
        private readonly PartyRegistry _registry;
        private readonly IPartyMemberLevelSource _levels;
        private readonly int _levelRangeGate;

        /// <summary>Creates the query.</summary>
        /// <param name="registry">The zone's party registry.</param>
        /// <param name="levels">The level reader.</param>
        /// <param name="config">The Party System knobs (LEVEL_RANGE_GATE).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public PartyXpEligibility(PartyRegistry registry, IPartyMemberLevelSource levels,
            PartySystemConfigValues config)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
            _levelRangeGate = config.LevelRangeGate;
        }

        /// <summary>Whether a character earns party XP.</summary>
        /// <param name="partyId">The party.</param>
        /// <param name="character">The character.</param>
        /// <returns>
        /// True when the character is a member of that party, Online, and its level is within the gate of the leader's.
        /// False for an unknown party or non-member, or when either level is unreadable.
        /// </returns>
        public bool IsXpEligible(PartyID partyId, CharacterID character)
        {
            if (!_registry.TryGetParty(partyId, out PartyState party))
            {
                return false;
            }

            int index = party.IndexOf(character);
            if (index < 0 || _registry.GetMemberStatus(character) != PartyMemberStatus.Online)
            {
                return false;
            }

            if (party.LeaderIndex < 0 || party.LeaderIndex >= party.MemberCount)
            {
                return false;
            }

            if (!_levels.TryGetLevel(party.GetEntityAtIndex(party.LeaderIndex), out int leaderLevel)
                || !_levels.TryGetLevel(party.GetEntityAtIndex(index), out int memberLevel))
            {
                return false;
            }

            return Math.Abs((long)memberLevel - leaderLevel) <= _levelRangeGate;
        }
    }
}
