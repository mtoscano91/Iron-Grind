using IronGrind.ServerLogic.ZoneInstancing;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneScene"/> over <c>SceneManager.LoadSceneAsync(name, LoadSceneMode.Single)</c> (ADR-009
    /// Decision 1, Amendment 1 S6). <see cref="BeginLoad"/> resolves the <c>-zoneTemplate</c> value (a
    /// <see cref="ZoneTemplateID"/> name) through the <see cref="ZoneSceneRegistry"/>; an unknown name, a template
    /// without a complete entry, or a scene name <c>LoadSceneAsync</c> rejects makes the loader report done and not
    /// succeeded, which is S-fail at S6. On a started load it publishes the definition through the
    /// <see cref="ZoneDefinitionSlot"/>. <see cref="AddNavMeshData"/> adds the definition's baked NavMesh; a
    /// definition without one keeps a once-logged stand-in and returns <c>default</c>. Main thread only.
    /// </summary>
    /// <remarks>
    /// In the Editor and development builds it logs the instance ids of the host object and of the
    /// <c>NetworkManager</c> object before the load starts and once the load completed
    /// (<c>[ZoneServer] A1-3 before load</c> / <c>after load</c>), so a run can show both survived the Single-mode load
    /// (Story 045 AC-7). Removing the NavMesh instance at teardown belongs to the real navigation (Navigation epic).
    /// </remarks>
    /// <example>
    /// <code>
    /// IZoneScene scene = new UnitySceneLoader(gameObject, registry, slot);
    /// scene.BeginLoad("StartingZone");
    /// while (!scene.IsDone) { yield return null; }
    /// bool ok = scene.Succeeded;
    /// </code>
    /// </example>
    public sealed class UnitySceneLoader : IZoneScene
    {
        private readonly GameObject _hostObject;
        private readonly ZoneSceneRegistry _registry;
        private readonly ZoneDefinitionSlot _slot;
        private AsyncOperation _operation;
        private bool _nullOperation;
        private bool _afterLogged;
        private bool _standInLogged;

        /// <summary>Creates a loader.</summary>
        /// <param name="hostObject">The host's GameObject for the instance id logs; may be null.</param>
        /// <param name="registry">The template registry; null makes every load fail.</param>
        /// <param name="slot">The slot filled with the loaded definition; must not be null.</param>
        public UnitySceneLoader(GameObject hostObject, ZoneSceneRegistry registry, ZoneDefinitionSlot slot)
        {
            if (slot == null)
            {
                throw new System.ArgumentNullException(nameof(slot));
            }

            _hostObject = hostObject;
            _registry = registry;
            _slot = slot;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Single use per loader: a second call resets the loader state but does not cancel an earlier started load.
        /// Startup S6 calls it once.
        /// </remarks>
        public void BeginLoad(string zoneTemplate)
        {
            _operation = null;
            _nullOperation = false;
            _afterLogged = false;
            _slot.Clear();
            LogInstanceIds("A1-3 before load");

            string sceneName = null;
            ZoneDefinition definition = null;
            if (_registry == null || !_registry.TryResolve(zoneTemplate, out sceneName, out definition))
            {
                _nullOperation = true;
                Debug.LogError("[ZoneInstancing] ZoneDefinitionLoadFailed: unknown or unregistered zone template '"
                    + zoneTemplate + "'");
                return;
            }

            _operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            _nullOperation = _operation == null;
            if (!_nullOperation)
            {
                _slot.Set(definition);
            }
        }

        /// <inheritdoc/>
        public bool IsDone
        {
            get
            {
                if (_nullOperation)
                {
                    return true;
                }

                if (_operation == null || !_operation.isDone)
                {
                    return false;
                }

                if (!_afterLogged)
                {
                    _afterLogged = true;
                    LogInstanceIds("A1-3 after load");
                }

                return true;
            }
        }

        /// <inheritdoc/>
        public bool Succeeded => !_nullOperation && _operation != null && _operation.isDone;

        /// <inheritdoc/>
        public NavMeshDataInstance AddNavMeshData()
        {
            ZoneDefinition definition = _slot.Current;
            if (definition != null && definition.BakedNavMesh != null)
            {
                return NavMesh.AddNavMeshData(definition.BakedNavMesh);
            }

            if (!_standInLogged)
            {
                _standInLogged = true;
                Debug.Log("[StandIn] no zone NavMesh data");
            }

            return default(NavMeshDataInstance);
        }

        // Unity's overloaded == makes a destroyed object read as null, so a lost object logs "missing".
        private void LogInstanceIds(string label)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            NetworkManager manager = NetworkManager.Singleton;
            string host = _hostObject != null ? _hostObject.GetInstanceID().ToString() : "missing";
            string network = manager != null ? manager.gameObject.GetInstanceID().ToString() : "missing";
            Debug.Log("[ZoneServer] " + label + ": host object id=" + host + ", NetworkManager object id=" + network);
#endif
        }
    }
}
