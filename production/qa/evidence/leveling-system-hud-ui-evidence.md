# Test Evidence: Leveling System Story 013 — HUD/UI Display & Manual Verification

> **Story**: `production/epics/leveling-system/story-013-hud-ui-display.md`
> **Story Type**: Visual/Feel
> **Verified**: 2026-09-24
> **Verified by**: Manuel Toscano, in a live Unity Editor Play Mode session, using the debug test harness (`LevelingHudManualTestHarness`, compiled out of release builds)
> **Lead sign-off**: Manuel Toscano (project owner) — confirmed "looks great" for all 3 ACs after the fixes below were applied

---

## Method

The debug harness adds 5 on-screen buttons that drive the real, unmodified production code path for each scenario (traced during code review — `CrossIntoNextLevel` calls the real `CharacterStats.AddExperience` → `LevelingService.NotifyExperienceCrossedThreshold`, the actual CR-2.9 consecutive-level-up loop; nothing is mocked or shortcut for the trigger itself). `ForceLevel` is test-setup-only positioning, analogous to a persistence-load path, and does not participate in the mechanism under test.

Verification required several rounds of real bug-fixing before the UI rendered/behaved correctly — see `production/session-state/active.md`'s Story 013 entries for the full debugging trail (a `CharacterStats` namespace/class ambiguity, a `PanelSettings` scale-mode issue, a genuine UI Toolkit auto-height layout bug across nested containers, a `<ui:Instance>` full-screen sizing gap, and two rounds of an invalid-XML-comment mistake). All are fixed and confirmed via this verification pass.

---

## AC-LS-46 — Level-up overlay parity (normal vs. tier-transition)

**Confirmed via Play Mode**:
- Normal level-up (button 1, L5→L6): brief overlay flash, level number animates in with overshoot (scale 0→1.2→1.0), holds ~1.5s.
- Tier-transition (button 2, L19→L20): visually **identical** overlay intensity to the normal case — no reviewer-detectable difference in flash brightness, scale animation, or overlay style. Only the hold duration differs (~2.5s vs ~1.5s).
- Consecutive level-up (button 3, +3 levels L10→L13): overlay plays **once**, showing the final level (13) directly — no flicker through intermediate levels (11, 12).

**Confirmed via code trace, not separately re-watched in the moment** (both are implemented, self-contained behaviors triggered by the same button clicks above — accepted as covered based on reading `LevelUpOverlayPresenter.PlayOverlay`, not a separate manual pass):
- XP bar "fills-then-flashes-white-then-resets" sequence (`LevelUpOverlayPresenter.cs` — sets fill to 1.0, flashes `backgroundColor` to white, then reverts to the USS class color and calls `PlayerResourceClusterPresenter.Render()` to show the real post-level-up value).
- Floating "+Level 13!" text (`_floatingTextLabel.text = $"+Level {finalLevel}!"`, rendered alongside the level number).

**Not tested — out of this story's reach**: audio (same-instrument-longer-sustain for tier transitions vs. normal). No audio assets exist anywhere in this project yet (confirmed via repo search); `_levelUpChime`/`_tierTransitionChime` fields are present and wired but left unassigned pending real asset delivery from the audio team. This is a known, accepted gap, not a defect.

**Also flagged, not implemented, out of AC-LS-46's literal scope**: the Implementation Notes describe a "tap XP bar for tooltip" feature (current XP / time-to-level) that was never built in this story. Logged as a gap for the story owner to decide (implement later, formally defer, or strike from the GDD) — the XP bar elements are also `PickingMode.Ignore`, which would need to be removed from that specific element if this is ever implemented.

## AC-LS-47 — XP bar L60 "MAX" state

**Confirmed via Play Mode**: bringing the entity to L60 (button 4) shows the level badge as **"MAX"** (not a number) and the XP bar fill locked at 1.0, static, no XP text/tooltip.

**Confirmed via code trace, not separately watched during the transition moment itself**: `PlayerResourceClusterPresenter.Render()` checks `level >= MaxLevel` and returns before ever evaluating the XP fill formula — architecturally, no near-empty-bar artifact is possible during the L59→L60 transition via this code path, regardless of write ordering.

## AC-LS-48 — Respec screen floor/commit behavior

**Confirmed via Play Mode** (button 5, opens respec screen):
- Floor values render distinctly from redistributable values (verified functionally: the `-` button on any attribute stops decrementing once `_allocation[stat] <= _floors[stat]` — the floor is never editable below its value) and are rendered in the muted secondary-text color per the USS (`--color-secondary-text`).
- "Redistributable Points Remaining" counter updates live as `+`/`-` are clicked.
- **Commit** button stays disabled (greyed out, unclickable) until the counter reaches exactly 0.
- Clicking Commit (once enabled) opens a second modal with **only a Confirm button** — no Cancel — matching the GDD's deliberate reservation/TTL rationale.
- Clicking Confirm calls the real `LevelingService.TryApplyRespec` and closes both modals.

---

## Fixes applied during this verification pass (see code review + session state for full detail)

- `RespecScreenPresenter`'s per-stat +/- button handlers were leaking (unsubscribable lambda closures) — fixed with named delegates, unsubscribed in `Dispose()`.
- `HUD_Root`, `HUD_Overlay`, and the respec screen's `<ui:Instance>` wrapper were all full-screen elements missing `PickingMode.Ignore` — would have silently blocked all touch input project-wide once real touch gameplay exists beneath this HUD. Fixed.
- Respec screen's +/- buttons resized from 28×24px to 48×48px to meet this project's mobile touch-target minimum.
- `PanelSettings.clearDepthStencil` now explicitly set to `false` per ADR-005's required-config table.

## Screenshots

Not attached to this file. Visual confirmation was done live and interactively across several iterations (documented in the chat transcript from this Story 013 implementation session, 2026-09-24) rather than captured as static images at each step. If a durable visual record is needed later, re-run the 5 debug-harness buttons and capture at that time — the harness remains available in any Development/Editor build.
