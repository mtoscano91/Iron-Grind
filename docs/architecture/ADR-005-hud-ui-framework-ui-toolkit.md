# ADR-005: HUD UI Framework — UI Toolkit for Screen-Space Layer

## Status
Accepted (2026-06-27)

## Date
2026-06-20

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.4) |
| **Domain** | UI |
| **Knowledge Risk** | HIGH — Unity 6.x is beyond LLM training data |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/modules/ui.md`, `docs/engine-reference/unity/breaking-changes.md`, `docs/engine-reference/unity/deprecated-apis.md`, `docs/engine-reference/unity/current-best-practices.md` |
| **Post-Cutoff APIs Used** | `UsageHints.DynamicTransform` on fill elements (scale-based fill animation); `RuntimePanelUtils.ScreenToPanel` for safe area coordinate conversion; `element.style.translate` for positional animation (replaces deprecated `VisualElement.transform` setter, Unity 6.2+); `pickingMode = PickingMode.Ignore` (UI Toolkit equivalent of UGUI `raycast target = false`) |
| **Verification Required** | (1) Profile HUD update cost on iPhone SE 3rd gen: target < 0.3ms per tick at 60fps under 20 stat events/second. (2) Validate `RuntimePanelUtils.ScreenToPanel` safe area conversion on physical notched iPhone. (3) Confirm `PanelSettings.clearColor = false` before each build. (4) Verify `InputSystemUIInputModule` is active on scene `EventSystem`. (5) All HUD USS files pass syntax validation in CI before first commit. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | ADR-008 (Combat UI Framework — Painter2D arc + MonoBehaviour presenter, Accepted 2026-06-27) |
| **Blocks** | All HUD implementation epics (resolves OQ-HUD-1); Combat UI (#28, Not Started) should align its framework choice with this ADR before design begins |
| **Ordering Note** | Auto-Attack Combat GDD charge bar spec must be amended before HUD implementation sprint begins — see Migration Plan |

## Context

### Problem Statement
The HUD GDD (`design/gdd/hud.md`) requires a UI framework choice for the persistent screen-space HUD layer before any HUD implementation epic can begin (OQ-HUD-1). The Auto-Attack Combat GDD established that world-space floating damage numbers require a UGUI world-space Canvas regardless of HUD framework choice — making a mixed UGUI + UI Toolkit project mandatory at the engine level. This ADR decides which framework owns the screen-space HUD layer specifically.

### Constraints
- Unity 6.3 LTS is the pinned engine version. `VisualElement.transform` setter is deprecated since Unity 6.2 — it still compiles with a warning in 6.3 (it is **not** removed; see `docs/engine-reference/unity/deprecated-apis.md`), but `element.style.translate` is required for all new positional animation code. USS invalid syntax now blocks file import in 6.3 (was a warning in 6.1/6.2). `AccessibilityRole` changed to `byte` underlying type in 6.3 — bitwise combination of role values is a compile error.
- World-space damage numbers (Auto-Attack Combat GDD) require UGUI at the world-space Canvas layer. Mixed-framework is therefore mandatory regardless of this decision.
- iOS primary target, touch-only. No hover interactions. Landscape orientation.
- Performance budget: HUD update cost must remain < 0.3ms per tick at 60fps on iPhone SE 3rd gen (CR-HUD Canvas Layer Architecture spec, `design/gdd/hud.md`).
- Memory ceiling: 1.5 GB total.
- The Auto-Attack Combat GDD's charge bar spec uses UGUI APIs (`Image.FillMethod.Horizontal`, `fillAmount`). If UI Toolkit is chosen for the HUD layer, this spec must be amended.
- `Screen.safeArea` returns physical screen pixels; UI Toolkit `style.margin*` takes panel logical units. Raw pixel values must not be written to style properties — coordinate conversion is required.

### Requirements
- Must support high-frequency stat updates (HP bar changes per combat tick at TICK_RATE_HZ=20) without causing full-panel redraws.
- Must support non-interactive display-only elements (equivalent of UGUI `raycast target = false`).
- Must support safe area insets via `Screen.safeArea` with correct coordinate space conversion.
- Must handle Solo/Party mode transitions that show/hide element subtrees (CR-HUD-8).
- Must not block touch input from reaching the 3D scene beneath the HUD.
- Must integrate with the world-space UGUI damage number Canvas without input routing conflicts.
- All positional animation must use `element.style.translate` — `VisualElement.transform` setter is deprecated (6.2).
- All USS files must pass syntax validation (blocks Unity 6.3 import on failure).

## Decision

**The HUD screen-space layer uses UI Toolkit (UIDocument / VisualElement / UXML / USS).**

The world-space damage number layer retains UGUI (a separate world-space Canvas), making this a two-layer mixed-framework architecture. The layers are strictly isolated: UI Toolkit handles all screen-space HUD elements; UGUI handles only world-space floating text in 3D camera space.

### Architecture

```
UIDocument (PanelSettings: sortingOrder 0, targetDisplay 0,
            renderTexture null — renders over all cameras)
