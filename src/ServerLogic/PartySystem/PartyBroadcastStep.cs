using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.PartySystem;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The outbound-phase party state broadcast (design/gdd/party-system.md CR-PS-9 and Visual/Audio signals;
    /// networking-wire-protocol.md <c>PartyStateUpdate</c>; TR-party-011; Party System Story 008, ADR-014).
    /// <see cref="Flush"/> runs once per tick after <see cref="PartyMembershipStep.ApplyQueued"/>: every real party
    /// that is not Disbanding and whose <c>SequenceId</c> differs from the last value sent for it gets one full
    /// snapshot, sent to each member that is Online or Dead (L3). Re-soloed players (requested through
    /// <see cref="RequestSoloUpdate"/>) get their solo snapshot in the same flush (L5).
    /// </summary>
    /// <remarks>
    /// <c>PartyDisbanded</c> is sent at once by the membership step, while snapshots only go out here, so a
    /// recipient always sees PartyDisbanded before its solo update. The message carries the registry's current
    /// <c>SequenceId</c> (L4). Steady state allocates nothing: buffers are sized up front and only grow when the
    /// party count outgrows them. Tick thread only.
    /// </remarks>
    public sealed class PartyBroadcastStep
    {
        /// <summary>Log prefix.</summary>
        public const string LOG_PREFIX = "[PartyBroadcast] ";

        private const int INITIAL_PARTY_BUFFER = 16;
        private const int MAX_LEVEL_BYTE = 255;

        private readonly PartyRegistry _registry;
        private readonly IPartyMemberLevelSource _levels;
        private readonly PartyXpEligibility _eligibility;
        private readonly IPartyStateMessageSink _sink;
        private readonly Dictionary<PartyID, uint> _lastSent = new Dictionary<PartyID, uint>(INITIAL_PARTY_BUFFER);
        private readonly List<PartyID> _forget = new List<PartyID>(INITIAL_PARTY_BUFFER);
        private readonly List<CharacterID> _soloRequests = new List<CharacterID>(INITIAL_PARTY_BUFFER);
        private PartyID[] _ids = new PartyID[INITIAL_PARTY_BUFFER];

        /// <summary>Creates the step.</summary>
        /// <param name="registry">Party and member reads.</param>
        /// <param name="levels">Level reads for the slots.</param>
        /// <param name="eligibility">The <c>xpEligible</c> query.</param>
        /// <param name="sink">Outbound messages.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PartyBroadcastStep(PartyRegistry registry, IPartyMemberLevelSource levels,
            PartyXpEligibility eligibility, IPartyStateMessageSink sink)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
            _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>
        /// Asks for a solo <c>PartyStateUpdate</c> for a character at the next <see cref="Flush"/> (a leaver or a
        /// re-soloed member). Duplicate requests in one tick collapse; the request is dropped at flush time when the
        /// character has since joined a real party (that party's broadcast covers it).
        /// </summary>
        /// <param name="character">The character.</param>
        public void RequestSoloUpdate(CharacterID character)
        {
            if (!_soloRequests.Contains(character))
            {
                _soloRequests.Add(character);
            }
        }

        /// <summary>Sends this tick's party snapshots. Call once per tick.</summary>
        public void Flush()
        {
            try
            {
                BroadcastRealParties();
                ForgetFreedParties();
                for (int i = 0; i < _soloRequests.Count; i++)
                {
                    SendSolo(_soloRequests[i]);
                }
            }
            finally
            {
                _soloRequests.Clear();
            }
        }

        /// <summary>Whether a member receives party messages in this zone: Online or Dead (L3).</summary>
        /// <param name="member">The member.</param>
        /// <returns>True for Online or Dead.</returns>
        internal bool IsConnected(CharacterID member)
        {
            PartyMemberStatus status = _registry.GetMemberStatus(member);
            return status == PartyMemberStatus.Online || status == PartyMemberStatus.Dead;
        }

        private void BroadcastRealParties()
        {
            if (_registry.PartyCount > _ids.Length)
            {
                _ids = new PartyID[_registry.PartyCount * 2];
            }

            int count = _registry.CopyRealPartyIds(_ids);
            for (int i = 0; i < count; i++)
            {
                if (!_registry.TryGetParty(_ids[i], out PartyState party)
                    || party.State == PartyLifecycleState.Disbanding)
                {
                    continue;
                }

                if (_lastSent.TryGetValue(_ids[i], out uint last) && last == party.SequenceId)
                {
                    continue;
                }

                SendToMembers(party);
                _lastSent[_ids[i]] = party.SequenceId;
            }
        }

        private void ForgetFreedParties()
        {
            if (_lastSent.Count == 0)
            {
                return;
            }

            _forget.Clear();
            foreach (KeyValuePair<PartyID, uint> pair in _lastSent)
            {
                if (!_registry.TryGetParty(pair.Key, out _))
                {
                    _forget.Add(pair.Key);
                }
            }

            for (int i = 0; i < _forget.Count; i++)
            {
                _lastSent.Remove(_forget[i]);
            }
        }

        private void SendSolo(CharacterID character)
        {
            if (_registry.TryGetParty(_registry.GetPartyID(character), out PartyState party) && party.IsSoloParty)
            {
                SendToMembers(party);
            }
        }

        private void SendToMembers(PartyState party)
        {
            PartyStateUpdate update = Build(party);
            for (int i = 0; i < party.MemberCount; i++)
            {
                CharacterID member = party.GetMemberAtIndex(i);
                if (IsConnected(member))
                {
                    _sink.SendStateUpdate(member, in update);
                }
            }
        }

        private PartyStateUpdate Build(PartyState party)
        {
            return new PartyStateUpdate(party.PartyId.RawValue, party.SequenceId, party.IsSoloParty,
                (byte)party.MemberCount, (byte)party.LeaderIndex, party.RrNextIndex,
                Slot(party, 0), Slot(party, 1), Slot(party, 2), Slot(party, 3));
        }

        private PartyMemberSlot Slot(PartyState party, int index)
        {
            if (index >= party.MemberCount)
            {
                return default;
            }

            CharacterID member = party.GetMemberAtIndex(index);
            EntityID entity = party.GetEntityAtIndex(index);
            _levels.TryGetLevel(entity, out int level);
            return new PartyMemberSlot(member.RawValue, entity.RawValue,
                (byte)Mathf.Clamp(level, 0, MAX_LEVEL_BYTE), _registry.GetMemberStatus(member),
                _eligibility.IsXpEligible(party.PartyId, member));
        }
    }
}
