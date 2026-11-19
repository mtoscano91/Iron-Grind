using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Splits a kill's experience across the killer's party (design/gdd/party-system.md CR-PS-5, CR-PS-10, F-PS-1;
    /// TR-party-005; Party System Story 005). An <see cref="IAutoAttackExperienceSink"/> decorator.
    /// </summary>
    /// <remarks>
    /// <para>G1 (one seam): both kill paths already take <see cref="IAutoAttackExperienceSink"/>, so this wraps the
    /// existing sink (<c>CharacterStatsExperienceSink</c>) and no combat system changes.</para>
    /// <para>G2 (solo pass-through): a killer with no registered character, or in a solo party, gets exactly one
    /// inner call with the unchanged amount; eligibility is not evaluated (a Dead solo killer still gets XP). In a
    /// real party every member, the killer included, goes through <see cref="PartyXpEligibility"/>.</para>
    /// <para>G3 (call-time read): eligibility is read at the call, synchronously inside the kill; there is no
    /// snapshot buffer. A party in <c>Disbanding</c> still awards normally.</para>
    /// <para>G4 (logging): only the zero-eligible line is logged. A per-member award log is deliberately omitted
    /// (hot path); tests verify awards with a recording sink.</para>
    /// <para>Tick thread. Allocates nothing except the rare zero-eligible log line: a count pass records eligibility
    /// once in a bitmask, then an award pass pays from that mask, so an award that levels a member up (or reenters)
    /// cannot change who is paid or the amount. Exceptions from the inner sink propagate; members already paid keep
    /// their XP.</para>
    /// </remarks>
    public sealed class PartyExperienceDistributor : IAutoAttackExperienceSink
    {
        private const string LOG_PREFIX = "[PartyXp] ";

        private readonly IAutoAttackExperienceSink _inner;
        private readonly PartyRegistry _registry;
        private readonly PartyXpEligibility _eligibility;
        private readonly float _rate;

        /// <summary>Creates the distributor.</summary>
        /// <param name="inner">The sink that receives each award (the unwrapped XP sink).</param>
        /// <param name="registry">The zone's party registry.</param>
        /// <param name="eligibility">The XP eligibility query.</param>
        /// <param name="config">The Party System knobs (XP_PARTY_DEDUCTION_RATE).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public PartyExperienceDistributor(IAutoAttackExperienceSink inner, PartyRegistry registry,
            PartyXpEligibility eligibility, PartySystemConfigValues config)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
            _rate = config.XpPartyDeductionRate;
        }

        /// <summary>
        /// Awards a kill's experience: pass-through for a non-party killer, otherwise the F-PS-1 split to each
        /// eligible member in join order.
        /// </summary>
        /// <param name="attacker">The killing player's entity.</param>
        /// <param name="xpBase">The mob's base XP.</param>
        public void AddExperience(EntityID attacker, int xpBase)
        {
            if (!_registry.TryGetCharacter(attacker, out CharacterID killer))
            {
                _inner.AddExperience(attacker, xpBase);
                return;
            }

            PartyID partyId = _registry.GetPartyID(killer);
            if (!_registry.TryGetParty(partyId, out PartyState party) || party.IsSoloParty)
            {
                _inner.AddExperience(attacker, xpBase);
                return;
            }

            // Eligibility is read once, before any award: an award can level a member (the leader included) up, which
            // would change a second IsXpEligible read. MemberCount <= MAX_PARTY_SIZE_MAX (4), so a uint mask fits.
            int count = party.MemberCount;
            uint eligibleMask = 0u;
            int eligible = 0;
            for (int i = 0; i < count; i++)
            {
                if (_eligibility.IsXpEligible(partyId, party.GetMemberAtIndex(i)))
                {
                    eligibleMask |= 1u << i;
                    eligible++;
                }
            }

            if (eligible == 0)
            {
                Debug.Log(LOG_PREFIX + "party " + partyId + ": 0 eligible, 0 XP awarded");
                return;
            }

            // Entities are captured before the first award, so a reentrant membership change cannot shift a slot.
            int amount = PartyXpFormula.Compute(xpBase, eligible, _rate);
            EntityID e0 = party.GetEntityAtIndex(0);
            EntityID e1 = party.GetEntityAtIndex(1);
            EntityID e2 = party.GetEntityAtIndex(2);
            EntityID e3 = party.GetEntityAtIndex(3);
            for (int i = 0; i < count; i++)
            {
                if ((eligibleMask & (1u << i)) != 0u)
                {
                    _inner.AddExperience(i == 0 ? e0 : i == 1 ? e1 : i == 2 ? e2 : e3, amount);
                }
            }
        }
    }
}
