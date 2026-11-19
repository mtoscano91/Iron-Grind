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
    /// The party registry: solo PartyIDs, real parties and membership (design/gdd/party-system.md CR-PS-1, CR-PS-2,
    /// CR-PS-9; TR-party-001, TR-party-002; Party System Story 002). Zone-local: one registry per zone process, so a
    /// per-process counter issues every id (starting at 1, never reused, never 0).
    /// </summary>
    /// <remarks>
    /// Failures return a <see cref="PartyRegistryResult"/> and change no state. Mutations may allocate; queries do
    /// not. Not thread-safe (called from the server tick). Leader transfer, the round-robin cursor and the
    /// IPartyService facade are Story 003. A party's <c>SequenceId</c> also bumps when a member's status value
    /// changes (<see cref="SetMemberStatus"/>), not only on membership changes.
    /// </remarks>
    public sealed class PartyRegistry
    {
        private const string LOG_PREFIX = "[PartyRegistry] ";

        private readonly int _maxPartySize;
        private readonly Dictionary<CharacterID, PartyID> _partyOf = new Dictionary<CharacterID, PartyID>();
        private readonly Dictionary<CharacterID, EntityID> _entityOf = new Dictionary<CharacterID, EntityID>();
        private readonly Dictionary<PartyID, PartyState> _parties = new Dictionary<PartyID, PartyState>();
        private readonly Dictionary<EntityID, CharacterID> _characterOf = new Dictionary<EntityID, CharacterID>();
        private readonly Dictionary<CharacterID, PartyMemberStatus> _statusOf =
            new Dictionary<CharacterID, PartyMemberStatus>();
        private uint _nextPartyId = 1;

        /// <summary>Creates a registry.</summary>
        /// <param name="config">The Party System knobs; must pass <c>Validate()</c>.</param>
        /// <exception cref="ArgumentException">The config failed validation.</exception>
        public PartyRegistry(PartySystemConfigValues config)
        {
            if (!config.Validate())
            {
                throw new ArgumentException("The party system config failed validation (see the logged errors).",
                    nameof(config));
            }

            _maxPartySize = config.MaxPartySize;
        }

        /// <summary>The number of live parties, solo and real.</summary>
        public int PartyCount => _parties.Count;

        /// <summary>
        /// Registers a player and gives them a solo party (state <c>Active</c>: a solo party is stable).
        /// </summary>
        /// <param name="character">The character; must not be <c>CharacterID.Invalid</c>.</param>
        /// <param name="entity">The character's runtime entity; must not be <c>EntityID.Invalid</c>.</param>
        /// <param name="soloPartyId">The new solo party id, or <c>Uninitialized</c> on failure.</param>
        /// <returns>The outcome.</returns>
        public PartyRegistryResult RegisterPlayer(CharacterID character, EntityID entity, out PartyID soloPartyId)
        {
            soloPartyId = PartyID.Uninitialized;
            if (character == CharacterID.Invalid)
            {
                return PartyRegistryResult.InvalidCharacter;
            }

            if (entity == EntityID.Invalid)
            {
                return PartyRegistryResult.InvalidEntity;
            }

            if (_partyOf.ContainsKey(character))
            {
                return PartyRegistryResult.AlreadyRegistered;
            }

            if (_characterOf.ContainsKey(entity))
            {
                return PartyRegistryResult.EntityAlreadyRegistered;
            }

            if (!HasIds(1))
            {
                return PartyRegistryResult.IdsExhausted;
            }

            _entityOf[character] = entity;
            _characterOf[entity] = character;
            _statusOf[character] = PartyMemberStatus.Online;
            soloPartyId = CreateSolo(character);
            return PartyRegistryResult.Ok;
        }

        /// <summary>
        /// Turns a registered, solo character into the leader of a new real party (state <c>Forming</c>, leader at
        /// index 0) and frees their solo party.
        /// </summary>
        /// <param name="leader">The leader.</param>
        /// <param name="partyId">The new party id, or <c>Uninitialized</c> on failure.</param>
        /// <returns>The outcome.</returns>
        public PartyRegistryResult TryCreateParty(CharacterID leader, out PartyID partyId)
        {
            partyId = PartyID.Uninitialized;
            if (leader == CharacterID.Invalid)
            {
                return PartyRegistryResult.InvalidCharacter;
            }

            if (!_partyOf.TryGetValue(leader, out PartyID current))
            {
                return PartyRegistryResult.NotRegistered;
            }

            if (!_parties[current].IsSoloParty)
            {
                return PartyRegistryResult.NotSolo;
            }

            if (!HasIds(1))
            {
                return PartyRegistryResult.IdsExhausted;
            }

            FreeParty(current);
            PartyID id = AllocateId();
            var party = new PartyState(id, false, PartyLifecycleState.Forming, _maxPartySize);
            party.Append(leader, _entityOf[leader]);
            _parties.Add(id, party);
            _partyOf[leader] = id;
            partyId = id;
            return PartyRegistryResult.Ok;
        }

        /// <summary>
        /// Appends a registered, solo character to a real party in join order, frees the joiner's solo party and
        /// moves the party <c>Forming</c> to <c>Active</c> at the second member.
        /// </summary>
        /// <param name="partyId">The target party.</param>
        /// <param name="character">The joiner.</param>
        /// <returns>The outcome. A character that was never registered is reported as <c>NotRegistered</c>.</returns>
        public PartyRegistryResult TryAddMember(PartyID partyId, CharacterID character)
        {
            if (character == CharacterID.Invalid)
            {
                return PartyRegistryResult.InvalidCharacter;
            }

            if (!_parties.TryGetValue(partyId, out PartyState party))
            {
                return PartyRegistryResult.UnknownParty;
            }

            if (party.IsSoloParty)
            {
                return PartyRegistryResult.SoloPartyCannotAccept;
            }

            if (!_partyOf.TryGetValue(character, out PartyID current))
            {
                return PartyRegistryResult.NotRegistered;
            }

            if (!_parties[current].IsSoloParty)
            {
                return PartyRegistryResult.AlreadyInParty;
            }

            if (party.MemberCount >= _maxPartySize)
            {
                return PartyRegistryResult.PartyFull;
            }

            FreeParty(current);
            party.Append(character, _entityOf[character]);
            _partyOf[character] = partyId;
            if (party.State == PartyLifecycleState.Forming && party.MemberCount >= 2)
            {
                party.SetState(PartyLifecycleState.Active);
            }

            return PartyRegistryResult.Ok;
        }

        /// <summary>
        /// Removes a member from a real party with compaction (join order preserved) and gives them a fresh solo
        /// party. Freeing the last member frees the party. Leader index handling is Story 003.
        /// </summary>
        /// <param name="partyId">The real party.</param>
        /// <param name="character">The member to remove.</param>
        /// <param name="newSoloPartyId">The character's new solo party id, or <c>Uninitialized</c> on failure.</param>
        /// <returns>The outcome.</returns>
        public PartyRegistryResult RemoveMember(PartyID partyId, CharacterID character, out PartyID newSoloPartyId)
        {
            newSoloPartyId = PartyID.Uninitialized;
            if (!_parties.TryGetValue(partyId, out PartyState party))
            {
                return PartyRegistryResult.UnknownParty;
            }

            if (party.IsSoloParty)
            {
                return PartyRegistryResult.SoloParty;
            }

            int index = party.IndexOf(character);
            if (index < 0)
            {
                return PartyRegistryResult.NotMember;
            }

            if (!HasIds(1))
            {
                return PartyRegistryResult.IdsExhausted;
            }

            party.RemoveAt(index);
            if (party.MemberCount == 0)
            {
                FreeParty(partyId);
            }

            newSoloPartyId = CreateSolo(character);
            return PartyRegistryResult.Ok;
        }

        /// <summary>
        /// Removes a player from the registry. A solo party is freed; a real-party member is removed with
        /// compaction, gets no new solo party, and the party is freed if it is empty.
        /// </summary>
        /// <param name="character">The character.</param>
        /// <param name="formerRealPartyId">The real party the character left, or <c>Uninitialized</c>.</param>
        /// <returns>The outcome.</returns>
        public PartyRegistryResult UnregisterPlayer(CharacterID character, out PartyID formerRealPartyId)
        {
            formerRealPartyId = PartyID.Uninitialized;
            if (!_partyOf.TryGetValue(character, out PartyID current))
            {
                return PartyRegistryResult.NotRegistered;
            }

            PartyState party = _parties[current];
            if (party.IsSoloParty)
            {
                FreeParty(current);
            }
            else
            {
                party.RemoveAt(party.IndexOf(character));
                if (party.MemberCount == 0)
                {
                    FreeParty(current);
                }

                formerRealPartyId = current;
            }

            _partyOf.Remove(character);
            if (_entityOf.TryGetValue(character, out EntityID entity)
                && _characterOf.TryGetValue(entity, out CharacterID owner)
                && owner == character)
            {
                _characterOf.Remove(entity);
            }

            _entityOf.Remove(character);
            _statusOf.Remove(character);
            return PartyRegistryResult.Ok;
        }

        /// <summary>Frees a real party and gives every member a fresh solo party (in join order).</summary>
        /// <param name="partyId">The real party.</param>
        /// <returns>The outcome.</returns>
        public PartyRegistryResult DisbandParty(PartyID partyId)
        {
            if (!_parties.TryGetValue(partyId, out PartyState party))
            {
                return PartyRegistryResult.UnknownParty;
            }

            if (party.IsSoloParty)
            {
                return PartyRegistryResult.SoloParty;
            }

            int count = party.MemberCount;
            if (!HasIds((uint)count))
            {
                return PartyRegistryResult.IdsExhausted;
            }

            _parties.Remove(partyId);
            for (int i = 0; i < count; i++)
            {
                CreateSolo(party.GetMemberAtIndex(i));
            }

            // Freed after the loop: the loop reads the members in join order.
            party.Free();
            return PartyRegistryResult.Ok;
        }

        /// <summary>The party a character belongs to.</summary>
        /// <param name="character">The character.</param>
        /// <returns>The party id, or <c>Uninitialized</c> when the character is not registered.</returns>
        public PartyID GetPartyID(CharacterID character)
        {
            return _partyOf.TryGetValue(character, out PartyID id) ? id : PartyID.Uninitialized;
        }

        /// <summary>The members of a party in join order (actual members only), as an immutable snapshot.</summary>
        /// <remarks>Read it with an indexed loop: <c>foreach</c> over the interface allocates an enumerator.</remarks>
        /// <param name="partyId">The party.</param>
        /// <returns>The snapshot; one shared empty list for an unknown party.</returns>
        public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId)
        {
            return _parties.TryGetValue(partyId, out PartyState party) ? party.Members : PartyState.EMPTY_MEMBERS;
        }

        /// <summary>The member at a join-order index.</summary>
        /// <param name="partyId">The party.</param>
        /// <param name="index">The index.</param>
        /// <returns>
        /// The member, or <c>CharacterID.Invalid</c> for an unknown party or bad index. Never throws.
        /// </returns>
        public CharacterID GetMemberAtIndex(PartyID partyId, int index)
        {
            return _parties.TryGetValue(partyId, out PartyState party)
                ? party.GetMemberAtIndex(index)
                : CharacterID.Invalid;
        }

        /// <summary>Looks up a party's state.</summary>
        /// <param name="partyId">The party.</param>
        /// <param name="party">The state, or null.</param>
        /// <returns>True when the party exists.</returns>
        public bool TryGetParty(PartyID partyId, out PartyState party)
        {
            return _parties.TryGetValue(partyId, out party);
        }

        /// <summary>Whether a character is registered.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True when registered.</returns>
        public bool IsRegistered(CharacterID character)
        {
            return _partyOf.ContainsKey(character);
        }

        /// <summary>The status of a character (Party System Story 004).</summary>
        /// <param name="character">The character.</param>
        /// <returns>The stored status; <c>Online</c> for an unregistered character. Never throws.</returns>
        public PartyMemberStatus GetMemberStatus(CharacterID character)
        {
            return _statusOf.TryGetValue(character, out PartyMemberStatus status) ? status : PartyMemberStatus.Online;
        }

        /// <summary>Finds the character registered for an entity.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="character">The character, or <c>CharacterID.Invalid</c>.</param>
        /// <returns>True when the entity is registered.</returns>
        public bool TryGetCharacter(EntityID entity, out CharacterID character)
        {
            if (entity != EntityID.Invalid && _characterOf.TryGetValue(entity, out character))
            {
                return true;
            }

            character = CharacterID.Invalid;
            return false;
        }

        /// <summary>
        /// Writes a party's members in join order with their statuses into a caller buffer (no allocation).
        /// Indices <c>0 .. MaxPartySize - 1</c> are written; empty indices are CharacterID 0, <c>EntityID.Invalid</c>,
        /// <c>Online</c>.
        /// </summary>
        /// <param name="partyId">The party.</param>
        /// <param name="buffer">The buffer; must hold at least MAX_PARTY_SIZE entries.</param>
        /// <returns>The member count; 0 (and nothing written) for an unknown party or a short buffer.</returns>
        public int GetPartyMembersWithStatus(PartyID partyId, Span<PartyMemberStatusEntry> buffer)
        {
            if (buffer.Length < _maxPartySize || !_parties.TryGetValue(partyId, out PartyState party))
            {
                return 0;
            }

            int count = party.MemberCount;
            for (int i = 0; i < _maxPartySize; i++)
            {
                if (i < count)
                {
                    CharacterID member = party.GetMemberAtIndex(i);
                    buffer[i] = new PartyMemberStatusEntry(member, party.GetEntityAtIndex(i), GetMemberStatus(member));
                }
                else
                {
                    buffer[i] = new PartyMemberStatusEntry(CharacterID.Invalid, EntityID.Invalid,
                        PartyMemberStatus.Online);
                }
            }

            return count;
        }

        /// <summary>
        /// Stores a registered character's status. A change bumps the character's current party's
        /// <c>SequenceId</c> by 1; the same value changes nothing. Shared by the death sink and Story 009.
        /// </summary>
        /// <param name="character">The character.</param>
        /// <param name="status">The new status.</param>
        /// <returns>True when the stored value changed; false when unregistered or unchanged.</returns>
        internal bool SetMemberStatus(CharacterID character, PartyMemberStatus status)
        {
            if (!_statusOf.TryGetValue(character, out PartyMemberStatus current) || current == status)
            {
                return false;
            }

            _statusOf[character] = status;
            if (_partyOf.TryGetValue(character, out PartyID partyId)
                && _parties.TryGetValue(partyId, out PartyState party))
            {
                party.MarkChanged();
            }

            return true;
        }

        /// <summary>Test seam: moves a party's leader index. Story 003 replaces it with the real transfer.</summary>
        /// <param name="partyId">The party.</param>
        /// <param name="index">The new leader index.</param>
        internal void SetLeaderIndexForTests(PartyID partyId, int index)
        {
            if (_parties.TryGetValue(partyId, out PartyState party))
            {
                party.SetLeaderIndexForTests(index);
            }
        }

        /// <summary>Test seam: sets the next id the counter will issue.</summary>
        /// <param name="next">The next id; 0 is refused (PartyID 0 is never issued).</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="next"/> is 0.</exception>
        internal void SetNextPartyIdForTests(uint next)
        {
            if (next == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(next), "PartyID 0 is never issued.");
            }

            _nextPartyId = next;
        }

        private void FreeParty(PartyID partyId)
        {
            _parties[partyId].Free();
            _parties.Remove(partyId);
        }

        private bool HasIds(uint needed)
        {
            // Issuable ids are _nextPartyId .. uint.MaxValue - 1; uint.MaxValue itself is never issued (OQ-PS-2).
            if (uint.MaxValue - _nextPartyId >= needed)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + "party id counter exhausted; refusing the operation (needs "
                + needed + " id(s), next id " + _nextPartyId + ").");
            return false;
        }

        private PartyID AllocateId()
        {
            return new PartyID(_nextPartyId++);
        }

        private PartyID CreateSolo(CharacterID character)
        {
            PartyID id = AllocateId();
            var party = new PartyState(id, true, PartyLifecycleState.Active, 1);
            party.Append(character, _entityOf[character]);
            _parties.Add(id, party);
            _partyOf[character] = id;
            return id;
        }
    }
}
