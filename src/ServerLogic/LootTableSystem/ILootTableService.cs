using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Public entry points of the Loot Table System. The GDDs call this <c>ILootTableSystem</c>
    /// (design/gdd/enemy-ai.md, design/gdd/loot-table-system.md); the code name follows the
    /// <c>I[SystemName]Service</c> rule of ADR-010.
    /// </summary>
    public interface ILootTableService
    {
        /// <summary>
        /// Records one damage event against a mob for the party tag (CR-LT-3). Forwards to
        /// <see cref="PartyTagTracker.RecordDamage"/>, so combat code depends on one interface.
        /// </summary>
        /// <param name="mobEntityId">The damaged mob.</param>
        /// <param name="attacker">The attacking character.</param>
        /// <param name="finalDamage">The final damage dealt by this event.</param>
        void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage);

        /// <summary>
        /// Drops a mob's damage record without resolving a kill. Call it when a mob is removed
        /// from the world by anything other than <see cref="ResolveMobDrop"/> (despawn), so a
        /// reused <see cref="EntityID"/> does not inherit the old threshold and tag owner. An
        /// unknown mob is a silent no-op.
        /// </summary>
        /// <param name="mobEntityId">The mob to forget.</param>
        void ClearMob(EntityID mobEntityId);

        /// <summary>
        /// Resolves a mob's death (design/gdd/enemy-ai.md entry point): clears the mob's damage
        /// record, rolls the drop list and pays the gold share to each member of the winning party
        /// (CR-LT-14, F-LT-1). A kill is resolved at most once. A mob with no recorded damage
        /// resolves silently (CR-LT-4).
        /// </summary>
        /// <param name="mobEntityID">The mob that died.</param>
        /// <param name="tierShift">
        /// Tier shift from Enemy AI. Accepted but not applied: the loot GDD has no rule for it yet
        /// (enemy-ai.md OQ-AI-1); a non-zero value logs one warning.
        /// </param>
        void ResolveMobDrop(EntityID mobEntityID, int tierShift);

        /// <summary>
        /// Drops every mob damage record, so nothing survives a zone teardown (Story 012). Called
        /// by <see cref="LootTeardownCoordinator.FlushForZoneTeardown"/>. With no records it does nothing.
        /// </summary>
        void Clear();
    }
}
