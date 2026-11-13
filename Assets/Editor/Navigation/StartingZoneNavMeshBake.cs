using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace IronGrind.EditorTools.Navigation
{
    /// <summary>
    /// Editor-only bake of the starting zone's placeholder NavMesh (Navigation Story 008, CR-NAV-2: meshes are baked in
    /// the Editor, never at runtime). Adds or reuses a flat <c>PlaceholderGround</c> at y = 0 in
    /// <c>ZoneTemplate_Empty</c> covering the x/z walkable bounds of <c>ZoneDefinition_StartingZone</c>, bakes a
    /// <see cref="NavMeshSurface"/> on a root object at the origin, saves the data as
    /// <c>Assets/NavMesh/Zone0_StartingZone.asset</c> (a re-bake copies into the existing asset so its GUID is kept),
    /// assigns it to <c>ZoneDefinition_StartingZone._bakedNavMesh</c> and leaves the surface <b>disabled</b>, so only
    /// the scene loader (<c>UnitySceneLoader.AddNavMeshData</c>) adds the data at S6.
    /// </summary>
    /// <remarks>
    /// The verification report reloads the asset from disk and adds it temporarily; its counts include any other
    /// NavMesh data loaded at that moment (none in this scene).
    /// </remarks>
    /// <example>
    /// Batch mode (Editor closed):
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;repo&gt; -executeMethod IronGrind.EditorTools.Navigation.StartingZoneNavMeshBake.BakeBatch -logFile bake.log
    /// </code>
    /// Or the menu item <c>IronGrind/Navigation/Bake Starting Zone NavMesh</c> (Edit mode only).
    /// </example>
    public static class StartingZoneNavMeshBake
    {
        /// <summary>The starting zone's scene (ZoneSceneRegistry: template 0).</summary>
        public const string SCENE_PATH = "Assets/Scenes/Zones/ZoneTemplate_Empty.unity";

        /// <summary>The baked data asset (CR-NAV-2 naming <c>Zone{ZoneTemplateID}_{Name}.asset</c>).</summary>
        public const string NAVMESH_ASSET_PATH = "Assets/NavMesh/Zone0_StartingZone.asset";

        /// <summary>The definition that carries the baked mesh and the walkable bounds.</summary>
        public const string ZONE_DEFINITION_PATH = "Assets/data/Zones/ZoneDefinition_StartingZone.asset";

        /// <summary>The placeholder floor's name (a stand-in for Level Design).</summary>
        public const string FLOOR_NAME = "PlaceholderGround";

        /// <summary>The surface object's name.</summary>
        public const string SURFACE_NAME = "NavMeshSurface_Zone0";

        private const string ASSET_NAME = "Zone0_StartingZone";
        private const float UNITY_PLANE_SIZE = 10f;
        private const float REPORT_SAMPLE_RADIUS = 0.5f; // Movement's CR-MOV-7 search radius.
        private const string BAKED_FIELD = "_bakedNavMesh";
        private const string BOUNDS_MIN_FIELD = "_walkableBoundsMin";
        private const string BOUNDS_MAX_FIELD = "_walkableBoundsMax";
        private const string LOG_PREFIX = "[NavMeshBake]";

        /// <summary>Menu entry for a re-bake in the open Editor (Edit mode only; offers to save modified scenes).</summary>
        [MenuItem("IronGrind/Navigation/Bake Starting Zone NavMesh")]
        public static void BakeFromMenu()
        {
            Bake();
        }

        /// <summary>Batch-mode entry: bakes and exits with 0 on success, 1 on failure.</summary>
        public static void BakeBatch()
        {
            bool ok = false;
            try
            {
                ok = Bake();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: {e}");
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Runs the bake and logs a verification report from the asset as saved on disk.</summary>
        /// <returns>True when every step succeeded; false when refused (Play mode, save cancelled) or failed.</returns>
        public static bool Bake()
        {
            if (!CanOpenScene())
                return false;

            var definition = LoadDefinition();
            if (definition == null || !TryReadBounds(definition, out Vector3 min, out Vector3 max))
                return false;

            Scene scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            FindOrCreateFloor(scene, min, max);
            NavMeshSurface surface = FindOrCreateSurface(scene);

            // BuildNavMesh collects sources whether or not the surface is enabled and only adds the data when enabled.
            surface.enabled = false;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: BuildNavMesh produced no data");
                return false;
            }

            NavMeshData asset = SaveData(surface.navMeshData);
            surface.navMeshData = asset;
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);

            definition.FindProperty(BAKED_FIELD).objectReferenceValue = asset;
            definition.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: could not save {SCENE_PATH}");
                return false;
            }

            AssetDatabase.SaveAssets();
            return LogReport(surface, ReloadFromDisk(asset));
        }

        private static bool CanOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError($"{LOG_PREFIX} refused: exit Play mode before baking");
                return false;
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log($"{LOG_PREFIX} cancelled: modified scenes were not saved");
                return false;
            }

            return true;
        }

        private static SerializedObject LoadDefinition()
        {
            ScriptableObject definition = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ZONE_DEFINITION_PATH);
            if (definition == null)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: {ZONE_DEFINITION_PATH} not found");
                return null;
            }

            var so = new SerializedObject(definition);
            if (so.FindProperty(BAKED_FIELD) == null)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: {BAKED_FIELD} not found on {ZONE_DEFINITION_PATH}");
                return null;
            }

            return so;
        }

        private static bool TryReadBounds(SerializedObject definition, out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.zero;
            SerializedProperty minProp = definition.FindProperty(BOUNDS_MIN_FIELD);
            SerializedProperty maxProp = definition.FindProperty(BOUNDS_MAX_FIELD);
            if (minProp == null || maxProp == null)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: walkable bounds fields not found on {ZONE_DEFINITION_PATH}");
                return false;
            }

            min = minProp.vector3Value;
            max = maxProp.vector3Value;
            if (max.x <= min.x || max.z <= min.z)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: empty walkable bounds {min} .. {max}");
                return false;
            }

            return true;
        }

        // The floor covers the x/z walkable bounds at y = 0 (Story 008 AC1), whatever the bounds' y range.
        private static void FindOrCreateFloor(Scene scene, Vector3 min, Vector3 max)
        {
            GameObject floor = FindRoot(scene, FLOOR_NAME);
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = FLOOR_NAME;
                SceneManager.MoveGameObjectToScene(floor, scene);
            }

            var center = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
            floor.transform.SetPositionAndRotation(center, Quaternion.identity);
            floor.transform.localScale = new Vector3((max.x - min.x) / UNITY_PLANE_SIZE, 1f, (max.z - min.z) / UNITY_PLANE_SIZE);
            EditorUtility.SetDirty(floor);
        }

        private static NavMeshSurface FindOrCreateSurface(Scene scene)
        {
            GameObject go = FindRoot(scene, SURFACE_NAME);
            if (go == null)
            {
                go = new GameObject(SURFACE_NAME);
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            // The loader adds the data at the origin with no rotation, so the surface must sit there too.
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            NavMeshSurface surface = go.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = go.AddComponent<NavMeshSurface>();

            surface.agentTypeID = 0; // Humanoid: Unity defaults (radius 0.5, height 2).
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            return surface;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            return null;
        }

        private static NavMeshData SaveData(NavMeshData built)
        {
            if (!AssetDatabase.IsValidFolder("Assets/NavMesh"))
                AssetDatabase.CreateFolder("Assets", "NavMesh");

            NavMeshData existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NAVMESH_ASSET_PATH);
            if (existing != null)
            {
                // Keep the GUID so the ZoneDefinition reference survives a re-bake. The report reads the result back
                // from disk, so a copy that silently did nothing shows there.
                EditorUtility.CopySerialized(built, existing);
                existing.name = ASSET_NAME;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(built);
                return existing;
            }

            built.name = ASSET_NAME;
            AssetDatabase.CreateAsset(built, NAVMESH_ASSET_PATH);
            EditorUtility.SetDirty(built);
            return built;
        }

        // Drops the in-memory object and loads the asset again from the file the bake wrote.
        private static NavMeshData ReloadFromDisk(NavMeshData asset)
        {
            AssetDatabase.ImportAsset(NAVMESH_ASSET_PATH, ImportAssetOptions.ForceUpdate);
            Resources.UnloadAsset(asset);
            return AssetDatabase.LoadAssetAtPath<NavMeshData>(NAVMESH_ASSET_PATH);
        }

        private static bool LogReport(NavMeshSurface surface, NavMeshData reloaded)
        {
            if (reloaded == null)
            {
                Debug.LogError($"{LOG_PREFIX} FAILED: {NAVMESH_ASSET_PATH} did not reload from disk");
                return false;
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(surface.agentTypeID);
            var sb = new StringBuilder();
            sb.AppendLine($"{LOG_PREFIX} OK: {NAVMESH_ASSET_PATH} ({AssetDatabase.AssetPathToGUID(NAVMESH_ASSET_PATH)}), reloaded from disk");
            sb.AppendLine($"{LOG_PREFIX} agent type {surface.agentTypeID}: radius {settings.agentRadius}, height {settings.agentHeight}, slope {settings.agentSlope}, climb {settings.agentClimb}; geometry {surface.useGeometry}; surface enabled {surface.enabled}");

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(reloaded);
            try
            {
                AppendTriangulation(sb);
                AppendSample(sb, new Vector3(0f, 0f, 0f));
                AppendSample(sb, new Vector3(25f, 0f, -25f));
                AppendSample(sb, new Vector3(49.4f, 0f, 0f));
                AppendSample(sb, new Vector3(60f, 0f, 0f));
                AppendSample(sb, new Vector3(0f, 0f, -60f));
            }
            finally
            {
                instance.Remove();
            }

            Debug.Log(sb.ToString());
            return true;
        }

        private static void AppendTriangulation(StringBuilder sb)
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            foreach (Vector3 v in tri.vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            sb.AppendLine($"{LOG_PREFIX} added once: {tri.vertices.Length} vertices, {tri.indices.Length / 3} triangles, bounds {min.ToString("F3")} .. {max.ToString("F3")}");
        }

        private static void AppendSample(StringBuilder sb, Vector3 point)
        {
            bool hit = NavMesh.SamplePosition(point, out NavMeshHit navHit, REPORT_SAMPLE_RADIUS, NavMesh.AllAreas);
            sb.AppendLine($"{LOG_PREFIX} SamplePosition {point.ToString("F1")} r{REPORT_SAMPLE_RADIUS}: {(hit ? "hit " + navHit.position.ToString("F3") : "miss")}");
        }
    }
}
