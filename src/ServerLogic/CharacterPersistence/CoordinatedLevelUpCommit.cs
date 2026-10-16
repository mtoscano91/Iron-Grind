using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The coordinator-backed <see cref="ILevelUpCommit"/> (Leveling Story 016; CR-CP-4, CR-CP-5, CR-NET-5;
    /// ADR-011 Decision 5): the level-up is applied in memory, persisted with
    /// <c>SaveIrreversibleOutcome(LevelUp)</c> without awaiting, and delivered only after the write succeeds;
    /// any other result reverts it, disconnects the client and preserves the session.
    /// </summary>
    /// <remarks>
    /// <para>When the coordinator rejects the request (a write is in flight for the character, or validation
    /// failed) nothing is applied; the next XP gain retries (decision 2).</para>
    /// <para>No production <see cref="ILevelUpSessionResolver"/> exists yet and nothing constructs the coordinator
    /// in production, so this class is used by tests only until the server composition is written.</para>
    /// </remarks>
    public sealed class CoordinatedLevelUpCommit : ILevelUpCommit
    {
        private readonly IIrreversibleOutcomeCoordinator _coordinator;
        private readonly ICharacterPersistence _persistence;
        private readonly ILevelUpSessionResolver _resolver;
        private readonly Action<uint, DisconnectReason> _disconnectClient;
        private readonly Action<uint, int> _preserveSessionForTtl;

        /// <summary>Creates the seam over a coordinator, the persistence service, a session resolver and the two failure steps.</summary>
        /// <param name="coordinator">The irreversible-outcome coordinator.</param>
        /// <param name="persistence">The persistence service the level-up write goes to.</param>
        /// <param name="resolver">Maps an entity to its client and character.</param>
        /// <param name="disconnectClient">Failure step: disconnects the client.</param>
        /// <param name="preserveSessionForTtl">Failure step: keeps the session for the TTL.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public CoordinatedLevelUpCommit(
            IIrreversibleOutcomeCoordinator coordinator,
            ICharacterPersistence persistence,
            ILevelUpSessionResolver resolver,
            Action<uint, DisconnectReason> disconnectClient,
            Action<uint, int> preserveSessionForTtl)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _disconnectClient = disconnectClient ?? throw new ArgumentNullException(nameof(disconnectClient));
            _preserveSessionForTtl = preserveSessionForTtl ?? throw new ArgumentNullException(nameof(preserveSessionForTtl));
        }

        /// <summary>
        /// Starts the level-up through the coordinator. Does nothing when no level would be gained. If the session
        /// cannot be resolved, logs one error and applies nothing.
        /// </summary>
        /// <param name="entityId">The entity that crossed its XP threshold.</param>
        /// <param name="wouldLevelUp">Validation: true when a level would be gained.</param>
        /// <param name="applyLevelUp">Applies the level-up in memory and returns the levels gained.</param>
        /// <param name="deliverLevelUp">Broadcasts the levels gained, after the write succeeded.</param>
        /// <param name="revertLevelUp">Restores the pre-level-up state on any failure.</param>
        public void Commit(
            IronGrind.CharacterStats.EntityID entityId,
            Func<bool> wouldLevelUp,
            Func<IReadOnlyList<int>> applyLevelUp,
            Action<IReadOnlyList<int>> deliverLevelUp,
            Action revertLevelUp)
        {
            // Checked before resolving so a gain that cannot level up never reaches the resolver or its error log.
            if (!wouldLevelUp())
            {
                return;
            }

            if (!_resolver.TryResolve(entityId, out uint clientId, out CharacterID charId))
            {
                Debug.LogError(
                    $"[CoordinatedLevelUpCommit] Could not resolve the session of the level-up (entity={entityId}); the level-up was not applied.");
                return;
            }

            // Rejected results (write in flight, invalid request) need no handling: nothing was applied.
            _coordinator.Begin<IReadOnlyList<int>, CharacterSaveResult>(
                clientId,
                charId,
                wouldLevelUp,
                () => { },
                applyLevelUp,
                IrreversibleOutcomePersistStep.StartWrite<IReadOnlyList<int>>(_persistence, charId, IrreversibleOutcomeTrigger.LevelUp),
                IrreversibleOutcomePersistStep.IsSuccess,
                deliverLevelUp,
                revertLevelUp,
                _disconnectClient,
                _preserveSessionForTtl);
        }
    }
}
