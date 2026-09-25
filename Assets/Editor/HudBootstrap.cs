using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using IronGrind.UI.LevelingSystem;

namespace IronGrind.HudEditorTools
{
    /// <summary>
    /// One-time editor utility that materializes the <see cref="PanelSettings"/> asset and the
    /// scene's <see cref="UIDocument"/> GameObject for Story 013 (Leveling System HUD).
    /// </summary>
    /// <remarks>
    /// <para><b>Why an editor script, not a hand-authored .asset file.</b>
    /// <see cref="PanelSettings"/> is a <see cref="ScriptableObject"/>; its Unity 6.3 serialized
    /// YAML format is beyond LLM training data (ADR-005's own HIGH knowledge-risk
    /// classification) -- hand-writing that file directly risked producing a corrupt or
    /// subtly-wrong asset. This script instead constructs the asset programmatically via
    /// <see cref="ScriptableObject.CreateInstance{T}"/> + <see cref="AssetDatabase.CreateAsset"/>,
    /// which always produces a format Unity's own current version understands.</para>
    /// <para><b>Idempotent.</b> Both menu items check for an existing asset/GameObject before
    /// creating one -- re-running either command is harmless.</para>
    /// </remarks>
    public static class HudBootstrap
    {
        private const string PanelSettingsPath = "Assets/UI/HUD/HUD_PanelSettings.asset";
        private const string HudRootUxmlPath = "Assets/UI/HUD/HUD_Root.uxml";
        private const string HudRootGameObjectName = "HUD_Root";

        [MenuItem("Tools/HUD/Bootstrap HUD Scaffolding")]
        public static void BootstrapHudScaffolding()
        {
            PanelSettings panelSettings = GetOrCreatePanelSettings();
            GameObject hudGameObject = GetOrCreateHudGameObject(panelSettings);
            EnsureInputSystemUIInputModule();

            EditorUtility.SetDirty(hudGameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                "[HudBootstrap] HUD scaffolding ready: " +
                $"PanelSettings at '{PanelSettingsPath}' (clearColor=false, sortingOrder=0), " +
                $"GameObject '{HudRootGameObjectName}' in the active scene with UIDocument + LevelingHudController. " +
                "Remember to SAVE THE SCENE so this persists. " +
                "Run Tools/HUD/Add Leveling Manual Test Harness next if you want on-screen debug " +
                "buttons to trigger AC-LS-46/47/48 scenarios for the manual verification pass.");
        }

        [MenuItem("Tools/HUD/Add Leveling Manual Test Harness")]
        public static void AddManualTestHarness()
        {
            GameObject hudGameObject = GameObject.Find(HudRootGameObjectName);
            if (hudGameObject == null)
            {
                Debug.LogError(
                    $"[HudBootstrap] No '{HudRootGameObjectName}' GameObject found in the active scene. " +
                    "Run Tools/HUD/Bootstrap HUD Scaffolding first.");
                return;
            }

            if (hudGameObject.GetComponent<LevelingHudManualTestHarness>() == null)
                hudGameObject.AddComponent<LevelingHudManualTestHarness>();

            EditorUtility.SetDirty(hudGameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log(
                $"[HudBootstrap] LevelingHudManualTestHarness added to '{HudRootGameObjectName}'. " +
                "Enter Play Mode and use the on-screen debug buttons (bottom-left) to trigger " +
                "AC-LS-46 (normal / tier-transition / consecutive level-ups), AC-LS-47 (bring to L60), " +
                "and AC-LS-48 (open respec screen). Save the scene afterward if you want this to persist.");
        }

        private static PanelSettings GetOrCreatePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null)
                return existing;

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.clearColor = false; // REQUIRED: true erases the 3D scene beneath (ADR-005).
            panelSettings.clearDepthStencil = false; // Explicit per ADR-005's PanelSettings required-config table (code review).
            panelSettings.sortingOrder = 0;
            // ConstantPixelSize, not ConstantPhysicalSize: the latter depends on the runtime
            // correctly detecting screen DPI, which the Unity Editor's Game view frequently
            // cannot do reliably, silently falling back to a scale factor well below 1 and
            // rendering every element far smaller than authored (confirmed via manual testing
            // this session -- tiny debug buttons, overlapping respec-screen text). 1 USS px ==
            // 1 screen px at all times with this mode, matching this project's touch-target and
            // dp-based measurements exactly for Editor testing and predictable behavior in
            // general. Revisit only if/when profiling on a real target device shows a genuine
            // need for physical-size scaling (ADR-005's own "beyond LLM training data" caveat
            // on UI Toolkit specifics applies here).
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

            AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return panelSettings;
        }

        private static GameObject GetOrCreateHudGameObject(PanelSettings panelSettings)
        {
            GameObject hudGameObject = GameObject.Find(HudRootGameObjectName);
            if (hudGameObject == null)
                hudGameObject = new GameObject(HudRootGameObjectName);

            var uiDocument = hudGameObject.GetComponent<UIDocument>();
            if (uiDocument == null)
                uiDocument = hudGameObject.AddComponent<UIDocument>();

            uiDocument.panelSettings = panelSettings;

            var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudRootUxmlPath);
            if (visualTreeAsset != null)
                uiDocument.visualTreeAsset = visualTreeAsset;
            else
                Debug.LogWarning(
                    $"[HudBootstrap] Could not load '{HudRootUxmlPath}' -- has Unity imported it yet? " +
                    "Re-run this menu command after the Project window shows it importing successfully.");

            var hudController = hudGameObject.GetComponent<LevelingHudController>();
            if (hudController == null)
                hudController = hudGameObject.AddComponent<LevelingHudController>();

            // _uiDocument is a private [SerializeField] on LevelingHudController -- assigned via
            // SerializedObject rather than widening its visibility, per this project's
            // [SerializeField]-private-fields-only convention (Unity 6.3 compile error otherwise
            // if it were a property).
            var serializedController = new SerializedObject(hudController);
            var uiDocumentProperty = serializedController.FindProperty("_uiDocument");
            if (uiDocumentProperty != null)
            {
                uiDocumentProperty.objectReferenceValue = uiDocument;
                serializedController.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning(
                    "[HudBootstrap] Could not find LevelingHudController's '_uiDocument' serialized field. " +
                    "Assign the UIDocument component manually in the Inspector.");
            }

            if (hudGameObject.GetComponent<AudioSource>() == null)
                hudGameObject.AddComponent<AudioSource>();

            var serializedControllerAudio = new SerializedObject(hudController);
            var audioSourceProperty = serializedControllerAudio.FindProperty("_audioSource");
            if (audioSourceProperty != null && audioSourceProperty.objectReferenceValue == null)
            {
                audioSourceProperty.objectReferenceValue = hudGameObject.GetComponent<AudioSource>();
                serializedControllerAudio.ApplyModifiedPropertiesWithoutUndo();
            }

            return hudGameObject;
        }

        /// <summary>
        /// ADR-005 Migration Plan step 3: the scene's <see cref="EventSystem"/> must carry
        /// <see cref="InputSystemUIInputModule"/>, not the legacy <see cref="StandaloneInputModule"/>
        /// (which routes touch events differently and conflicts with UI Toolkit's
        /// PanelEventHandler). Creates an EventSystem if none exists; replaces a legacy module if
        /// found.
        /// </summary>
        private static void EnsureInputSystemUIInputModule()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemGameObject = new GameObject("EventSystem");
                eventSystem = eventSystemGameObject.AddComponent<EventSystem>();
                eventSystemGameObject.AddComponent<InputSystemUIInputModule>();
                Debug.Log("[HudBootstrap] Created EventSystem with InputSystemUIInputModule (none existed in the scene).");
                return;
            }

            var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                Object.DestroyImmediate(legacyModule);
                Debug.LogWarning(
                    "[HudBootstrap] Removed legacy StandaloneInputModule from the scene's EventSystem " +
                    "(ADR-005 requires InputSystemUIInputModule -- the legacy module conflicts with " +
                    "UI Toolkit's PanelEventHandler).");
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                Debug.Log("[HudBootstrap] Added InputSystemUIInputModule to the scene's existing EventSystem.");
            }
        }
    }
}
