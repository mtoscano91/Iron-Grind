using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// AC-LS-48: Respec Screen — auto-alloc floor values in a muted color (never editable),
    /// a redistributable pool counter, commit disabled until the pool is fully allocated, a
    /// Confirm-only (no Cancel) modal, and a projected derived-stats preview column. Calls the
    /// real <see cref="LevelingService.TryApplyRespec"/> on confirm — never reimplements respec
    /// logic itself.
    /// </summary>
    /// <remarks>
    /// <para><b>Scope.</b> This is the minimum needed to make the respec screen functional and
    /// demonstrable (Story 013's own instruction) — not the full everyday Stat Screen `+`-button
    /// allocation UX (that is explicitly out of this story's AC-gated scope).</para>
    /// <para><b>classType / <see cref="IClassRegistry"/> are caller-supplied.</b> The CR-4.3
    /// floor formula (<c>10 + (level-1) * increment</c>) needs the entity's per-class auto-alloc
    /// increments, which live behind <c>LevelingService</c>'s PRIVATE <c>_classRegistry</c> field
    /// and PRIVATE <c>GetClassType</c> lookup — there is no public accessor for either, and
    /// widening them was out of scope. <see cref="Open"/> therefore takes the SAME
    /// <see cref="IClassRegistry"/> instance and <c>classType</c> byte the caller already passed
    /// into <c>new LevelingService(...)</c> / <c>RegisterPlayerClassType</c>, mirroring the
    /// established "caller-supplied state for a forward dependency" idiom already used elsewhere
    /// in this codebase (e.g. <see cref="IClassRegistry"/> itself). Today the only real caller of
    /// this screen is Story 007's own mock-backed respec entry point, which already holds this
    /// data — this is not a new mocking burden.</para>
    /// <para><b>Two-step confirmation.</b> "Commit button disabled while any redistributable
    /// point remains unallocated" + "modal confirmation with Confirm only, no Cancel" is read
    /// literally as TWO steps: (1) the main Commit button becomes enabled only once the pool is
    /// fully allocated, and clicking it OPENS a confirmation modal (not an immediate commit); (2)
    /// that modal's single Confirm button performs the actual
    /// <see cref="LevelingService.TryApplyRespec"/> call. Flagged as this story's own reading of
    /// the two GDD lines together, not a literal single-sentence spec.</para>
    /// </remarks>
    public sealed class RespecScreenPresenter : IDisposable
    {
        private static readonly StatID[] PrimaryStats =
        {
            StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence,
        };

        private readonly VisualElement _root;
        private readonly VisualElement _confirmModal;
        private readonly Label _poolLabel;
        private readonly Label _heldFreePointsLabel;
        private readonly Button _commitButton;
        private readonly Button _confirmButton;

        // Per-stat row widgets, keyed by StatID.
        private readonly Dictionary<StatID, Label> _floorLabels = new Dictionary<StatID, Label>();
        private readonly Dictionary<StatID, Label> _currentLabels = new Dictionary<StatID, Label>();
        private readonly Dictionary<StatID, Button> _minusButtons = new Dictionary<StatID, Button>();
        private readonly Dictionary<StatID, Button> _plusButtons = new Dictionary<StatID, Button>();

        // Named per-stat delegates (code-review fix) -- kept so Dispose() can unsubscribe them
        // the same way _commitButton/_confirmButton already are. A bare lambda closure
        // (() => OnMinusClicked(capturedStat)) cannot be unsubscribed with -=, which meant a
        // second LevelingHudController.Initialize() call (a documented, re-entrant contract of
        // that class, e.g. for a future respawn/reconnect system) would leave the OLD
        // presenter's handlers still firing alongside the new one on every +/- click.
        private readonly Dictionary<StatID, Action> _minusHandlers = new Dictionary<StatID, Action>();
        private readonly Dictionary<StatID, Action> _plusHandlers = new Dictionary<StatID, Action>();

        private readonly Label _previewMaxHp;
        private readonly Label _previewMaxMp;
        private readonly Label _previewAttackPower;
        private readonly Label _previewDefense;
        private readonly Label _previewMagicDefense;
        private readonly Label _previewCritChance;
        private readonly Label _previewAttackSpeed;

        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly LevelingService _levelingService;
        private readonly IClassRegistry _classRegistry;
        private readonly EntityID _entityId;

        // Session state, populated by Open(); valid only while the screen is visible.
        private readonly Dictionary<StatID, int> _floors = new Dictionary<StatID, int>();
        private readonly Dictionary<StatID, int> _allocation = new Dictionary<StatID, int>();
        private int _poolRemaining;
        private int _level;
        private bool _disposed;

        public RespecScreenPresenter(
            VisualElement respecScreenRoot,
            IronGrind.CharacterStats.CharacterStats stats,
            LevelingService levelingService,
            IClassRegistry classRegistry,
            EntityID entityId)
        {
            _root = respecScreenRoot ?? throw new ArgumentNullException(nameof(respecScreenRoot));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _levelingService = levelingService ?? throw new ArgumentNullException(nameof(levelingService));
            _classRegistry = classRegistry ?? throw new ArgumentNullException(nameof(classRegistry));
            _entityId = entityId;

            _confirmModal = RequireElement<VisualElement>("RespecConfirmModal");
            _poolLabel = RequireElement<Label>("RespecPoolLabel");
            _heldFreePointsLabel = RequireElement<Label>("RespecHeldFreePointsLabel");
            _commitButton = RequireElement<Button>("RespecCommitButton");
            _confirmButton = RequireElement<Button>("RespecConfirmButton");

            _previewMaxHp = RequireElement<Label>("RespecPreviewMaxHP");
            _previewMaxMp = RequireElement<Label>("RespecPreviewMaxMP");
            _previewAttackPower = RequireElement<Label>("RespecPreviewAttackPower");
            _previewDefense = RequireElement<Label>("RespecPreviewDefense");
            _previewMagicDefense = RequireElement<Label>("RespecPreviewMagicDefense");
            _previewCritChance = RequireElement<Label>("RespecPreviewCritChance");
            _previewAttackSpeed = RequireElement<Label>("RespecPreviewAttackSpeed");

            foreach (StatID stat in PrimaryStats)
            {
                string name = stat.ToString();
                _floorLabels[stat] = RequireElement<Label>($"{name}_FloorLabel");
                _currentLabels[stat] = RequireElement<Label>($"{name}_CurrentLabel");
                _minusButtons[stat] = RequireElement<Button>($"{name}_MinusButton");
                _plusButtons[stat] = RequireElement<Button>($"{name}_PlusButton");

                // Capture stat by value for the closures below (foreach variable capture is
                // per-iteration in modern C#, but the local copy keeps intent explicit). Stored
                // as named delegates (not just += directly) so Dispose() can unsubscribe them.
                StatID capturedStat = stat;
                Action minusHandler = () => OnMinusClicked(capturedStat);
                Action plusHandler = () => OnPlusClicked(capturedStat);
                _minusHandlers[stat] = minusHandler;
                _plusHandlers[stat] = plusHandler;
                _minusButtons[stat].clicked += minusHandler;
                _plusButtons[stat].clicked += plusHandler;
            }

            _commitButton.clicked += OnCommitClicked;
            _confirmButton.clicked += OnConfirmClicked;
        }

        /// <summary>
        /// <c>_root.Q&lt;T&gt;(name)</c>, throwing a clear, named exception instead of leaving a
        /// UXML naming typo to surface as a raw <see cref="NullReferenceException"/> the first
        /// time the missing element is used (code-review suggestion).
        /// </summary>
        private T RequireElement<T>(string name) where T : VisualElement
        {
            T element = _root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"[RespecScreenPresenter] Required element '{name}' ({typeof(T).Name}) not found under the respec screen root — check UI_RespecScreen.uxml for a naming mismatch.");
            return element;
        }

        /// <summary>
        /// Opens the respec screen for the tracked entity. <paramref name="classType"/> is
        /// caller-supplied — see class remarks for why (no public classType accessor on
        /// <see cref="LevelingService"/>).
        /// </summary>
        public void Open(byte classType)
        {
            _level = _stats.GetBaseStat(_entityId, StatID.Level);
            _classRegistry.TryGetClass(classType, out ClassDefinition def); // unregistered -> all-zero increments, same fallback LevelingService itself uses.

            _floors.Clear();
            _allocation.Clear();
            int totalPoints = 0;
            int totalFloor = 0;

            foreach (StatID stat in PrimaryStats)
            {
                int increment = LevelingFormulaPreview.GetAutoAllocIncrement(def, stat);
                int floor = LevelingFormulaPreview.GetRespecFloor(_level, increment);
                int current = _stats.GetBaseStat(_entityId, stat);

                _floors[stat] = floor;
                _allocation[stat] = floor; // Start every stat at its floor; extra points start unallocated in the pool.

                totalPoints += current;
                totalFloor += floor;

                _floorLabels[stat].text = $"Floor: {floor}";
            }

            // Redistributable pool = current total investment - sum of floors. TryApplyRespec
            // itself does not enforce sum conservation (it only checks each stat's floor); the
            // "pool" concept — preserving total invested points while letting the player choose
            // where they land — is this screen's own UX rule (AC-LS-48), not a service-layer rule.
            _poolRemaining = Mathf.Max(0, totalPoints - totalFloor);

            _heldFreePointsLabel.text = $"Held Free Points (separate, not part of this pool): {_levelingService.GetHeldFreePoints(_entityId)}";

            _confirmModal.style.display = DisplayStyle.None;
            _root.style.display = DisplayStyle.Flex;

            RefreshUi();
        }

        public void Close()
        {
            _root.style.display = DisplayStyle.None;
            _confirmModal.style.display = DisplayStyle.None;
        }

        private void OnMinusClicked(StatID stat)
        {
            if (_allocation[stat] <= _floors[stat]) return; // Floor is never editable below its value.
            _allocation[stat]--;
            _poolRemaining++;
            RefreshUi();
        }

        private void OnPlusClicked(StatID stat)
        {
            if (_poolRemaining <= 0) return;
            _allocation[stat]++;
            _poolRemaining--;
            RefreshUi();
        }

        private void RefreshUi()
        {
            _poolLabel.text = $"Redistributable Points Remaining: {_poolRemaining}";

            foreach (StatID stat in PrimaryStats)
                _currentLabels[stat].text = _allocation[stat].ToString();

            // AC-LS-48 — commit disabled while any redistributable point remains unallocated.
            _commitButton.SetEnabled(_poolRemaining == 0);

            float tier = LevelingService.GetLevelTierMultiplier(_level);
            var preview = LevelingFormulaPreview.ComputeDerivedStatsPreview(
                _allocation[StatID.Strength], _allocation[StatID.Dexterity],
                _allocation[StatID.Vitality], _allocation[StatID.Intelligence], tier);

            _previewMaxHp.text = $"MaxHP: {preview.MaxHP}";
            _previewMaxMp.text = $"MaxMP: {preview.MaxMP}";
            _previewAttackPower.text = $"AttackPower: {preview.AttackPower}";
            _previewDefense.text = $"Defense: {preview.Defense}";
            _previewMagicDefense.text = $"MagicDefense: {preview.MagicDefense}";
            _previewCritChance.text = $"CritChance: {preview.CritChance:P1}";
            _previewAttackSpeed.text = $"AttackSpeedMultiplier: {preview.AttackSpeedMultiplier:F3}";
        }

        private void OnCommitClicked()
        {
            if (_poolRemaining != 0) return; // Defensive — button should already be disabled.
            _confirmModal.style.display = DisplayStyle.Flex;
        }

        private void OnConfirmClicked()
        {
            var newTotals = new Dictionary<StatID, int>(PrimaryStats.Length);
            foreach (StatID stat in PrimaryStats)
                newTotals[stat] = _allocation[stat];

            try
            {
                _levelingService.TryApplyRespec(_entityId, newTotals);
                Close();
            }
            catch (Exception ex)
            {
                // TryApplyRespec can throw on a floor violation or malformed newTotals; this
                // screen's own +/- logic should make that unreachable in practice (floors are
                // never editable below their value), but the call is still wrapped defensively
                // rather than trusting the UI-side guard alone.
                Debug.LogError($"[RespecScreenPresenter] TryApplyRespec failed for entity {_entityId}: {ex}");
                _confirmModal.style.display = DisplayStyle.None;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _commitButton.clicked -= OnCommitClicked;
            _confirmButton.clicked -= OnConfirmClicked;

            // Code-review fix: the per-stat +/- buttons ARE unsubscribed here, using the named
            // delegates stored in _minusHandlers/_plusHandlers at construction time. The
            // previous assumption ("this presenter's lifetime is 1:1 with the respec screen's
            // VisualElement subtree") was wrong — LevelingHudController.Initialize() is
            // documented as re-callable against the SAME UXML-loaded elements (e.g. for a
            // future respawn/reconnect system), which would have left a disposed presenter's
            // stale handlers still firing alongside a newly-constructed one on every click.
            foreach (StatID stat in PrimaryStats)
            {
                _minusButtons[stat].clicked -= _minusHandlers[stat];
                _plusButtons[stat].clicked -= _plusHandlers[stat];
            }
        }
    }
}
