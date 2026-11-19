using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Slot release is a leave (Party System Story 009 AC-4, D6): drops the player's invites, unregisters it and,
    /// when one member is left in the former real party, runs the Story 008 auto-disband at once. Registry
    /// removal already moves leadership (CR-PS-8: lowest non-Ghost, else index 0), so no second transfer here.
    /// Unknown entity: no-op. Composed into the release composite by Story 011.
    /// </summary>
    public sealed class PartySlotReleaseListener : IZoneSlotReleaseListener
    {
        private readonly PartyRegistry _registry;
        private readonly PartyInviteService _invites;
        private readonly PartyMembershipStep _membership;
        private readonly PartyPresenceStep _presence;

        /// <summary>Creates the listener.</summary>
        /// <param name="registry">Membership writes.</param>
        /// <param name="invites">Invite cancellation.</param>
        /// <param name="membership">The shared auto-disband tail.</param>
        /// <param name="presence">Grace record cleanup.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PartySlotReleaseListener(PartyRegistry registry, PartyInviteService invites,
            PartyMembershipStep membership, PartyPresenceStep presence)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _invites = invites ?? throw new ArgumentNullException(nameof(invites));
            _membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        }

        /// <inheritdoc/>
        public void BeforeRelease(EntityID id)
        {
            if (!_registry.TryGetCharacter(id, out CharacterID character))
            {
                return;
            }

            // Registry state first, so a throwing invite sink cannot leave the player registered. CancelFor does
            // not need the leaver registered: it reads only the other parties of each invite.
            try
            {
                _presence.Forget(character);
                if (_registry.UnregisterPlayer(character, out PartyID former) == PartyRegistryResult.Ok
                    && former != PartyID.Uninitialized && _registry.TryGetParty(former, out PartyState party)
                    && party.MemberCount == 1)
                {
                    _membership.FinishAutoDisband(former, party.GetMemberAtIndex(0),
                        party.State == PartyLifecycleState.Disbanding);
                }
            }
            finally
            {
                _invites.CancelFor(character);
            }
        }
    }
}