└── HUD_Root (VisualElement)
    │
    ├── HUD_Static (VisualElement)
    │       Panel backgrounds, bar frame tracks, slot outlines, colorblind icons
    │       Dirty trigger: zone entry / party join–leave only
    │
    ├── HUD_Dynamic (VisualElement)
    │   ├── HP scale-fill   (UsageHints.DynamicTransform)
    │   ├── MP scale-fill   (UsageHints.DynamicTransform)
    │   ├── XP scale-fill   (UsageHints.DynamicTransform)
    │   ├── Charge bar scale-fill (UsageHints.DynamicTransform)
    │   ├── Party member HP scale-fills × 4 (UsageHints.DynamicTransform)
    │   ├── Target HP scale-fill (UsageHints.DynamicTransform)
    │   └── Gold text label
    │       Dirty trigger: per OnStatChanged / GoldSyncEvent / tick
    │
    └── HUD_Overlay (VisualElement)
            Loot notification stack, chat panel
            Dirty trigger: item assignment / despawn / chat message

WorldSpaceDamageCanvas (Canvas, RenderMode.WorldSpace, UGUI)
└── DamageNumberPool (TextMeshPro, world-space floating text, non-interactive)
    No GraphicRaycaster component — damage numbers are display-only
```

### Key Interfaces

#### Fill bar update pattern — scale-based (all HP/MP/XP/charge bars)

Fill bars use `style.scale` on the X axis rather than `style.width` percentage. This allows the renderer to apply the fill on the GPU without re-tessellating the mesh (no layout recalculation), satisfying the < 0.3ms per-tick budget. `transformOrigin: left center` in USS anchors the scale to the left edge of the track.

```csharp
// UGUI (rejected)
healthBarImage.fillAmount = fillHP;

// UI Toolkit — scale-based fill (chosen)
// USS on fill element: transform-origin: left center; position: absolute;
//                      width: 100%; height: 100%;
_hpFillElement.style.scale = new StyleScale(new Scale(new Vector3(fillHP, 1f, 1f)));

// usageHints set once at init — allows GPU-side transform update without tessellation
_hpFillElement.usageHints = UsageHints.DynamicTransform;
```

Caveat: `style.scale` with `border-radius` on fill elements produces visually incorrect corners at low fill values. HUD GDD fill elements are rectangular (no border-radius on fills — the rounded/chamfered appearance is on the outer track frame, not the fill layer). If a future design adds border-radius to fill layers, switch to the width-percent pattern with absolute positioning instead.

#### Non-interactive element pattern

`PickingMode.Ignore` does NOT propagate to children automatically. Every non-interactive HUD leaf element must set it explicitly. Use a base USS class applied to all display-only elements:

```css
/* hud-base.uss */
.hud-display-only {
    -unity-picking-mode: ignore;
}
```

Apply `.hud-display-only` to all fill elements, background panels, icon images, and text labels that are not tap targets. Interactive elements (buff tray scroll arrows, auto-run toggle, target frame dismiss region) must NOT carry this class — they use the default `PickingMode.Position`.

```csharp
// UGUI (rejected)
image.raycastTarget = false;

