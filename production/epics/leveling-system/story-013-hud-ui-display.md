# Story 013: HUD/UI Display & Manual Verification

> **Epic**: Leveling System
> **Status**: Ready
> **Layer**: Core
> **Type**: Visual/Feel
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-013`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-005: HUD UI Framework (UI Toolkit) — this story's XP bar/level badge/stat screen are HUD-layer presentation, governed by ADR-005's UI Toolkit requirements. Confirm ADR-005 file exists and read its Decision/Implementation Guidelines before implementation.
**ADR Decision Summary**: HUD screen-space elements use UI Toolkit (`UIDocument`/`VisualElement`/UXML/USS); fill bars use `style.scale` on X axis with `transform-origin: left center` — never `fillAmount` or `style.width` percentage.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM (UI Toolkit, per ADR-005's HIGH-risk domain classification — verify current UI Toolkit APIs against `docs/engine-reference/unity/` before implementation)
**Engine Notes**: `VisualElement.transform` setter is deprecated (6.2) — use `style.translate`/`style.scale`. `[SerializeField]` on private fields only. USS syntax errors block import in 6.3.
**Performance**: Real budget applies — ADR-005's HUD update guardrail is **<0.3ms per tick at 60fps on iPhone SE 3rd gen (A15 Bionic)**, must be profiled on device before this story's HUD elements (XP bar, level badge) ship. Fill-bar updates must use `style.scale`, never `style.width` percentage (the latter triggers layout recalculation per tick and breaks this budget).

**Control Manifest Rules (Presentation layer, per ADR-005)**:
- Required: Fill bars via `style.scale` + `UsageHints.DynamicTransform`, `transform-origin: left center` — never `fillAmount`/`style.width` percentage
- Required: `HUD_PanelSettings.clearColor = false`
- Forbidden: `style.width` percentage for fill bars (breaks the <0.3ms HUD update budget)
- Forbidden: `VisualElement.transform` setter

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [ ] **AC-LS-46** [ADVISORY]: Tier-transition level-up uses the SAME overlay intensity as a normal level-up — only the level-number hold duration differs (1.5s normal, 2.5s tier). Audio: same instrument/character, slightly longer sustain/reverb — never louder, no stinger, no ambient pause. Lead sign-off required; screenshot + audio review saved to `production/qa/evidence/`.
- [ ] **AC-LS-47** [ADVISORY]: HUD XP bar at L60 — fill=1.0 static (formula bypassed via the explicit `Level==60` guard), "MAX" displayed instead of a number, no XP text. Screenshot saved to `production/qa/evidence/`.
- [ ] **AC-LS-48** [ADVISORY]: Respec screen — auto-alloc floor values shown in muted color; commit button disabled while any redistributable point remains unallocated.

---

## Implementation Notes

*Derived from the Visual/Audio Requirements and UI Requirements sections (leveling-system.md):*

- **XP Bar (HUD)**: fill = `(Experience − XpThreshold[Level]) / (XpThreshold[Level+1] − XpThreshold[Level])`, both operands cast to `float` before division. **L60 guard (CR-1.3/CR-5.5 implementer note)**: the formula must NOT be evaluated at L60 — after CR-2.2a clamps Experience, the raw formula produces `0.0 / (int.MaxValue − XpThreshold[60]) ≈ 0.0`, a near-empty bar. Special-case `Level==60`: render `fill=1.0` constant, bypass the formula entirely, show "MAX" instead of the level number, no XP text. Tapping the bar opens a tooltip (current XP / XP to next level, plus an estimated time-to-level if recent kill rate data is available).
- **Level Badge**: visible to all players in range, updates immediately on level-up (no animation delay on the badge itself, even though the overlay/floating-text animate). Must NOT display an intermediate level during a consecutive level-up (Story 003) — only the final level. Font size readable at combat distance; must not overlap the health bar.
- **Level-Up Event visuals** (fires on `OnLevelUp`, Story 003): brief non-blocking overlay flash (~0.5s), level number animates in (scale 0→1.2→1.0), hold ~1.5s normal / ~2.5s tier transition — **the longer hold is the ONLY visual distinction for tier walls, no additional effects**. XP bar fills-then-flashes-white-then-resets. On consecutive level-ups, cut directly to the final state — do not animate each intermediate level. Floating "+Level [N]!" text visible to nearby players. Sound: distinct clean chime for normal; same instrument with longer sustain/reverb (not louder, no stinger) for tier transitions; no music interruption ever.
- **Free Point Grant**: pulsing (non-modal) indicator on the stat screen entry point whenever `heldFreePoints > 0`; soft one-shot UI chime when points first become available.
- **Stat Screen**: shows Level, XP, held free points; each primary attribute shows current value + a `+` button active only while `heldFreePoints > 0`; each tap calls `AllocateFreePoint` (Story 005) immediately, no preview/confirm at this layer (the GDD's own UX note requires a client-side preview-and-confirm flow above the API on touch platforms — implement that flow here, not as a system-layer change); button disabled while `_levelingUpInProgress==true` (Story 003); F-3–F-9 values update immediately via `OnStatChanged` subscription.
- **Respec Screen**: opened after Inventory System Phase 1 reservation (Story 007, currently mock-only); auto-alloc floor in muted color (non-redistributable); redistributable pool counter at top; `heldFreePoints` shown separately, clearly not part of the pool; commit button disabled while any redistributable point is unallocated; modal confirmation with **Confirm only, no Cancel** (deliberate — the reservation/TTL model makes Cancel unnecessary, see GDD rationale); projected derived stats shown in a preview column before commit.
- Subscribe to `OnStatChanged(Experience, Level)` and `OnLevelUp` per Story 001/003's contracts — HUD must key level-up animation start to `OnLevelUp`, never to `OnStatChanged(Level)`, and must tolerate transient intermediate stat values during a level-up sequence (EC-LS-37 — the sequence doesn't use a stat transaction) without treating them as bugs.

---

## Out of Scope

*Handled by neighbouring stories:*

- All underlying system logic this UI displays — Stories 001–012
- Zone-wide social announcement on level-up — explicitly deferred post-MVP (OQ-LS-6), do not implement

---

## QA Test Cases

*Manual verification — Visual/Feel story, no automated test file:*

- **AC-LS-46**: Setup — trigger a normal level-up and a tier-transition level-up (L19→L20) side by side. Verify — overlay intensity is visually identical between the two; only the level-number hold duration differs (1.5s vs 2.5s); tier audio has the same instrument character, just a longer sustain/reverb tail, never louder or with a stinger. Pass condition — a reviewer cannot distinguish tier-transition intensity from normal by eye/ear, only by hold duration.
- **AC-LS-47**: Setup — bring a test character to L60. Verify — XP bar fill is static at 1.0, "MAX" text replaces the level number, no XP tooltip text shown. Pass condition — screenshot matches the described static-full state with no residual formula artifacts (e.g. no near-empty bar flash).
- **AC-LS-48**: Setup — open the respec screen for a L10 character with unallocated redistributable points. Verify — floor values render in a visually muted color distinct from redistributable values; commit button is disabled. Pass condition — allocating all redistributable points enables commit; the floor values never become editable.

---

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**: `production/qa/evidence/leveling-system-hud-ui-evidence.md` — screenshots for AC-LS-46/47/48 plus lead sign-off

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`OnStatChanged`), Story 003 (`OnLevelUp`), Story 005 (`AllocateFreePoint`), Story 007 (respec screen entry point, currently mock-backed), Story 008 (L60 "MAX" state)
- Unlocks: None — last story in the epic
