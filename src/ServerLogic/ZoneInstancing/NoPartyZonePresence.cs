using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Stand-in <see cref="IPartyZonePresence"/> for a build without a Party System: nobody is in a party, and the
    /// first lookup per instance logs that party zone presence is not implemented (Zone Instancing Story 010).
    /// </summary>
    public sealed class NoPartyZonePresence : IPartyZonePresence
    {
        /// <summary>The line logged on the first lookup.</summary>
        public const string NOT_IMPLEMENTED_MESSAGE = "[ZoneServer] party zone presence not implemented";

        private bool _logged;

        /// <inheritdoc/>
        public bool TryGetFormalParty(CharacterID character, out PartyID party)
        {
            if (!_logged)
            {
                _logged = true;
                Debug.LogWarning(NOT_IMPLEMENTED_MESSAGE);
            }

            party = PartyID.Uninitialized;
            return false;
        }

        /// <inheritdoc/>
        public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone)
        {
        }

        /// <inheritdoc/>
        public void NotifyZoneExit(PartyID party, EntityID entityId)
        {
        }
    }
}
