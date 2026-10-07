using System;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Records, per character, that an irreversible write is in flight (ADR-011 Decision 4). The
    /// irreversible-outcome coordinator closes it before applying the outcome and opens it after the
    /// result has been handled; the request dispatcher and server-originated bag mutations read it.
    /// </summary>
    /// <example>
    /// <code>
    /// gate.Close(charId);
    /// try { /* apply outcome, start write, track it */ }
    /// finally { /* later, in the completion callback: */ gate.Open(charId); }
    /// </code>
    /// </example>
    public interface ICharacterMutationGate
    {
        /// <summary>True while the character's gate is closed.</summary>
        /// <param name="charId">The character.</param>
        bool IsHeld(CharacterID charId);

        /// <summary>Closes the gate for a character.</summary>
        /// <param name="charId">The character.</param>
        /// <exception cref="InvalidOperationException">The gate is already closed (CR-CP-7: one write in flight); it stays closed.</exception>
        void Close(CharacterID charId);

        /// <summary>
        /// Opens the gate and then raises <see cref="OnGateOpened"/>. Opening a gate that is not
        /// closed does nothing and raises no event.
        /// </summary>
        /// <param name="charId">The character.</param>
        void Open(CharacterID charId);

        /// <summary>Raised once per closed-to-open transition, after <see cref="IsHeld"/> has become false.</summary>
        event Action<CharacterID> OnGateOpened;
    }
}
