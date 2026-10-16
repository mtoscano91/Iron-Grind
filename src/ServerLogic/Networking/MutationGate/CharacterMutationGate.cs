using System;
using System.Collections.Generic;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Default <see cref="ICharacterMutationGate"/> backed by a <see cref="HashSet{T}"/> of closed
    /// characters (ADR-011 Decision 4, Networking Core Story 030). No thread assertion (the ADR has
    /// none). Each <see cref="OnGateOpened"/> subscriber is invoked in its own
    /// try/catch: an exception is logged with <c>Debug.LogError</c>, the later subscribers still run
    /// and <see cref="Open"/> does not throw (the gate is already open at that point). Not wired in:
    /// nothing in production constructs it yet.
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

            Action<CharacterID> handlers = OnGateOpened;
            if (handlers == null)
            {
                return;
            }

            // Each subscriber runs in its own try/catch so one failure neither stops the later ones nor reaches the caller (TD-060).
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<CharacterID>)d)(charId);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[CharacterMutationGate] An OnGateOpened subscriber threw while opening the gate for {charId}; the remaining subscribers still ran: {ex}");
                }
            }
        }
    }
}