// UI Toolkit (chosen) — per element, does not propagate
element.pickingMode = PickingMode.Ignore;
// Or via USS class on UXML element
```

#### Safe area inset — coordinate conversion required

`Screen.safeArea` returns physical screen pixels. UI Toolkit `style.margin*` takes panel logical units. Direct assignment produces incorrect insets (off by the screen-to-panel scale factor). `RuntimePanelUtils.ScreenToPanel` converts between spaces.

Note: `Screen.safeArea` uses bottom-left origin; UI Toolkit panel space uses top-left. The Y coordinate must be flipped.

```csharp
void ApplySafeArea(VisualElement hudRoot)
{
    IPanel panel = hudRoot.panel;
    Rect sa = Screen.safeArea;

    // Convert safe area corners to panel space (Y-flipped from Screen coords)
    Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel,
        new Vector2(sa.xMin, Screen.height - sa.yMax));
    Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel,
        new Vector2(sa.xMax, Screen.height - sa.yMin));

    // Panel-space dimensions of the full screen (used for right/bottom margin calculation)
    Vector2 panelSize = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height));

    hudRoot.style.marginLeft   = topLeft.x                     + 8f; // + 8dp interior margin (CR-HUD-17)
    hudRoot.style.marginTop    = topLeft.y                     + 8f;
    hudRoot.style.marginRight  = (panelSize.x - bottomRight.x) + 8f;
    hudRoot.style.marginBottom = (panelSize.y - bottomRight.y) + 8f;
}
```

Call on `Start()` and again on `OnRectTransformDimensionsChange` equivalent (subscribe to `RuntimePanelUtils` panel geometry change or re-apply in `Update` when `Screen.safeArea` changes). Since this is a landscape-only project, safe area changes mid-session are not expected but the call is cheap.

#### Positional animation

```csharp
// WRONG — deprecated in Unity 6.2
element.transform.position = new Vector3(10, 10, 0);

// CORRECT — UI Toolkit 6.2+
element.style.translate = new StyleTranslate(new Translate(10, 10));
```

#### PanelSettings required configuration

```
PanelSettings asset (HUD_PanelSettings):
  clearColor         = false    ← REQUIRED — true erases the 3D scene beneath
  clearDepthStencil  = false
  sortingOrder       = 0
  targetDisplay      = 0
  referenceResolution = (project reference resolution)
  scaleMode          = ScaleWithScreenSize (or ConstantPhysicalSize for dp units)
