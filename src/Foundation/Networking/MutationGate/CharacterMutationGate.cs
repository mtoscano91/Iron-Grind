using System;
using System.Collections.Generic;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Default <see cref="ICharacterMutationGate"/> backed by a <see cref="HashSet{T}"/> of closed
    /// characters (ADR-011 Decision 4, Networking Core Story 030). No thread assertion (the ADR has
    /// none). An exception thrown by an <see cref="OnGateOpened"/> subscriber propagates to the
    /// caller of <see cref="Open"/>; the gate is already open at that point. Not wired in: nothing
    /// in production constructs it yet.
    /// </summary>
    /// <example>
    /// <code>
    /// var gate = new CharacterMutationGate();
    /// gate.OnGateOpened += RetryDeferredPickups;
    /// gate.Close(charId);
    /// bool held = gate.IsHeld(charId); // true
    /// gate.Open(charId);               // raises OnGateOpened(charId)
    /// </code>
    /// </example>
    public sealed class CharacterMutationGate : ICharacterMutationGate
    {
        private readonly HashSet<CharacterID> _closed = new HashSet<CharacterID>();

        /// <inheritdoc/>
        public event Action<CharacterID> OnGateOpened;

        /// <inheritdoc/>
        public bool IsHeld(CharacterID charId) => _closed.Contains(charId);

        /// <inheritdoc/>
        public void Close(CharacterID charId)
        {
            if (!_closed.Add(charId))
            {
                throw new InvalidOperationException(
                    $"The mutation gate for {charId} is already closed: only one irreversible write may be in flight per character (CR-CP-7).");
            }
        }

        /// <inheritdoc/>
        public void Open(CharacterID charId)
        {
            if (!_closed.Remove(charId))
            {
                return;
            }

            OnGateOpened?.Invoke(charId);
        }
    }
}
