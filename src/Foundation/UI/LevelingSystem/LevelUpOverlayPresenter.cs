using System;
using UnityEngine;
using UnityEngine.UIElements;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// AC-LS-46: drives the Level-Up overlay — brief non-blocking flash, level number animates
    /// in (scale 0→1.2→1.0), hold 1.5s normal / 2.5s tier-transition (the ONLY visual
    /// distinction between the two), XP bar fills-then-flashes-white-then-resets, floating
    /// "+Level [N]!" text, optional chime audio. Keyed exclusively to
    /// <see cref="ILevelingEventBroadcaster.OnLevelUp"/> — never to <c>OnStatChanged(Level)</c>
    /// (EC-LS-37).
    /// </summary>
    /// <remarks>
    /// <para><b>Consecutive level-up batching (Story 003 CR-2.9).</b>
    /// <c>LevelingService.NotifyExperienceCrossedThreshold</c> fires <c>OnLevelUp</c> once PER
    /// LEVEL GAINED, in ascending order, all synchronously within the same C# call stack (after
    /// its loop has fully completed — never mid-iteration, per that method's own doc comment).
    /// A 4-level jump therefore fires 4 separate <c>OnLevelUp</c> events before this presenter's
    /// handler ever returns control. The story requires cutting directly to the final state
    /// rather than animating each intermediate level, so this presenter coalesces: the FIRST
    /// event in a burst schedules a single deferred <see cref="FlushBatch"/> via
    /// <c>IVisualElementScheduler.ExecuteLater(0)</c> (fires on the next scheduler tick, after
    /// every synchronous firing in the current burst has already updated
    /// <see cref="_batchFinalLevel"/>); only that single deferred call actually plays the
    /// overlay, using the LAST level seen. This is Story 013's own interpretation of "cut
    /// directly to the final state" — the story does not literally specify a batching
    /// mechanism, so this is flagged as a resolved ambiguity, not a literal spec requirement.</para>
    /// <para><b>Tier-transition detection for a batch.</b> A single level-up's tier-transition
    /// status is unambiguous (does crossing this one level cross a
    /// <c>LevelingService.GetLevelTierMultiplier</c> boundary?). For a CONSECUTIVE batch, the
    /// story doesn't specify how tier vs. normal should be decided when the batch spans a tier
    /// wall. This presenter's resolution (own design call, flagged): compare the tier at the
    /// level BEFORE the batch started against the tier at the FINAL level reached — if they
    /// differ, the whole batch plays as a tier-transition (2.5s hold); otherwise normal (1.5s).</para>
    /// <para><b>Local player only.</b> hud.md's "Floating +Level [N]! text visible to nearby
    /// players" describes a world-space effect near the leveling player's avatar (UGUI
    /// world-space layer, per ADR-005's two-layer architecture) — a different system/story, out
    /// of this HUD-layer story's scope. This presenter reacts only to
    /// <paramref name="trackedEntityId"/> (the locally-controlled player), not to every
    /// <c>OnLevelUp</c> in the world. Flagged as a scoping decision, not a literal reading of
    /// hud.md's "nearby players" line.</para>
    /// <para><b>Audio.</b> No audio assets exist yet in this project (confirmed via repo glob —
    /// no <c>Assets/Audio/</c> folder). <see cref="_levelUpChime"/>/<see cref="_tierTransitionChime"/>
    /// are supplied by the caller (left unassigned by <c>LevelingHudController</c> pending real
    /// asset delivery); <see cref="AudioSource.PlayOneShot(AudioClip)"/> is only called when a
    /// clip is actually assigned — never blocks the visual sequence.</para>
    /// </remarks>
    public sealed class LevelUpOverlayPresenter : IDisposable
    {
        private const float NormalHoldSeconds = 1.5f;
        private const float TierHoldSeconds = 2.5f;
        private const int ScaleUpMs = 300;   // 0 -> 1.2
        private const int ScaleSettleMs = 200; // 1.2 -> 1.0
        private const int FadeInMs = 150;
        private const int FadeOutMs = 200;
        private const int XpFlashMs = 150;

        // Matches --color-xp-fill in USS_HUD_Theme.uss (#C4912A). Duplicated here only as a
        // fallback revert color for the transient white-flash inline override — the real
        // steady-state color always comes from the USS class, this is a cosmetic-only constant.
        private static readonly Color XpFillColor = new Color32(0xC4, 0x91, 0x2A, 0xFF);

        private readonly VisualElement _overlayRoot;
        private readonly Label _levelNumberLabel;
        private readonly Label _floatingTextLabel;
        private readonly VisualElement _xpBarFill;
        private readonly LevelingService _levelingService;
        private readonly PlayerResourceClusterPresenter _resourceClusterPresenter;
        private readonly AudioSource _audioSource;
        private readonly AudioClip _levelUpChime;
        private readonly AudioClip _tierTransitionChime;
        private readonly EntityID _trackedEntityId;

        private bool _batchPending;
        private int _batchStartLevel;
        private int _batchFinalLevel;
        private bool _disposed;

        public LevelUpOverlayPresenter(
            VisualElement overlayRoot,
            Label levelNumberLabel,
            Label floatingTextLabel,
            VisualElement xpBarFill,
            LevelingService levelingService,
            PlayerResourceClusterPresenter resourceClusterPresenter,
            AudioSource audioSource,
            AudioClip levelUpChime,
            AudioClip tierTransitionChime,
            EntityID trackedEntityId)
        {
            _overlayRoot = overlayRoot ?? throw new ArgumentNullException(nameof(overlayRoot));
            _levelNumberLabel = levelNumberLabel ?? throw new ArgumentNullException(nameof(levelNumberLabel));
            _floatingTextLabel = floatingTextLabel ?? throw new ArgumentNullException(nameof(floatingTextLabel));
            _xpBarFill = xpBarFill ?? throw new ArgumentNullException(nameof(xpBarFill));
            _levelingService = levelingService ?? throw new ArgumentNullException(nameof(levelingService));
            _resourceClusterPresenter = resourceClusterPresenter ?? throw new ArgumentNullException(nameof(resourceClusterPresenter));
            _audioSource = audioSource; // may be null — PlayOneShot guarded below
            _levelUpChime = levelUpChime; // may be null — pending real asset (see remarks)
            _tierTransitionChime = tierTransitionChime; // may be null
            _trackedEntityId = trackedEntityId;

            _levelingService.OnLevelUp += OnLevelUp;
        }

        private void OnLevelUp(LevelUpEventArgs args)
        {
            if (args.EntityId != _trackedEntityId) return;

            if (!_batchPending)
            {
                _batchPending = true;
                _batchStartLevel = args.NewLevel - 1;
                _overlayRoot.schedule.Execute(FlushBatch).ExecuteLater(0);
            }
            _batchFinalLevel = args.NewLevel;
        }

        private void FlushBatch()
        {
            _batchPending = false;
            PlayOverlay(_batchStartLevel, _batchFinalLevel);
        }

        private void PlayOverlay(int startLevel, int finalLevel)
        {
            bool isTierTransition =
                !Mathf.Approximately(
                    LevelingService.GetLevelTierMultiplier(finalLevel),
                    LevelingService.GetLevelTierMultiplier(startLevel));

            float holdSeconds = isTierTransition ? TierHoldSeconds : NormalHoldSeconds;
            AudioClip clip = isTierTransition ? _tierTransitionChime : _levelUpChime;
            if (clip != null && _audioSource != null)
                _audioSource.PlayOneShot(clip);

            // -- XP bar: "fills-then-flashes-white-then-resets" --
            _xpBarFill.style.scale = new StyleScale(new Scale(Vector3.one)); // "fills"
            _xpBarFill.style.backgroundColor = Color.white;                 // "flashes white"
            _xpBarFill.schedule.Execute(() =>
            {
                _xpBarFill.style.backgroundColor = StyleKeyword.Null; // fall back to USS .xp-bar-fill color
                _resourceClusterPresenter.Render(); // "resets" -- recompute the real post-level-up fill/badge
            }).ExecuteLater(XpFlashMs);

            // -- Overlay entrance: ~0.5s flash (fade-in + scale bounce) --
            _levelNumberLabel.text = finalLevel.ToString();
            _floatingTextLabel.text = $"+Level {finalLevel}!";
            _levelNumberLabel.style.scale = new StyleScale(new Scale(Vector3.zero));
            _overlayRoot.style.opacity = 0f;
            _overlayRoot.style.display = DisplayStyle.Flex;

            _overlayRoot.experimental.animation
                .Start(0f, 1f, FadeInMs, (element, value) => element.style.opacity = value);

            _levelNumberLabel.experimental.animation
                .Scale(1.2f, ScaleUpMs)
                .OnCompleted(() =>
                {
                    _levelNumberLabel.experimental.animation
                        .Scale(1.0f, ScaleSettleMs)
                        .OnCompleted(() => ScheduleHoldThenExit(holdSeconds));
                });
        }

        private void ScheduleHoldThenExit(float holdSeconds)
        {
            _overlayRoot.schedule.Execute(() =>
            {
                _overlayRoot.experimental.animation
                    .Start(1f, 0f, FadeOutMs, (element, value) => element.style.opacity = value)
                    .OnCompleted(() => _overlayRoot.style.display = DisplayStyle.None);
            }).ExecuteLater((long)(holdSeconds * 1000f));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _levelingService.OnLevelUp -= OnLevelUp;
        }
    }
}
