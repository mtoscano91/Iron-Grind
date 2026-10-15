using System;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The one place that builds the <c>startWrite</c> and <c>isSuccess</c> arguments of
    /// <c>IIrreversibleOutcomeCoordinator.Begin</c> from <see cref="ICharacterPersistence.SaveIrreversibleOutcome"/>
    /// (Character Persistence Story 009; ADR-011 Decision 5).
    /// </summary>
    /// <remarks>
    /// Only starts the write and returns its task: it never awaits or blocks. The coordinator tracks the
    /// task, and the caller's revert, disconnect and session-preserve delegates handle every result other
    /// than <see cref="CharacterSaveResult.Success"/> (CR-CP-5).
    /// </remarks>
    /// <example>
    /// <code>
    /// coordinator.Begin&lt;LevelUpOutcome, CharacterSaveResult&gt;(
    ///     clientId, charId,
    ///     validateRequest: () =&gt; true,
    ///     emitAcknowledgment: () =&gt; { },
    ///     computeAndApplyOutcome: ApplyLevelUp,
    ///     startWrite: IrreversibleOutcomePersistStep.StartWrite&lt;LevelUpOutcome&gt;(persistence, charId, IrreversibleOutcomeTrigger.LevelUp),
    ///     isSuccess: IrreversibleOutcomePersistStep.IsSuccess,
    ///     deliverOutcome: Broadcast,
    ///     revertOnFailure: RollBack,
    ///     disconnectClient: Disconnect,
    ///     preserveSessionForTtl: Preserve);
    /// </code>
    /// </example>
    public static class IrreversibleOutcomePersistStep
    {
        /// <summary>
        /// Decides whether a completed write means success: true for <see cref="CharacterSaveResult.Success"/> only.
        /// </summary>
        public static readonly Func<CharacterSaveResult, bool> IsSuccess = result => result == CharacterSaveResult.Success;

        /// <summary>
        /// Builds the coordinator's <c>startWrite</c> delegate: it calls
        /// <see cref="ICharacterPersistence.SaveIrreversibleOutcome"/> once per invocation with the given
        /// character and trigger and the coordinator's cancellation token, and returns the started task.
        /// </summary>
        /// <typeparam name="TOutcome">
        /// The caller's outcome type; the write ignores it. The coordinator applies the outcome in memory before it starts
        /// the write, so the live state that <see cref="ICharacterPersistence.SaveIrreversibleOutcome"/> snapshots already
        /// includes it.
        /// </typeparam>
        /// <param name="persistence">The persistence service.</param>
        /// <param name="charId">The character to save.</param>
        /// <param name="trigger">The outcome that triggers the save (log lines only).</param>
        /// <returns>A delegate suitable for the <c>startWrite</c> argument of <c>Begin</c>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="persistence"/> is null.</exception>
        public static Func<TOutcome, CancellationToken, Task<CharacterSaveResult>> StartWrite<TOutcome>(
            ICharacterPersistence persistence,
            CharacterID charId,
            IrreversibleOutcomeTrigger trigger)
        {
            if (persistence == null)
            {
                throw new ArgumentNullException(nameof(persistence));
            }

            return (outcome, token) => persistence.SaveIrreversibleOutcome(charId, trigger, token);
        }
    }
}
