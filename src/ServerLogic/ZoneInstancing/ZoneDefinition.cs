using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Server-side description of one zone template (ADR-009, CR-ZI-10): walkable bounds, town respawn point and the
    /// baked <see cref="NavMeshData"/>. Read-only at runtime; the loader publishes the loaded one through a
    /// <see cref="ZoneDefinitionSlot"/>.
    /// </summary>
    /// <remarks>
    /// Deviation from the ADR-009 code sketch (<c>Rect _walkableBoundsXZ</c>): the bounds are <see cref="Vector3"/>
    /// min / max checked on all three axes, because the position encoding (CR-NET-7.2) caps every axis (Story 004
    /// Decision 3). Fields are serialized private fields; Unity 6.3 rejects <c>[SerializeField]</c> on properties.
    /// </remarks>
    /// <example>
    /// <code>
    /// Vector3 size = definition.WalkableBoundsMax - definition.WalkableBoundsMin;
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "ZoneDefinition", menuName = "IronGrind/Zone Definition")]
    public sealed class ZoneDefinition : ScriptableObject
    {
        [SerializeField] private ZoneTemplateID _templateId;
        [SerializeField] private Vector3 _walkableBoundsMin;
        [SerializeField] private Vector3 _walkableBoundsMax;
        [SerializeField] private Vector3 _townRespawnPoint;
        [SerializeField] private bool _townRespawnPointAuthored;
        [SerializeField] private NavMeshData _bakedNavMesh;

        /// <summary>The template this definition describes.</summary>
        public ZoneTemplateID TemplateId => _templateId;

        /// <summary>Minimum corner of the walkable bounds, metres.</summary>
        public Vector3 WalkableBoundsMin => _walkableBoundsMin;

        /// <summary>Maximum corner of the walkable bounds, metres.</summary>
        public Vector3 WalkableBoundsMax => _walkableBoundsMax;

        /// <summary>The town respawn point (used from Story 005).</summary>
        public Vector3 TownRespawnPoint => _townRespawnPoint;

        /// <summary>True when the respawn point was authored by level design.</summary>
        public bool TownRespawnPointAuthored => _townRespawnPointAuthored;

        /// <summary>The baked NavMesh data; null when the zone has none yet.</summary>
        public NavMeshData BakedNavMesh => _bakedNavMesh;

        /// <summary>Test-only initialiser (EditMode tests see internals).</summary>
        internal void Initialize(
            ZoneTemplateID templateId, Vector3 boundsMin, Vector3 boundsMax, Vector3 townRespawnPoint,
            bool townRespawnPointAuthored, NavMeshData bakedNavMesh)
        {
            _templateId = templateId;
            _walkableBoundsMin = boundsMin;
            _walkableBoundsMax = boundsMax;
            _townRespawnPoint = townRespawnPoint;
            _townRespawnPointAuthored = townRespawnPointAuthored;
            _bakedNavMesh = bakedNavMesh;
        }
    }
}
