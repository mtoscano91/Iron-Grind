using UnityEngine.AI;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Seam over the zone scene load and NavMesh data of startup S6 (ADR-009 Decision 1 and Amendment 1
    /// Decision 2). Story 045 implements it over <c>SceneManager.LoadSceneAsync</c> and the zone definition's
    /// baked NavMesh. Main thread only.
    /// </summary>
    /// <remarks>
    /// The composition calls <see cref="BeginLoad"/> inside a step, polls <see cref="IsDone"/> once per frame
    /// between steps (the only real wait of startup), then reads <see cref="Succeeded"/> and calls
    /// <see cref="AddNavMeshData"/> inside the next step.
    /// </remarks>
    /// <example>
    /// <code>
    /// scene.BeginLoad("Forest");
    /// while (!scene.IsDone) { yield return null; }
    /// if (!scene.Succeeded) { /* S-fail */ }
    /// NavMeshDataInstance instance = scene.AddNavMeshData();
    /// </code>
    /// </example>
    public interface IZoneScene
    {
        /// <summary>Starts loading the zone scene.</summary>
        /// <param name="zoneTemplate">The <c>-zoneTemplate</c> command-line value.</param>
        void BeginLoad(string zoneTemplate);

        /// <summary>True once the load finished (successfully or not).</summary>
        bool IsDone { get; }

        /// <summary>True when the finished load succeeded. Only read after <see cref="IsDone"/>.</summary>
        bool Succeeded { get; }

        /// <summary>Adds the zone's baked NavMesh data (old Decision 2 step 1) and returns its instance.</summary>
        /// <returns>The NavMesh data instance handed to <see cref="IZoneNavigation.Initialize"/>.</returns>
        NavMeshDataInstance AddNavMeshData();
    }
}
