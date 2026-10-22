using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneScene"/> over <c>SceneManager.LoadSceneAsync(name, LoadSceneMode.Single)</c> (ADR-009
    /// Decision 1, Amendment 1 S6). An unknown scene name makes <c>LoadSceneAsync</c> return null; the loader then
    /// reports done and not succeeded, which is S-fail at S6. The zone has no baked NavMesh yet, so
    /// <see cref="AddNavMeshData"/> is a stand-in. Main thread only.
    /// </summary>
    /// <remarks>
    /// In the Editor and development builds it logs the instance ids of the host object and of the
    /// <c>NetworkManager</c> object before the load starts and once the load completed
    /// (<c>[ZoneServer] A1-3 before load</c> / <c>after load</c>), so a run can show both survived the Single-mode load
    /// (Story 045 AC-7).
    /// </remarks>
    /// <example>
    /// <code>
    /// IZoneScene scene = new UnitySceneLoader(gameObject);
    /// scene.BeginLoad("ZoneTemplate_Empty");
    /// while (!scene.IsDone) { yield return null; }
    /// bool ok = scene.Succeeded;
    /// </code>
    /// </example>
    public sealed class UnitySceneLoader : IZoneScene
    {
        private readonly GameObject _hostObject;
        private AsyncOperation _operation;
        private bool _nullOperation;
        private bool _afterLogged;
        private bool _standInLogged;

        /// <summary>Creates a loader that logs no instance ids.</summary>
        public UnitySceneLoader()
        {
        }

        /// <summary>Creates a loader that logs the host object's instance id around the load (dev builds and Editor).</summary>
        /// <param name="hostObject">The host's GameObject; may be null (then only the NetworkManager id is logged).</param>
        public UnitySceneLoader(GameObject hostObject)
        {
            _hostObject = hostObject;
        }

        /// <inheritdoc/>
        public void BeginLoad(string zoneTemplate)
        {
            _operation = null;
            _nullOperation = false;
            _afterLogged = false;
            LogInstanceIds("A1-3 before load");
            _operation = SceneManager.LoadSceneAsync(zoneTemplate, LoadSceneMode.Single);
            _nullOperation = _operation == null;
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
