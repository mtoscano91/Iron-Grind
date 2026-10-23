using IronGrind.CharacterStats;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The one member of <see cref="CharacterPersistenceService"/> that Zone Instancing needs after a load: writes a
    /// loaded record into the live systems. A seam so tests can force a failure (Zone Instancing Story 009).
    /// </summary>
    public interface ILoadedRecordApplier
    {
        /// <summary>Applies <paramref name="record"/> to the live systems for <paramref name="entityId"/>. Tick thread.</summary>
        /// <param name="entityId">The entity that now represents the character.</param>
        /// <param name="record">The record returned by <c>LoadCharacter</c>.</param>
        /// <returns><see cref="CharacterLoadCode.Success"/>, or the failure code (for example <see cref="CharacterLoadCode.InventoryNotRestored"/>).</returns>
        CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record);
    }
}