```

#### World-space Canvas isolation

The `EventSystem` GameObject in the scene must carry `InputSystemUIInputModule` (from `com.unity.inputsystem`). The legacy `StandaloneInputModule` routes touch events differently and conflicts with UI Toolkit's `PanelEventHandler`. The `WorldSpaceDamageCanvas` must have no `GraphicRaycaster` component — damage numbers are non-interactive and an active raycaster creates an unnecessary hit-test layer.

```csharp
// WorldSpaceDamageCanvas configuration check (assert in editor script or Awake)
Debug.Assert(
    worldSpaceDamageCanvas.GetComponent<GraphicRaycaster>() == null,
    "WorldSpaceDamageCanvas must not have a GraphicRaycaster — damage numbers are non-interactive"
);
```

## Alternatives Considered

### Alternative A: UGUI Canvas for HUD Layer

- **Description**: HUD screen-space layer uses UGUI Canvas with 3-layer nested Canvas dirty-isolation (HUD_Static / HUD_Dynamic / HUD_Overlay). Charge bar: `Image.FillMethod.Horizontal` / `fillAmount`. No framework bridge at the screen-space layer.
- **Pros**: No Auto-Attack Combat GDD amendment required; 3-layer Canvas architecture already documented in HUD GDD UI Requirements; more widely documented at the LLM training cutoff; single renderer for all screen-space UI; charge bar implementation straightforward with `fillAmount`.
- **Cons**: Unity 6 explicitly recommends UI Toolkit for new projects; Canvas rebuild granularity is coarser — dirty-isolation requires nested Canvas workaround rather than per-element UsageHints; harder to reskin without code changes; nested Canvas hierarchy adds scene complexity.
- **Rejection Reason**: Unity 6 recommends UI Toolkit for new projects. Per-element dirty isolation via `UsageHints.DynamicTransform` is semantically cleaner than nested Canvas. The additional upfront cost (charge bar re-spec) is a one-time amendment contained in a single GDD and ADR.

## Consequences

### Positive
- UI Toolkit is Unity 6's recommended UI system — best long-term support trajectory.
- Scale-based fill pattern (`style.scale` + `DynamicTransform`): GPU applies fill animation without mesh re-tessellation. No Canvas rebuild per HP tick. Meets < 0.3ms HUD update budget.
- UXML / USS files are text-based, diffable, and version-controllable.
- `UsageHints.DynamicTransform` provides element-level dirty isolation without nested Canvas hierarchy workaround.
- CSS-like USS enables reskinning and theming without code changes.

### Negative
- Auto-Attack Combat GDD (`design/gdd/auto-attack-combat.md`) charge bar spec must be amended: `Image.FillMethod.Horizontal` / `fillAmount` → `style.scale` on X axis with `UsageHints.DynamicTransform`.
- UGUI / UI Toolkit event routing must be explicitly isolated: `PanelEventHandler` + `PanelRaycaster` for screen-space (UI Toolkit) vs. `InputSystemUIInputModule` for world-space (UGUI). If legacy `StandaloneInputModule` is active, touches may be routed incorrectly.
- USS validation becomes a CI gate in Unity 6.3: any USS syntax error blocks file import. A pre-commit USS linter must be added to the toolchain before any HUD `.uss` files are authored.
- `PickingMode.Ignore` does not propagate down the element tree. Every non-interactive HUD leaf element must set it explicitly via a base USS class. Failure produces silent touch-blocking over decorative elements.
- `PanelSettings.clearColor` must remain `false`. A toggled-on `clearColor` erases the 3D game scene beneath the HUD. Must be verified before each Unity build.
- HUD GDD `raycast target = false` terminology (UGUI) maps to `PickingMode.Ignore` (UI Toolkit). No GDD amendment required — the behavioral requirement is clear — but implementation team must know the mapping.

### Risks

1. **Mobile performance unvalidated for UI Toolkit**: UI Toolkit's render performance on iPhone SE 3rd gen at 60fps under combat load (20 stat events/second, scale-based fills) is beyond LLM training data. **Mitigation**: profile on physical device before HUD implementation sprint. If HUD update cost exceeds 0.3ms at 60fps, escalate to Architecture Review and evaluate UGUI fallback.

2. **USS import blocker risk**: Any invalid USS file committed to version control blocks all developers' Unity import on affected machines. **Mitigation**: add USS syntax checker to CI (or pre-commit hook) before the first HUD `.uss` file is authored. Do not rely on developer discipline alone.

3. **Mixed-framework input conflict**: If `InputSystemUIInputModule` is absent or the wrong module is active, touches may be double-routed between UGUI EventSystem and UI Toolkit PanelEventHandler. **Mitigation**: assert `InputSystemUIInputModule` presence in an editor startup check; verify in integration test that a tap on a HUD element does not produce a simultaneous event on the world-space Canvas.

4. **Texture atlas hitch on first HUD frame**: UI Toolkit populates its internal dynamic texture atlas on first render. With all HUD elements appearing simultaneously at zone entry, the atlas population spike may hit 1–3ms on the first frame. **Mitigation**: pre-warm the atlas at startup by rendering the HUD off-screen for one frame before zone entry (or use TextCore glyph pre-generation on the font asset at build time).

5. **Scale-based fill and border-radius incompatibility**: `style.scale` applied to an element with `border-radius` produces visually incorrect scaled corners at low fill values. HUD fill elements are currently rectangular (no `border-radius` per GDD spec). **Mitigation**: enforce no `border-radius` on fill layer elements in USS review. If a future design adds rounded fill elements, switch those bars to the width-percent pattern with absolute positioning.

6. **`PanelSettings.clearColor` accidental toggle**: This flag is one Inspector click away from erasing the 3D scene under the HUD. **Mitigation**: add to the project's pre-build checklist; document in the HUD README.

7. **Knowledge gap — Unity 6.3 iOS Metal UI Toolkit runtime**: Specific behavior of UI Toolkit on iOS Metal in Unity 6.3 is beyond LLM cutoff. **Mitigation**: treat all UI Toolkit API usage as requiring profiling and runtime verification on target hardware before shipping.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `hud.md` | OQ-HUD-1: ADR required before any HUD implementation epic can begin | This ADR resolves OQ-HUD-1: selects UI Toolkit for the HUD screen-space layer |
| `hud.md` | Canvas Layer Architecture (UI Requirements): dirty-isolation target < 0.3ms per HP tick at 60fps | Scale-based fill with `UsageHints.DynamicTransform` achieves per-element GPU-side update without tessellation; HUD_Static subtree never dirtied by stat changes |
| `hud.md` | CR-HUD-18: display-only elements must have `raycast target = false` | UI Toolkit equivalent: `element.pickingMode = PickingMode.Ignore` applied via `.hud-display-only` USS class on all non-interactive leaf elements |
| `hud.md` | CR-HUD-17: safe area insets via `Screen.safeArea`, no hardcoded pixel offsets | `RuntimePanelUtils.ScreenToPanel` converts physical pixel safe area coords to panel logical units before applying to `HUD_Root` margins |
| `hud.md` | CR-HUD-13: charge bar is `raycast target = false` | `.hud-display-only` class on charge bar fill element → `PickingMode.Ignore` |
| `auto-attack-combat.md` | Charge bar fill animation: `Image.FillMethod.Horizontal` / `fillAmount` | Re-specced to `style.scale = new StyleScale(new Scale(new Vector3(fillPercent, 1f, 1f)))` with `UsageHints.DynamicTransform` — amendment required before HUD implementation sprint |

## Performance Implications

- **CPU**: Scale-based fill — GPU applies fill animation without tessellation; no layout recalculation per HP tick. `HUD_Static` subtree never touched by stat change events. HUD_Dynamic per-tick cost targets < 0.3ms on iPhone SE 3rd gen at 60fps. Must be verified on device.
- **Memory**: UIDocument + element tree for HUD (~40–60 VisualElements) adds ~1–2MB above UGUI equivalent. Within 1.5GB ceiling.
- **Load Time**: UXML parsing at scene load is a one-time cost. Atlas pre-warming adds ~1 frame at startup. Estimate: negligible against zone load time. Verify on target device.
- **Network**: N/A.

## Migration Plan

1. **Auto-Attack Combat GDD amendment** (required before HUD implementation sprint begins):
   - Update charge bar fill spec: `Image.FillMethod.Horizontal` / `fillAmount` → `style.scale` on X axis with `transform-origin: left center` in USS; `UsageHints.DynamicTransform` on fill element.
   - Update any acceptance criteria in that GDD that reference `fillAmount`.

2. **USS linter setup** (required before any HUD `.uss` file is committed):
   - Add USS syntax validation step to CI pipeline.
   - Optionally add as a pre-commit hook to catch errors locally.

3. **`InputSystemUIInputModule` verification**:
   - Confirm `EventSystem` in the main scene carries `InputSystemUIInputModule` — not the legacy `StandaloneInputModule`.
   - If legacy module is present, replace before HUD implementation begins.

4. **`WorldSpaceDamageCanvas` isolation**:
   - Remove `GraphicRaycaster` from the `WorldSpaceDamageCanvas` GameObject.
   - Damage numbers are non-interactive; the raycaster is unnecessary and risks input conflicts.

5. **HUD_PanelSettings asset**:
   - Create `HUD_PanelSettings` (PanelSettings asset) with `clearColor = false`, `sortingOrder = 0`.
   - Assign to the `UIDocument` component on the HUD GameObject.

## Validation Criteria

- **Dirty isolation**: Frame Debugger — fire a single `OnStatChanged` event (HP change). Confirm only `HUD_Dynamic` fill elements mark dirty; `HUD_Static` shows zero dirty calls. Confirm `HUD_Static` remains clean between zone entry and next party state change.
- **Performance**: Unity Profiler on iPhone SE 3rd gen — simulate 20 stat events/second (combat tick). HUD update cycle (event received → fill scale update) measured at < 0.3ms CPU time at 60fps sustained.
- **Safe area**: On physical iPhone X+ (or Xcode simulator with safe area enabled) in landscape — all HUD elements fall within `Screen.safeArea` + 8dp interior margin. No element overlaps notch or Dynamic Island region.
- **Touch isolation**: Tap in joystick zone (left half of screen) with `WorldSpaceDamageCanvas` active — no HUD `PointerDown` event fires. Tap on buff tray scroll arrow — no world-space Canvas event fires.
- **USS linter**: All HUD `.uss` files pass syntax validation before first commit to `main`.
- **`PanelSettings.clearColor`**: Pre-build check confirms `clearColor = false` on `HUD_PanelSettings` asset.
- **Atlas pre-warm**: On zone entry profiler trace — no >1ms spike attributable to `UIR.UIRenderDevice.BeginNewBatch` after the pre-warm frame.

## Related Decisions
- Resolves: `design/gdd/hud.md` OQ-HUD-1
- Requires amendment: `design/gdd/auto-attack-combat.md` (charge bar spec: `fillAmount` → `style.scale`)
- ADR-004: Networking Library (NGO) — unrelated to UI layer
- ADR-008: Combat UI Framework — Painter2D arc + MonoBehaviour presenter (Accepted 2026-06-27)
