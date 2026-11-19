using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Applies queued party leave and disband requests and runs disband cleanup (design/gdd/party-system.md CR-PS-9
    /// and States and Transitions; TR-party-009; Party System Story 008; ADR-014). The inbound handlers only
    /// <see cref="EnqueueLeave"/> / <see cref="EnqueueDisband"/>; <see cref="ApplyQueued"/> runs after the simulation
    /// phase (a kill and a leave in one tick still pays the leaver, Story 005 G3) and before
    /// <see cref="PartyBroadcastStep.Flush"/> (Story 011 places both).
    /// </summary>
    /// <remarks>
    /// Message order: <c>PartyDisbanded</c> is sent at once from here; solo <c>PartyStateUpdate</c>s are only
    /// requested from the broadcast step and go out at its flush, so a recipient always sees PartyDisbanded first.
    /// Decisions: (L1) a party that drops to one member is disbanded at once. A member who leaves a Disbanding party
    /// gets its solo id and update at once; if that leaves one member, the cleanup finishes now (no second
    /// PartyDisbanded: the first already went out). Deviation: the GDD mentions disband acknowledgements but no such
    /// message exists, so cleanup is timeout-only (<c>DisbandTimeoutTicks</c>). Same-tick leave and disband from one
    /// leader are applied in arrival order: leave then disband, the disband is ignored (the leader is solo already);
    /// disband then leave, the party is Disbanding and the leader leaves it at once (solo id and update now), and if
    /// that leaves one member the cleanup finishes at once. Tick thread only.
    /// </remarks>
    public sealed class PartyMembershipStep
    {
        /// <summary>Log prefix.</summary>
        public const string LOG_PREFIX = "[PartyMembership] ";

        /// <summary>Capacity of the request queue; further requests in one tick are dropped with one warning.</summary>
        public const int REQUEST_CAPACITY = 256;

        private const int INITIAL_CLEANUP_CAPACITY = 16;

        private struct Request
        {
            public CharacterID Character;
            public bool IsDisband;
        }

        private struct Cleanup
        {
            public PartyID Party;
            public uint DueTick;
        }

        private readonly PartyRegistry _registry;
        private readonly IPartyStateMessageSink _stateSink;
        private readonly IPartyInviteMessageSink _inviteSink;
        private readonly PartyBroadcastStep _broadcast;
        private readonly uint _timeoutTicks;
        private readonly Request[] _requests = new Request[REQUEST_CAPACITY];
        private readonly List<Cleanup> _cleanups = new List<Cleanup>(INITIAL_CLEANUP_CAPACITY);
        private int _requestCount;
        private bool _loggedOverflow;

        /// <summary>Creates the step.</summary>
        /// <param name="registry">Membership writes.</param>
        /// <param name="stateSink">Receives <c>PartyDisbanded</c>.</param>
        /// <param name="inviteSink">Receives the non-leader <c>PartyInviteResult</c>.</param>
        /// <param name="broadcast">Receives the solo update requests.</param>
        /// <param name="config">The party knobs (DisbandTimeoutTicks); must pass <c>Validate()</c>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException">The config failed validation.</exception>
        public PartyMembershipStep(PartyRegistry registry, IPartyStateMessageSink stateSink,
            IPartyInviteMessageSink inviteSink, PartyBroadcastStep broadcast, PartySystemConfigValues config)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _stateSink = stateSink ?? throw new ArgumentNullException(nameof(stateSink));
            _inviteSink = inviteSink ?? throw new ArgumentNullException(nameof(inviteSink));
            _broadcast = broadcast ?? throw new ArgumentNullException(nameof(broadcast));
            if (!config.Validate())
            {
                throw new ArgumentException("The party system config failed validation (see the logged errors).",
                    nameof(config));
            }

            _timeoutTicks = (uint)config.DisbandTimeoutTicks;
        }

        /// <summary>The number of requests waiting for <see cref="ApplyQueued"/>.</summary>
        public int QueuedCount => _requestCount;

        /// <summary>The number of Disbanding parties waiting for their cleanup tick.</summary>
        public int PendingCleanupCount => _cleanups.Count;

        /// <summary>Queues a leave request (called from the dispatch phase).</summary>
        /// <param name="character">The requesting character.</param>
        public void EnqueueLeave(CharacterID character)
        {
            Enqueue(character, false);
        }

        /// <summary>Queues a disband request (called from the dispatch phase).</summary>
        /// <param name="character">The requesting character.</param>
        public void EnqueueDisband(CharacterID character)
        {
            Enqueue(character, true);
        }

        /// <summary>Applies the queued requests in arrival order, then runs the cleanups that are due.</summary>
        /// <param name="currentTick">The current server tick.</param>
        public void ApplyQueued(uint currentTick)
        {
            int count = _requestCount;
            _requestCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (_requests[i].IsDisband)
                {
                    ApplyDisband(_requests[i].Character, currentTick);
                }
                else
                {
                    ApplyLeave(_requests[i].Character);
                }
            }

            RunCleanups(currentTick);
        }

        private void Enqueue(CharacterID character, bool isDisband)
        {
            for (int i = 0; i < _requestCount; i++)
            {
                if (_requests[i].Character == character && _requests[i].IsDisband == isDisband)
                {
                    return;
                }
            }

            if (_requestCount >= REQUEST_CAPACITY)
            {
                if (!_loggedOverflow)
                {
                    _loggedOverflow = true;
                    Debug.LogWarning(LOG_PREFIX + "request queue full; dropping requests (logged once).");
                }

                return;
            }

            _requests[_requestCount].Character = character;
            _requests[_requestCount].IsDisband = isDisband;
            _requestCount++;
        }

        private void ApplyLeave(CharacterID character)
        {
            PartyID partyId = _registry.GetPartyID(character);
            if (!_registry.TryGetParty(partyId, out PartyState party) || party.IsSoloParty)
            {
                return;
            }

            bool wasDisbanding = party.State == PartyLifecycleState.Disbanding;
            PartyRegistryResult removed = _registry.RemoveMember(partyId, character, out _);
            if (removed != PartyRegistryResult.Ok)
            {
                Debug.LogError(LOG_PREFIX + "leave of " + character + " from " + partyId + " failed: " + removed + ".");
                return;
            }

            _registry.ClearPresenceFlags(character);
            _broadcast.RequestSoloUpdate(character);
            if (party.MemberCount != 1)
            {
                return;
            }

            FinishAutoDisband(partyId, party.GetMemberAtIndex(0), wasDisbanding);
        }

        /// <summary>
        /// The "one member left" tail shared by <c>ApplyLeave</c> and the slot release listener (Story 009): sends
        /// <c>PartyDisbanded</c> to the remaining member when connected (unless the party was already Disbanding),
        /// disbands the party and requests the remaining member's solo update.
        /// </summary>
        /// <param name="partyId">The party that is down to one member.</param>
        /// <param name="remaining">The remaining member.</param>
        /// <param name="wasDisbanding">True when the party was Disbanding (the first notice already went out).</param>
        internal void FinishAutoDisband(PartyID partyId, CharacterID remaining, bool wasDisbanding)
        {
            if (!wasDisbanding)
            {
                SendDisbandedIfConnected(remaining, partyId);
            }

            if (_registry.DisbandParty(partyId) == PartyRegistryResult.Ok)
            {
                _registry.ClearPresenceFlags(remaining);
                _broadcast.RequestSoloUpdate(remaining);
            }
            else
            {
                Debug.LogError(LOG_PREFIX + "auto-disband of " + partyId + " failed (ids exhausted).");
            }
        }

        private void ApplyDisband(CharacterID character, uint currentTick)
        {
            PartyID partyId = _registry.GetPartyID(character);
            if (!_registry.TryGetParty(partyId, out PartyState party) || party.IsSoloParty
                || party.State == PartyLifecycleState.Disbanding)
            {
                return;
            }

            if (party.GetMemberAtIndex(party.LeaderIndex) != character)
            {
                _inviteSink.SendInviteResult(character, PartyInviteResultCode.NotLeader, CharacterID.Invalid);
                return;
            }

            _registry.BeginDisband(partyId);
            for (int i = 0; i < party.MemberCount; i++)
            {
                SendDisbandedIfConnected(party.GetMemberAtIndex(i), partyId);
            }

            _cleanups.Add(new Cleanup { Party = partyId, DueTick = unchecked(currentTick + _timeoutTicks) });
        }

        private void RunCleanups(uint currentTick)
        {
            for (int i = _cleanups.Count - 1; i >= 0; i--)
            {
                Cleanup cleanup = _cleanups[i];
                if (!_registry.TryGetParty(cleanup.Party, out PartyState party)
                    || party.State != PartyLifecycleState.Disbanding)
                {
                    _cleanups.RemoveAt(i);
                }
                else if (unchecked((int)(currentTick - cleanup.DueTick)) >= 0)
                {
                    _cleanups.RemoveAt(i);
                    FinishDisband(cleanup.Party, party);
                }
            }
        }

        private void FinishDisband(PartyID partyId, PartyState party)
        {
            int count = party.MemberCount;
            Span<CharacterID> members = stackalloc CharacterID[PartyStateUpdate.SlotCount];
            for (int i = 0; i < count; i++)
            {
                members[i] = party.GetMemberAtIndex(i);
            }

            if (_registry.DisbandParty(partyId) != PartyRegistryResult.Ok)
            {
                Debug.LogError(LOG_PREFIX + "cleanup of " + partyId + " failed (ids exhausted).");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                _registry.ClearPresenceFlags(members[i]);
                _broadcast.RequestSoloUpdate(members[i]);
            }
        }

        private void SendDisbandedIfConnected(CharacterID member, PartyID partyId)
        {
            if (_broadcast.IsConnected(member))
            {
                _stateSink.SendDisbanded(member, partyId);
            }
        }
    }
}
