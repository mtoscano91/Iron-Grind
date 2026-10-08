using System;
using IronGrind.Currency;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Event payload for <see cref="IInventoryService.OnInventoryChanged"/> (ADR-010 Decision 3,
    /// Tier 2 broadcast event). Wraps a reused, service-owned buffer of <see cref="SlotChange"/>
    /// entries plus a count — zero per-emit heap allocation, never boxes.
    /// </summary>
    /// <remarks>
    /// <para><b>Validity contract:</b> this struct and the entries it exposes are valid only
    /// during the synchronous dispatch of <see cref="IInventoryService.OnInventoryChanged"/> —
    /// the underlying buffer array is owned and reused by the emitting
    /// <see cref="InventoryService"/>, and its contents are overwritten by the next mutation.
    /// Subscribers that need to retain data beyond the handler call (network sync, persistence
    /// dirty-tracking) MUST copy the <see cref="SlotChange"/> values out during the handler call —
    /// never retain this struct, its enumerator, or any reference derived from it, past the end
    /// of the synchronous dispatch.</para>
    /// <para>Iterate with <c>foreach (var change in args)</c> — the duck-typed <see cref="Enumerator"/>
    /// requires no <see cref="System.Collections.Generic.IEnumerable{T}"/> implementation and
    /// therefore never boxes.</para>
    /// </remarks>
    public readonly struct InventoryChangedEventArgs
    {
        /// <summary>The character whose inventory changed.</summary>
        public readonly CharacterID CharacterID;

        private readonly SlotChange[] _buffer;

        /// <summary>Number of valid entries in this dispatch, occupying <c>_buffer[0..Count)</c>.</summary>
        public readonly int Count;

        /// <summary>
        /// Constructs the event payload for a single dispatch. Only <see cref="InventoryService"/>
        /// may construct this — <paramref name="buffer"/> is the service's reused, service-owned
        /// change buffer, never a defensive copy.
        /// </summary>
        /// <param name="characterId">The character whose inventory changed.</param>
        /// <param name="buffer">The service-owned, reused change buffer. Not copied.</param>
        /// <param name="count">Number of valid entries at the front of <paramref name="buffer"/>.</param>
        internal InventoryChangedEventArgs(CharacterID characterId, SlotChange[] buffer, int count)
        {
            CharacterID = characterId;
            _buffer = buffer;
            Count = count;
        }

        /// <summary>
        /// Returns the change entry at <paramref name="i"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="i"/> is outside <c>[0, Count)</c> — this bounds check exists specifically so a caller can never read a stale entry left over from a previous dispatch.</exception>
        public SlotChange this[int i]
        {
            get
            {
                if (i < 0 || i >= Count)
                    throw new ArgumentOutOfRangeException(nameof(i), i, $"Index must be within [0, {Count}).");
                return _buffer[i];
            }
        }

        /// <summary>Returns an allocation-free, duck-typed enumerator over this dispatch's <see cref="Count"/> entries.</summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <summary>Allocation-free struct enumerator for <see cref="InventoryChangedEventArgs"/>. Duck-typed for <c>foreach</c> — deliberately does not implement <see cref="System.Collections.Generic.IEnumerator{T}"/> to avoid boxing (ADR-010 Decision 3).</summary>
        public struct Enumerator
        {
            private readonly InventoryChangedEventArgs _args;
            private int _index;

            internal Enumerator(InventoryChangedEventArgs args)
            {
                _args = args;
                _index = -1;
            }

            /// <summary>The current entry. Only valid after a call to <see cref="MoveNext"/> that returned <see langword="true"/>.</summary>
            /// <exception cref="InvalidOperationException">Read before the first <see cref="MoveNext"/> or after enumeration finished — guarded so a stale buffer entry past <see cref="InventoryChangedEventArgs.Count"/> can never be read.</exception>
            public SlotChange Current
            {
                get
                {
                    if (_index < 0 || _index >= _args.Count)
                        throw new InvalidOperationException("Enumerator is not positioned on a valid entry.");
                    return _args._buffer[_index];
                }
            }

            /// <summary>Advances to the next entry. Returns <see langword="false"/> once all <see cref="InventoryChangedEventArgs.Count"/> entries have been visited.</summary>
            public bool MoveNext() => ++_index < _args.Count;
        }
    }
}
