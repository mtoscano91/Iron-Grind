using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// State of one party (design/gdd/party-system.md CR-PS-1, CR-PS-2; Party System Story 002). Read-only to
    /// callers; only <see cref="PartyRegistry"/> mutates it.
    /// </summary>
    /// <remarks>
    /// Members live in fixed-size join-order slot arrays. <see cref="Members"/> is an immutable snapshot that is
    /// replaced (never mutated) on every membership change, so a list handed out earlier never changes.
    /// </remarks>
    public sealed class PartyState
    {
        /// <summary>The one shared empty member list (unknown, new or freed parties).</summary>
        internal static readonly ReadOnlyCollection<CharacterID> EMPTY_MEMBERS =
            new ReadOnlyCollection<CharacterID>(Array.Empty<CharacterID>());

        private readonly CharacterID[] _members;
        private readonly EntityID[] _entities;
        private int _count;
        private ReadOnlyCollection<CharacterID> _snapshot;

        internal PartyState(PartyID partyId, bool isSoloParty, PartyLifecycleState state, int capacity)
        {
            PartyId = partyId;
            IsSoloParty = isSoloParty;
            State = state;
            _members = new CharacterID[capacity];
            _entities = new EntityID[capacity];
            _snapshot = EMPTY_MEMBERS;
        }

        /// <summary>
        /// True once the registry has freed this party (disbanded, emptied, or a solo party replaced on create or
        /// join). A freed party has no members and its id no longer resolves; a caller holding this object must not
        /// act on it. Resolve parties by id each time instead of caching this object.
        /// </summary>
        public bool IsFreed { get; private set; }

        /// <summary>The party id (never 0).</summary>
        public PartyID PartyId { get; }

        /// <summary>True for the single-player placeholder party every unpartied player has (write-once).</summary>
        public bool IsSoloParty { get; }

        /// <summary>The lifecycle state.</summary>
        public PartyLifecycleState State { get; private set; }

        /// <summary>The number of members.</summary>
        public int MemberCount => _count;

        /// <summary>The leader's slot index (always 0 in Story 002; leader rules arrive in Story 003).</summary>
        public int LeaderIndex { get; private set; }

        /// <summary>The round-robin cursor (always 0 in Story 002; arrives in Story 003).</summary>
        public byte RrNextIndex { get; private set; }

        /// <summary>
        /// Incremented on every membership change and on every member status change that alters a stored value
        /// (<see cref="MarkChanged"/>).
        /// </summary>
        public uint SequenceId { get; private set; }

        /// <summary>
        /// The current members in join order, no empty slots. An immutable snapshot, replaced on change.
        /// </summary>
        /// <remarks>Read it with an indexed loop: <c>foreach</c> over the interface allocates an enumerator.</remarks>
        public IReadOnlyList<CharacterID> Members => _snapshot;

        /// <summary>The member at a slot index, or <c>CharacterID.Invalid</c> when the index is out of range.</summary>
        /// <param name="index">Join-order index.</param>
        /// <returns>The member, or <c>CharacterID.Invalid</c>.</returns>
        public CharacterID GetMemberAtIndex(int index)
        {
            return index >= 0 && index < _count ? _members[index] : CharacterID.Invalid;
        }

        /// <summary>
        /// The entity registered for the member at a slot index, or the default entity when out of range.
        /// </summary>
        /// <param name="index">Join-order index.</param>
        /// <returns>The entity, or <c>EntityID.Invalid</c>.</returns>
        public EntityID GetEntityAtIndex(int index)
        {
            return index >= 0 && index < _count ? _entities[index] : EntityID.Invalid;
        }

        internal int IndexOf(CharacterID character)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_members[i] == character)
                {
                    return i;
                }
            }

            return -1;
        }

        internal void Append(CharacterID character, EntityID entity)
        {
            _members[_count] = character;
            _entities[_count] = entity;
            _count++;
            Changed();
        }

        internal void RemoveAt(int index)
        {
            for (int i = index; i < _count - 1; i++)
            {
                _members[i] = _members[i + 1];
                _entities[i] = _entities[i + 1];
            }

            _count--;
            _members[_count] = CharacterID.Invalid;
            _entities[_count] = EntityID.Invalid;
            Changed();
        }

        /// <summary>Marks a non-membership change (a member status write): <c>SequenceId</c> + 1.</summary>
        internal void MarkChanged()
        {
            SequenceId++;
        }

        /// <summary>Test seam: sets the leader index unchecked. Story 003 replaces it with the real transfer.</summary>
        internal void SetLeaderIndexForTests(int index)
        {
            LeaderIndex = index;
        }

        internal void SetState(PartyLifecycleState state)
        {
            State = state;
        }

        /// <summary>
        /// Marks the party freed: clears every slot, empties <see cref="Members"/>, bumps the sequence.
        /// </summary>
        internal void Free()
        {
            Array.Clear(_members, 0, _members.Length);
            Array.Clear(_entities, 0, _entities.Length);
            _count = 0;
            IsFreed = true;
            SequenceId++;
            _snapshot = EMPTY_MEMBERS;
        }

        private void Changed()
        {
            SequenceId++;
            _snapshot = BuildSnapshot();
        }

        private ReadOnlyCollection<CharacterID> BuildSnapshot()
        {
            if (_count == 0)
            {
                return EMPTY_MEMBERS;
            }

            var copy = new CharacterID[_count];
            Array.Copy(_members, copy, _count);
            return new ReadOnlyCollection<CharacterID>(copy);
        }
    }
}
