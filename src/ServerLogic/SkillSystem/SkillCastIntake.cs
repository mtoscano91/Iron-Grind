using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.StatusEffects;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>One stored cast request, as seen by the phase-3 step (Story 005).</summary>
    /// <example>
    /// <code>
    /// if (intake.TryGetPending(slot, out PendingSkillCast cast))
    /// {
    ///     EntityID caster = cast.Caster;             // use this, do not resolve the slot again
    ///     uint skillId = cast.Request.SkillId;
    /// }
    /// </code>
    /// </example>
    public readonly struct PendingSkillCast
    {
        /// <summary>The caster (the connection's sender entity).</summary>
        public readonly EntityID Caster;

        /// <summary>The request as received.</summary>
        public readonly SkillCastRequest Request;

        /// <summary>Creates a pending cast.</summary>
        public PendingSkillCast(EntityID caster, SkillCastRequest request)
        {
            Caster = caster;
            Request = request;
        }
    }

    /// <summary>
    /// Per-player-slot store for the one cast request kept per tick (skill-system.md CR-SK-1, CR-SK-2, EC-SK-11;
    /// ADR-014 Decision 3; Skill System Story 004). First-wins until the phase-3 step has run and
    /// <see cref="ClearAll"/> empties every slot. All storage is allocated in the constructor.
    /// </summary>
    /// <example>
    /// <code>
    /// var intake = new SkillCastIntake(slotIndex, maxPlayers: 50);
    /// intake.Submit(caster, in request);                 // dispatch step
    /// for (int i = 0; i &lt; intake.Capacity; i++)        // phase 3
    /// {
    ///     if (intake.TryGetPending(i, out PendingSkillCast cast)) { /* V-0, V-3..V-8 */ }
    /// }
    /// intake.ClearAll();
    /// </code>
    /// </example>
    public sealed class SkillCastIntake : ISkillCastIntake
    {
        private const int MAX_PLAYERS_LIMIT = 255;
        private const string LOG_PREFIX = "[SkillCastIntake] ";

        private readonly IEntitySlotIndex _slotIndex;
        private readonly PendingSkillCast[] _pending;
        private readonly bool[] _occupied;
        private bool _noSlotLogged;   // no-slot warning: once per tick, re-armed by ClearAll (which runs once per tick)

        /// <summary>Creates the intake and allocates one slot per player.</summary>
        /// <param name="slotIndex">Entity to zone slot index; players map to [0, <paramref name="maxPlayers"/>).</param>
        /// <param name="maxPlayers">Player capacity in [1, 255]; a slot at or above it is a mob index and is refused.</param>
        /// <exception cref="ArgumentNullException"><paramref name="slotIndex"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPlayers"/> is outside [1, 255].</exception>
        public SkillCastIntake(IEntitySlotIndex slotIndex, int maxPlayers)
        {
            _slotIndex = slotIndex ?? throw new ArgumentNullException(nameof(slotIndex));
            if (maxPlayers < 1 || maxPlayers > MAX_PLAYERS_LIMIT)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), maxPlayers, "must be in [1, 255]");
            }

            _pending = new PendingSkillCast[maxPlayers];
            _occupied = new bool[maxPlayers];
        }

        /// <summary>Number of player slots; valid slots for <see cref="TryGetPending"/> are [0, Capacity).</summary>
        public int Capacity => _occupied.Length;

        /// <inheritdoc />
        public void Submit(EntityID caster, in SkillCastRequest request)
        {
            if (!_slotIndex.TryGetSlotIndex(caster, out int slot) || slot < 0 || slot >= _occupied.Length)
            {
                // Throttled to one warning until the next ClearAll (once per tick); the request is dropped either way.
                if (!_noSlotLogged)
                {
                    _noSlotLogged = true;
                    Debug.LogWarning(LOG_PREFIX + "cast dropped: " + caster + " has no player slot (players only, CR-SK-17)");
                }

                return;
            }

            if (_occupied[slot])
            {
                return; // first-wins (EC-SK-11): silent discard
            }

            _pending[slot] = new PendingSkillCast(caster, request);
            _occupied[slot] = true;
        }

        /// <summary>
        /// Reads the stored request of a slot. The phase-3 consumer must use <see cref="PendingSkillCast.Caster"/> as
        /// the caster and must not resolve the entity again from the slot (the slot map may have changed since the
        /// request was stored).
        /// </summary>
        /// <param name="slot">Slot in [0, <see cref="Capacity"/>).</param>
        /// <param name="cast">The stored request when found.</param>
        /// <returns>True when the slot holds a request; false for an empty or out-of-range slot.</returns>
        public bool TryGetPending(int slot, out PendingSkillCast cast)
        {
            if (slot >= 0 && slot < _occupied.Length && _occupied[slot])
            {
                cast = _pending[slot];
                return true;
            }

            cast = default;
            return false;
        }

        /// <summary>Empties every slot; call once the phase-3 step has run. Unprocessed requests are not carried over.</summary>
        public void ClearAll()
        {
            _noSlotLogged = false;
            for (int i = 0; i < _occupied.Length; i++)
            {
                _occupied[i] = false;
                _pending[i] = default;
            }
        }
    }
}
