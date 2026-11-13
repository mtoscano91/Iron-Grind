using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Unity adapter for <see cref="INavAgentFactory"/>: builds each pooled agent as a plain <see cref="GameObject"/>
    /// with a <see cref="NavMeshAgent"/> under a named pool root (Story 009, ADR-002 Decision 5). No prefab.
    /// </summary>
    /// <remarks>
    /// The agent GameObject is created inactive, the component is added and disabled, and only then is the object
    /// activated, so the agent never enables off the mesh during the pool fill (it would log "Failed to create agent
    /// because it is not close enough to the NavMesh").
    /// <para>
    /// The claim enables a pooled agent at its parked position before <c>Warp</c> (ADR-003 order), and a real agent
    /// enabled off the mesh logs that warning (verified in Story 009). So when a pool root is created the park position
    /// is moved to the nearest NavMesh point within <see cref="PARK_SAMPLE_RADIUS"/>: the pool fill runs in
    /// <c>Initialize</c> at S6, after the zone's NavMesh is added. With no mesh in range the given position is kept and
    /// each claim logs the warning (the claim itself still succeeds, since <c>Warp</c> places the agent).
    /// </para>
    /// </remarks>
    public sealed class UnityNavAgentFactory : INavAgentFactory
    {
        private const string AGENT_NAME = "NavAgent";

        /// <summary>Search radius (metres) used to move the park position onto the NavMesh; covers a whole zone.</summary>
        public const float PARK_SAMPLE_RADIUS = 1000f;

        private readonly Vector3 _parkPosition;
        private readonly Dictionary<string, GameObject> _roots = new Dictionary<string, GameObject>();

        /// <summary>Creates the factory.</summary>
        /// <param name="parkPosition">
        /// Preferred world position for the pooled agent objects; moved to the nearest NavMesh point when a pool root
        /// is created.
        /// </param>
        public UnityNavAgentFactory(Vector3 parkPosition = default)
        {
            _parkPosition = parkPosition;
        }

        /// <inheritdoc/>
        /// <remarks>The pool root sits at the resolved park position; every agent is placed at the root.</remarks>
        public INavAgent Create(string poolRootName)
        {
            if (!_roots.TryGetValue(poolRootName, out GameObject root) || root == null)
            {
                root = new GameObject(poolRootName);
                root.transform.position = ResolveParkPosition();
                _roots[poolRootName] = root;
            }

            var go = new GameObject(AGENT_NAME);
            go.SetActive(false);
            go.transform.SetParent(root.transform, false);
            go.transform.position = root.transform.position;
            NavMeshAgent agent = go.AddComponent<NavMeshAgent>();
            agent.enabled = false;
            go.SetActive(true);
            return new UnityNavAgent(agent);
        }

        // Nearest NavMesh point to the preferred park position, or the preferred position when no mesh is in range.
        private Vector3 ResolveParkPosition()
        {
            return NavMesh.SamplePosition(_parkPosition, out NavMeshHit hit, PARK_SAMPLE_RADIUS, NavMesh.AllAreas)
                ? hit.position
                : _parkPosition;
        }

        /// <summary>Destroys every pool root this factory created (and their agents) and clears the cache.</summary>
        public void DestroyPoolRoots()
        {
            foreach (KeyValuePair<string, GameObject> pair in _roots)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(pair.Value);
                }
                else
                {
                    Object.DestroyImmediate(pair.Value);
                }
            }

            _roots.Clear();
        }
    }
}
