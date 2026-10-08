#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// Manual-verification-only scaffolding for Story 013's Test Evidence pass
    /// (AC-LS-46/47/48 require a human screenshot + lead sign-off — this class exists only to
    /// make that pass possible). No player/session spawn system exists yet in this codebase, so
    /// there is otherwise no way to get a <see cref="CharacterStats"/>/<see cref="LevelingService"/>
    /// instance wired to <see cref="LevelingHudController"/> in a live Play-mode session.
    /// </summary>
    /// <remarks>
    /// <para>Compiled out of release Player builds (<c>UNITY_EDITOR || DEVELOPMENT_BUILD</c>),
    /// matching this codebase's established fault-injection/test-seam guard pattern (see
    /// <c>LevelingService.TestOnly_ThrowDuringRespecStep2</c>). This is throwaway test-local
    /// state — a real player-spawn system replaces every one of these constructor calls, it does
    /// not extend this class.</para>
    /// <para>Builds its own tiny on-screen debug button row directly under the shared
    /// <see cref="UIDocument"/>'s root (not under <c>HUD_Root</c>) via C#, not UXML — deliberate,
    /// signalling this is not production HUD content.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(LevelingHudController))]
    public sealed class LevelingHudManualTestHarness : MonoBehaviour
    {
        private static readonly EntityID TestEntityId = new EntityID(9001);
        private const byte TestClassType = 0;

        private UIDocument _uiDocument;
        private LevelingHudController _hudController;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private LevelingService _levelingService;
        private List<int> _xpThresholds;
        private ClassRegistry _classRegistry;

        private void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            _hudController = GetComponent<LevelingHudController>();

            BuildTestLevelingSystem();
            _hudController.Initialize(_stats, _levelingService, _xpThresholds, _classRegistry, TestEntityId);
            BuildDebugButtonRow();
        }

        private void BuildTestLevelingSystem()
        {
            // Index 0..61 -- xpThresholds[level+1] must be readable up to level=60 per
            // LevelingService.GetExperienceThreshold's index = level + 1 contract.
            _xpThresholds = new List<int>(62);
            for (int i = 0; i <= 61; i++)
                _xpThresholds.Add(i * 500); // simple linear test table -- not production data (Story 010 owns the real table).

            _classRegistry = new ClassRegistry();
            // Non-zero auto-alloc AND FreePointsPerLevel so respec floors sit visibly above the
            // level-1 baseline of 10, and heldFreePoints has something to display.
            _classRegistry.RegisterClass(TestClassType, new ClassDefinition(
                strengthAutoAlloc: 1, dexterityAutoAlloc: 1, vitalityAutoAlloc: 1, intelligenceAutoAlloc: 1,
                freePointsPerLevel: 2));

            _levelingService = new LevelingService(_xpThresholds, _classRegistry);
            _stats = new IronGrind.CharacterStats.CharacterStats(_levelingService);
            _levelingService.AttachCharacterStats(_stats);
            _levelingService.RegisterPlayerEntity(TestEntityId);
            _levelingService.InitializeAtL1(TestEntityId, TestClassType);
        }

        /// <summary>
        /// Directly sets Level + Experience to "freshly arrived at <paramref name="targetLevel"/>,
        /// zero progress toward next" -- test-setup only positioning (bypasses the real
        /// level-up sequence's auto-alloc/HP-MP-restore on purpose, the same way
        /// LevelingService.RestoreLevelingState's own load path bypasses it for persistence
        /// loads). Legitimate public CharacterStats API calls, not a Leveling System modification.
        /// </summary>
        private void ForceLevel(int targetLevel)
        {
            _stats.SetBaseStat(TestEntityId, StatID.Level, targetLevel);
            _stats.SetBaseStat(TestEntityId, StatID.Experience, _xpThresholds[targetLevel]);
        }

        private void CrossIntoNextLevel(int fromLevel, int levelsToGain)
        {
            ForceLevel(fromLevel);
            int targetLevel = fromLevel + levelsToGain;
            int amount = _xpThresholds[targetLevel] - _xpThresholds[fromLevel] + 1;
            _stats.AddExperience(TestEntityId, amount);
        }

        private void BuildDebugButtonRow()
        {
            var row = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 8,
                    bottom = 8,
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                },
            };

            row.Add(MakeDebugButton("AC-LS-46: Normal Level-Up (L5->L6)", () => CrossIntoNextLevel(5, 1)));
            row.Add(MakeDebugButton("AC-LS-46: Tier-Transition (L19->L20)", () => CrossIntoNextLevel(19, 1)));
            row.Add(MakeDebugButton("AC-LS-46: Consecutive (+3, L10->L13)", () => CrossIntoNextLevel(10, 3)));
            row.Add(MakeDebugButton("AC-LS-47: Bring to L60 (MAX)", () => CrossIntoNextLevel(59, 1)));
            row.Add(MakeDebugButton("AC-LS-48: Open Respec Screen", () => _hudController.OpenRespecScreen(TestClassType)));

            _uiDocument.rootVisualElement.Add(row);
        }

        private static Button MakeDebugButton(string label, System.Action onClick)
        {
            var button = new Button(onClick) { text = label };
            // Debug-only sizing -- UI Toolkit's default Button style renders too small to
            // read/tap reliably at typical Editor Game view resolutions/zoom levels. (This was
            // originally attributed to HudBootstrap.cs's PanelSettings.scaleMode, which has
            // since been changed from ConstantPhysicalSize to ConstantPixelSize for unrelated
            // reasons -- that change did not fix this button-sizing issue on its own, which is
            // why explicit sizing is still needed here regardless of scale mode.) Not production
            // HUD styling -- this harness is compiled out of release/Player builds entirely (see
            // the #if guard at the top of this file).
            button.style.marginRight = 6;
            button.style.marginBottom = 6;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.paddingTop = 8;
            button.style.paddingBottom = 8;
            button.style.fontSize = 16;
            button.style.minWidth = 160;
            button.style.minHeight = 36;
            button.style.whiteSpace = WhiteSpace.Normal;
            return button;
        }
    }
}
#endif
