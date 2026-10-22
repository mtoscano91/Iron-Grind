using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The mob API of Zone Instancing used by Mob Spawning (<c>zone-instancing.md</c> CR-ZI-13, EC-ZI-10).
    /// Callers reach it by direct calls on an injected interface (ADR-010); there is no event bus.
    /// Main thread only: every call is serialised by the caller. Each call is O(1) and allocation-free.
    /// </summary>
    public interface IZoneMobRoster
    {
        /// <summary>
        /// Allocates a mob slot, mints an EntityID and stores the mob's type and spawn position.
        /// At cap the call fails without retrying and returns <see cref="EntityID.Invalid"/> (EC-ZI-10);
        /// a rate-limited <c>MobSlotExhausted</c> warning is logged. A foreign zone or a spent
        /// EntityID counter also return <see cref="EntityID.Invalid"/>.
        /// </summary>
        /// <param name="zone">Must be the zone this process hosts.</param>
        /// <param name="type">The mob's type.</param>
        /// <param name="spawnPosition">The spawn position (not the current position).</param>
        /// <returns>The new mob's EntityID, or <see cref="EntityID.Invalid"/> on failure.</returns>
        EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition);

        /// <summary>
        /// Frees the mob's slot. An unknown or already-removed id, or a foreign zone, changes nothing
        /// and logs one warning per call.
        /// </summary>
        /// <param name="zone">Must be the zone this process hosts.</param>
        /// <param name="id">The EntityID returned by <see cref="TryAddMob"/>.</param>
        void RemoveMob(ZoneID zone, EntityID id);

        /// <summary>
        /// The number of live mobs. A foreign zone returns 0 and warns once per roster
        /// (Mob Spawning polls this).
        /// </summary>
        /// <param name="zone">Must be the zone this process hosts.</param>
        /// <returns>The live mob count.</returns>
        int GetMobCount(ZoneID zone);

        /// <summary>Reads the stored type and spawn position of a live mob.</summary>
        /// <param name="id">The mob's EntityID.</param>
        /// <param name="type">The mob type, or default when unknown.</param>
        /// <param name="spawnPosition">The spawn position, or <see cref="Vector3.zero"/> when unknown.</param>
        /// <returns>True when <paramref name="id"/> is a live mob.</returns>
        bool TryGetMob(EntityID id, out MobTypeID type, out Vector3 spawnPosition);
    }
}
