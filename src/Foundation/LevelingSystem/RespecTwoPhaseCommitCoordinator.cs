using System;
using System.Collections.Generic;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Stateless static coordinator for the CR-4.1 two-phase-commit respec sequence: Phase 1 —
    /// Gate and Reserve (CR-4.6 combat gate, then reservation), Phase 2 — Commit (calls
    /// <see cref="LevelingService.TryApplyRespec"/>, then <see cref="IItemReservation.Consume"/>
    /// on success or <see cref="IItemReservation.Release"/> on any exception). Mirrors
    /// <c>PartyDisbandCoordinator</c>'s exact stateless-static shape (Networking Core Story 020):
    /// every call is fully described by its arguments — nothing is tracked between calls.
    /// </summary>
    /// <remarks>
    /// <para><b>Forward-dependency stand-in, per this story's own Implementation Notes.</b> No
    /// real Inventory System or Status Effects System exists yet in this codebase. The combat
    /// gate (<paramref name="hasCombatTaggedEffect"/> below) and the reservation factory
    /// (<paramref name="reserveItem"/> below) are supplied by the caller — today: a test double
    /// (<c>LevelingSystem_RespecTwoPhaseCommit_tests.cs</c>); eventually: the real Status
    /// Effects <c>HasCombatTaggedEffect()</c> and the real Inventory System's reservation
    /// machinery. This mirrors <c>PartyDisbandCoordinator</c>'s caller-supplied party-membership
    /// list treatment of the not-yet-built Party System.</para>
    /// <para><b>AC-LS-18a gate ordering.</b> The combat gate is checked BEFORE
    /// <paramref name="reserveItem"/> is ever invoked — a rejected gate means the item is never
    /// reserved and <see cref="LevelingService.TryApplyRespec"/> is never called (CR-4.6: the
    /// Inventory System owns this gate; the Leveling System never independently checks combat
    /// state).</para>
    /// <para><b>Not the real two-phase commit.</b> No 30-second reservation TTL, no DB
    /// persistence across server crash, no reconnect recovery — those belong to a future
    /// Inventory System epic story (CR-4.1's full text). This coordinator proves only the
    /// Leveling-System-side contract this story's ACs require: gate-before-reserve ordering
    /// (AC-LS-18a) and exception-safety with item-return-on-failure (AC-LS-22).</para>
    /// </remarks>
    public static class RespecTwoPhaseCommitCoordinator
    {
        /// <summary>
        /// Executes the CR-4.1 two-phase-commit respec sequence.
        /// </summary>
        /// <remarks>
        /// Call sequence: <paramref name="hasCombatTaggedEffect"/>(<paramref name="entityId"/>)
        /// → if <see langword="true"/>, return immediately (item untouched, never reserved,
        /// <see cref="LevelingService.TryApplyRespec"/> never called — AC-LS-18a). Otherwise:
        /// <paramref name="reserveItem"/>() → <see cref="LevelingService.TryApplyRespec"/> →
        /// on success, <see cref="IItemReservation.Consume"/>; on any exception,
        /// <see cref="IItemReservation.Release"/>, then the exception is re-thrown (AC-LS-22) so
        /// the caller can observe the failure.
        /// </remarks>
        /// <param name="entityId">The entity attempting respec.</param>
        /// <param name="hasCombatTaggedEffect">
        /// CR-4.6 combat gate, owned by the Inventory System per the GDD — checked BEFORE the
        /// item is reserved. Test doubles stub this (AC-LS-18a); the real implementation is
        /// Status Effects' <c>HasCombatTaggedEffect()</c> (AC-LS-18b, blocked on OQ-LS-3 — not
        /// wired here).
        /// </param>
        /// <param name="reserveItem">
        /// Creates the <see cref="IItemReservation"/> handle for the respec item, moving it from
        /// active inventory into a reserved hold slot (CR-4.1 Phase 1's second step). Invoked
        /// only when <paramref name="hasCombatTaggedEffect"/> returns <see langword="false"/>.
        /// </param>
        /// <param name="levelingService">The service <see cref="LevelingService.TryApplyRespec"/> is called on.</param>
        /// <param name="newTotals">Forwarded unchanged to <see cref="LevelingService.TryApplyRespec"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="hasCombatTaggedEffect"/>, <paramref name="reserveItem"/>,
        /// <paramref name="levelingService"/>, or <paramref name="newTotals"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="Exception">
        /// Re-thrown, unchanged, whenever <see cref="LevelingService.TryApplyRespec"/> throws —
        /// after <see cref="IItemReservation.Release"/> has already been called (AC-LS-22).
        /// </exception>
        public static void ExecuteRespec(
            IronGrind.CharacterStats.EntityID entityId,
            Func<IronGrind.CharacterStats.EntityID, bool> hasCombatTaggedEffect,
            Func<IItemReservation> reserveItem,
            LevelingService levelingService,
            IReadOnlyDictionary<IronGrind.CharacterStats.StatID, int> newTotals)
        {
            if (hasCombatTaggedEffect == null)
                throw new ArgumentNullException(nameof(hasCombatTaggedEffect));
            if (reserveItem == null)
                throw new ArgumentNullException(nameof(reserveItem));
            if (levelingService == null)
                throw new ArgumentNullException(nameof(levelingService));
            if (newTotals == null)
                throw new ArgumentNullException(nameof(newTotals));

            // CR-4.1 Phase 1 / CR-4.6 — combat gate BEFORE reservation. AC-LS-18a: a rejected
            // gate leaves the item untouched — never reserved, TryApplyRespec never called.
            if (hasCombatTaggedEffect(entityId))
                return;

            IItemReservation reservation = reserveItem();

            // CR-4.1 Phase 2 — Commit. Only TryApplyRespec can legitimately fail in the
            // two-phase-commit sense, so the try/catch wraps ONLY that call — Consume() runs
            // AFTER the try block completes without exception, never inside it. This keeps a
            // (hypothetical) throwing Consume() on an already-successful commit from being
            // misrouted into Release() as if the commit itself had failed.
            try
            {
                levelingService.TryApplyRespec(entityId, newTotals);
            }
            catch
            {
                // AC-LS-22 — item safety guarantee: any exception from TryApplyRespec (which has
                // already rolled back its own stat transaction — see LevelingService.
                // TryApplyRespec's catch block) returns the item to active inventory. Re-thrown
                // so the caller can observe the failure too.
                reservation.Release();
                throw;
            }

            reservation.Consume();
        }
    }
}
