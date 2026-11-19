using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.PartySystem;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Per-tick disconnect watcher for real-party members (design/gdd/party-system.md CR-PS-8, CR-PS-11; Party System
    /// Story 009 AC-1..AC-3, AC-7, D2, D3, D5). A member first seen disconnected at tick T stays Online until
    /// T + <c>ReconnectGraceTicks</c>, then becomes Ghost: its invites are cancelled and, if it led the party,
    /// leadership moves in the same call. A Ghost whose session is Connected again loses the ghost flag (no
    /// leadership restore). Solo players are not polled. Tick thread only; no per-tick allocation in steady state.
    /// </summary>
    public sealed class PartyPresenceStep
    {
        /// <summary>Log prefix.</summary>
        public const string LOG_PREFIX = "[PartyPresence] ";

        private const int INITIAL_CAPACITY = 64;

        private readonly PartyRegistry _registry;
        private readonly IPartySessionStateSource _source;
        private readonly PartyInviteService _invites;
        private readonly uint _graceTicks;
        private readonly Dictionary<CharacterID, uint> _since = new Dictionary<CharacterID, uint>(INITIAL_CAPACITY);
        private readonly List<CharacterID> _forget = new List<CharacterID>(INITIAL_CAPACITY);
        private PartyID[] _ids = new PartyID[INITIAL_CAPACITY];

        /// <summary>Creates the step.</summary>
        /// <param name="registry">Membership and flag writes.</param>
        /// <param name="source">Session state reads.</param>
        /// <param name="invites">Invite cancellation at Ghost.</param>
        /// <param name="config">The party knobs (ReconnectGraceTicks); must pass <c>Validate()</c>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException">The config failed validation.</exception>
        public PartyPresenceStep(PartyRegistry registry, IPartySessionStateSource source, PartyInviteService invites,
            PartySystemConfigValues config)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _invites = invites ?? throw new ArgumentNullException(nameof(invites));
            if (!config.Validate())
            {
                throw new ArgumentException("The party system config failed validation (see the logged errors).",
                    nameof(config));
            }

            _graceTicks = (uint)config.ReconnectGraceTicks;
        }

        /// <summary>The number of members with a running grace timer.</summary>
        public int TrackedCount => _since.Count;

        /// <summary>Drops a member's grace record (slot released). Unknown member: no-op.</summary>
        /// <param name="character">The character.</param>
        public void Forget(CharacterID character)
        {
            _since.Remove(character);
        }

        /// <summary>Reads every real-party member's session state and applies grace, Ghost and reconnect.</summary>
        /// <param name="currentTick">The current server tick.</param>
        public void Poll(uint currentTick)
        {
            if (_registry.PartyCount > _ids.Length)
            {
                _ids = new PartyID[_registry.PartyCount * 2];
            }

            int count = _registry.CopyRealPartyIds(_ids);
            for (int p = 0; p < count; p++)
            {
                if (!_registry.TryGetParty(_ids[p], out PartyState party))
                {
                    continue;
                }

                for (int i = 0; i < party.MemberCount; i++)
                {
                    try
                    {
                        PollMember(party, party.GetMemberAtIndex(i), party.GetEntityAtIndex(i), currentTick);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        Debug.LogError(LOG_PREFIX + "polling member " + i + " of " + party.PartyId
                            + " threw; the rest of the poll continues.");
                    }
                }
            }

            DropStaleRecords();
        }

        private void PollMember(PartyState party, CharacterID member, EntityID entity, uint currentTick)
        {
            if (!_source.TryIsDisconnected(entity, out bool disconnected))
            {
                return;
            }

            if (!disconnected)
            {
                if (_since.Count > 0)
                {
                    _since.Remove(member);
                }

                if (_registry.GetMemberStatus(member) == PartyMemberStatus.Ghost)
                {
                    _registry.SetGhost(member, false);
                }

                // A connected member makes a Ghost leader replaceable (the "all others were Ghost" case).
                CharacterID leader = party.GetMemberAtIndex(party.LeaderIndex);
                if (leader != member && _registry.GetMemberStatus(leader) == PartyMemberStatus.Ghost
                    && _registry.GetMemberStatus(member) != PartyMemberStatus.Ghost)
                {
                    _registry.TransferLeadership(party.PartyId);
                }

                return;
            }

            if (!_since.TryGetValue(member, out uint since))
            {
                since = currentTick;
                _since[member] = since;
            }

            bool graceOver = unchecked((int)(currentTick - since - _graceTicks)) >= 0;
            if (graceOver && _registry.GetMemberStatus(member) != PartyMemberStatus.Ghost)
            {
                BecomeGhost(party, member);
            }
        }

        private void BecomeGhost(PartyState party, CharacterID member)
        {
            bool wasLeader = party.GetMemberAtIndex(party.LeaderIndex) == member;
            _registry.SetGhost(member, true);
            if (wasLeader)
            {
                _registry.TransferLeadership(party.PartyId);
            }

            // Last: a throwing invite sink must not undo the Ghost flag or the leader transfer.
            _invites.CancelFor(member);
        }

        /// <summary>Removes the records of members that are no longer in a real party.</summary>
        private void DropStaleRecords()
        {
            if (_since.Count == 0)
            {
                return;
            }

            _forget.Clear();
            foreach (KeyValuePair<CharacterID, uint> pair in _since)
            {
                if (!_registry.TryGetParty(_registry.GetPartyID(pair.Key), out PartyState party) || party.IsSoloParty)
                {
                    _forget.Add(pair.Key);
                }
            }

            for (int i = 0; i < _forget.Count; i++)
            {
                _since.Remove(_forget[i]);
            }
        }
    }
}
