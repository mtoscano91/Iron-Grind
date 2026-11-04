using System;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Production <see cref="ISkillCastPartySource"/> (Skill System Story 009): maps each <see cref="EntityID"/> to its
    /// <c>CharacterID</c> through the zone pool, then compares the formal parties reported by
    /// <see cref="IPartyZonePresence"/>. With the <c>NoPartyZonePresence</c> stand-in nobody is in a party, so the
    /// answer is always false until the Party System lands.
    /// </summary>
    /// <remarks>Tick thread only. Allocation-free.</remarks>
    public sealed class ZoneSkillCastPartySource : ISkillCastPartySource
    {
        private readonly ZoneSlotPool _pool;
        private readonly IPartyZonePresence _presence;

        /// <summary>Creates the adapter.</summary>
        /// <param name="pool">The zone's slot pool; maps players to characters.</param>
        /// <param name="presence">Party lookup by character.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ZoneSkillCastPartySource(ZoneSlotPool pool, IPartyZonePresence presence)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        }

        /// <inheritdoc />
        public bool AreInSameParty(EntityID a, EntityID b)
        {
            if (!_pool.TryGetPlayer(a, out ZonePlayerSlot slotA) || !_pool.TryGetPlayer(b, out ZonePlayerSlot slotB))
            {
                return false;
            }

            if (!_presence.TryGetFormalParty(slotA.Character, out PartyID partyA)
                || !_presence.TryGetFormalParty(slotB.Character, out PartyID partyB))
            {
                return false;
            }

            return partyA.Equals(partyB);
        }
    }
}
