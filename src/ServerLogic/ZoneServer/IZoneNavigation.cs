using IronGrind.CharacterPersistence;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Zone navigation as the composition sees it (ADR-009 Amendment 1 Key Interfaces). The real service
    /// belongs to the Navigation epic; until then <see cref="StandInZoneNavigation"/> is passed.
    /// </summary>
    /// <example>
    /// <code>
    /// navigation.Initialize(zone, instance); // startup S6
    /// navigation.Teardown();                 // teardown step 3
    /// </code>
    /// </example>
    public interface IZoneNavigation
    {
        /// <summary>Initialises navigation for a zone with its loaded NavMesh data.</summary>
        /// <param name="zone">The zone being started.</param>
        /// <param name="instance">The added NavMesh data instance.</param>
        void Initialize(ZoneID zone, NavMeshDataInstance instance);

        /// <summary>Tears navigation down (CR-NAV-3); teardown step 3.</summary>
        void Teardown();
    }
}
