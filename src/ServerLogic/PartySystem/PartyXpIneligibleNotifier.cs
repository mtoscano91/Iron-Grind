using System;
using IronGrind.Currency;
using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Sends the XP-ineligible notice after a leader change (CR-PS-5; Party System Story 003 AC-7): snapshots each
    /// member's eligibility before the change and notifies members that went from eligible to ineligible. Members
    /// already ineligible, the new leader's own entry (eligible vs itself) and a departed leader get no notice.
    /// The production <see cref="IPartyXpIneligibleNoticeSink"/> adapter is Story 011.
    /// </summary>
    /// <remarks>
    /// Null constructor arguments throw. "No sink" (story G1) means no notifier is constructed. Constructing one
    /// registers it as the registry's single leader-change listener, replacing any existing listener. Not
    /// reentrant: the sink must not mutate the registry.
    /// </remarks>
    public sealed class PartyXpIneligibleNotifier : IPartyLeaderChangeListener
    {
        private readonly PartyRegistry _registry;
        private readonly PartyXpEligibility _eligibility;
        private readonly IPartyXpIneligibleNoticeSink _sink;
        private readonly CharacterID[] _snapshotMembers;
        private readonly bool[] _snapshotEligible;
        private int _snapshotCount;

        /// <summary>Creates the notifier and registers it as the registry's leader-change listener.</summary>
        /// <param name="registry">The zone's party registry.</param>
        /// <param name="eligibility">The XP eligibility query.</param>
        /// <param name="sink">Where notices go.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PartyXpIneligibleNotifier(PartyRegistry registry, PartyXpEligibility eligibility,
            IPartyXpIneligibleNoticeSink sink)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _snapshotMembers = new CharacterID[registry.MaxPartySize];
            _snapshotEligible = new bool[registry.MaxPartySize];
            registry.SetLeaderChangeListener(this);
        }

        /// <inheritdoc/>
        public void OnLeaderChanging(PartyID partyId)
        {
            _snapshotCount = 0;
            if (!_registry.TryGetParty(partyId, out PartyState party))
            {
                return;
            }

            int count = Math.Min(party.MemberCount, _snapshotMembers.Length);
            for (int i = 0; i < count; i++)
            {
                CharacterID member = party.GetMemberAtIndex(i);
                _snapshotMembers[i] = member;
                _snapshotEligible[i] = _eligibility.IsXpEligible(partyId, member);
            }

            _snapshotCount = count;
        }

        /// <inheritdoc/>
        public void OnLeaderChanged(PartyID partyId)
        {
            if (!_registry.TryGetParty(partyId, out PartyState party))
            {
                _snapshotCount = 0;
                return;
            }

            for (int i = 0; i < _snapshotCount; i++)
            {
                CharacterID member = _snapshotMembers[i];
                if (_snapshotEligible[i] && party.IndexOf(member) >= 0 && !_eligibility.IsXpEligible(partyId, member))
                {
                    _sink.Notify(member, partyId);
                }
            }

            _snapshotCount = 0;
        }
    }
}
