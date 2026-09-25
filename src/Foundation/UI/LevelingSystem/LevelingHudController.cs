using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// Top-level Story 013 HUD entry point: owns the <see cref="UIDocument"/> reference and the
    /// audio fields that must live on a <see cref="MonoBehaviour"/> to be serialized in the
    /// Inspector, and constructs/owns the plain-C# presenters
    /// (<see cref="PlayerResourceClusterPresenter"/>, <see cref="LevelUpOverlayPresenter"/>,
    /// <see cref="RespecScreenPresenter"/>) that do the real work.
    /// </summary>
    /// <remarks>
    /// <para><b>Forward-dependency wiring seam.</b> No player/session spawn system exists yet in
    /// this codebase (confirmed via repo search) — there is no automatic way for this component
    /// to discover which <see cref="CharacterStats"/>/<see cref="LevelingService"/>/
    /// <see cref="EntityID"/> it should track. <see cref="Initialize"/> mirrors the same
    /// "caller-supplied state for a forward dependency" idiom already established by
    /// <c>LevelingService.RegisterPlayerEntity</c> / <c>AttachCharacterStats</c> — a future
    /// session/player-spawn system (or, today,
    /// <see cref="LevelingHudManualTestHarness"/>) must call it once after both this component
    /// and the supplied services exist.</para>
    /// <para>Deliberately NOT subscribing/wiring in <c>OnEnable</c> (the ADR-005 "register in
    /// OnEnable, unregister in OnDisable" guidance assumes dependencies already exist at
    /// <c>OnEnable</c> time — they don't here). Subscription happens inside the presenters'
    /// constructors, invoked from <see cref="Initialize"/>; teardown happens in
    /// <see cref="OnDisable"/> via each presenter's <see cref="System.IDisposable.Dispose"/>.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LevelingHudController : MonoBehaviour
    {
        [Header("Wiring (ADR-005) — assigned by the HudBootstrap editor utility, or manually")]
        [SerializeField] private UIDocument _uiDocument;

        [Header("Story 013 AC-LS-46 — audio (left unassigned pending real asset delivery; no Assets/Audio/ folder exists yet in this project)")]
        [SerializeField] private AudioClip _levelUpChime;
        [SerializeField] private AudioClip _tierTransitionChime;
        [SerializeField] private AudioSource _audioSource;

        private PlayerResourceClusterPresenter _resourceClusterPresenter;
        private LevelUpOverlayPresenter _levelUpOverlayPresenter;
        private RespecScreenPresenter _respecScreenPresenter;

        private bool _initialized;

        private void Start()
        {
            if (_uiDocument != null)
                HudSafeAreaApplier.Apply(_uiDocument.rootVisualElement.Q<VisualElement>("HUD_Root"));
        }

        /// <summary>
        /// Wires this controller to a real (or test-harness-constructed) Leveling System
        /// instance. Safe to call more than once — a prior wiring is torn down first.
        /// </summary>
        /// <param name="stats">The tracked entity's stat container.</param>
        /// <param name="levelingService">The Leveling System instance the tracked entity belongs to.</param>
        /// <param name="xpThresholds">
        /// The SAME XP threshold table passed into <c>new LevelingService(xpThresholds, ...)</c>
        /// — see <see cref="PlayerResourceClusterPresenter"/>'s remarks for why the UI layer
        /// needs its own reference to this table.
        /// </param>
        /// <param name="classRegistry">
        /// The SAME <see cref="IClassRegistry"/> passed into <c>new LevelingService(...)</c> —
        /// see <see cref="RespecScreenPresenter"/>'s remarks for why the respec floor
        /// computation needs it directly.
        /// </param>
        /// <param name="trackedEntityId">The locally-controlled player entity this HUD displays.</param>
        public void Initialize(
            IronGrind.CharacterStats.CharacterStats stats,
            LevelingService levelingService,
            IReadOnlyList<int> xpThresholds,
            IClassRegistry classRegistry,
            EntityID trackedEntityId)
        {
            if (_initialized)
                ShutdownPresenters();

            VisualElement root = _uiDocument.rootVisualElement;

            var xpBarFill = root.Q<VisualElement>("XPBarFill");
            var levelBadge = root.Q<Label>("LevelBadge");
            var overlayRoot = root.Q<VisualElement>("LevelUpOverlay");
            var levelNumberLabel = root.Q<Label>("LevelUpNumberLabel");
            var floatingTextLabel = root.Q<Label>("LevelUpFloatingText");
            var respecRoot = root.Q<VisualElement>("RespecScreenRoot");

            _resourceClusterPresenter = new PlayerResourceClusterPresenter(
                xpBarFill, levelBadge, stats, xpThresholds, trackedEntityId);

            _levelUpOverlayPresenter = new LevelUpOverlayPresenter(
                overlayRoot, levelNumberLabel, floatingTextLabel, xpBarFill,
                levelingService, _resourceClusterPresenter, _audioSource,
                _levelUpChime, _tierTransitionChime, trackedEntityId);

            _respecScreenPresenter = new RespecScreenPresenter(
                respecRoot, stats, levelingService, classRegistry, trackedEntityId);

            _initialized = true;
        }

        /// <summary>
        /// Opens the respec screen for the currently tracked entity (AC-LS-48 entry point —
        /// today only reachable via Story 007's mock-backed trigger / the manual test harness;
        /// the real Inventory System Phase 1 reservation flow is not yet built).
        /// </summary>
        public void OpenRespecScreen(byte classType) => _respecScreenPresenter?.Open(classType);

        private void OnDisable() => ShutdownPresenters();

        private void ShutdownPresenters()
        {
            _resourceClusterPresenter?.Dispose();
            _levelUpOverlayPresenter?.Dispose();
            _respecScreenPresenter?.Dispose();
            _resourceClusterPresenter = null;
            _levelUpOverlayPresenter = null;
            _respecScreenPresenter = null;
            _initialized = false;
        }
    }
}
