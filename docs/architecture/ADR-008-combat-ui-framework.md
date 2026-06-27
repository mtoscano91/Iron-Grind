# ADR-008: Combat UI Framework — Painter2D Arc Renderer + MonoBehaviour Presenter

## Status
Accepted (2026-06-27)

## Date
2026-06-27

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS |
| **Domain** | UI (UI Toolkit — Painter2D, VisualElement, UIDocument) |
| **Knowledge Risk** | HIGH — Unity 6.x UI Toolkit is post-LLM-cutoff |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`; `docs/engine-reference/unity/breaking-changes.md`; `docs/engine-reference/unity/deprecated-apis.md`; ADR-005 (UI Toolkit Accepted) |
| **Post-Cutoff APIs Used** | Painter2D (`MeshGenerationContext.painter2D`, `BeginPath`, `Arc`, `Stroke`) — no breaking changes found in 6.0–6.3 engine reference; USS `transition` property — stable, no 6.0–6.3 changes found |
| **Verification Required** | (1) Verify `Painter2D.Arc(Vector2, float, float, float, ArcDirection)` signature against Unity 6.3 upgrade guide before implementation; (2) USS `transition: height 200ms` fires `TransitionEndEvent` on iOS Metal in 6.3 — unverified on device matrix; (3) All USS files must pass strict import validation (invalid USS blocks import in 6.3); (4) 8 concurrent `MarkDirtyRepaint()` calls per frame must be validated ≤0.3ms on iPhone SE 3rd gen by `performance-analyst` before implementation sprint begins (CR-CUI-19 gate) |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-005 (HUD UI Framework — UI Toolkit, Accepted ✓): all forbidden patterns from ADR-005 apply (no `VisualElement.transform` setter, `PickingMode.Ignore` on leaf elements). ADR-004 (NGO — Accepted ✓): `SkillCooldownUpdate` and `SkillCooldownSnapshot` messages originate from the NGO server layer. |
| **Enables** | Combat UI implementation epic: skill bar, cooldown arc, expand/collapse, long-press binding stories |
| **Blocks** | All combat UI implementation stories — skill bar cannot begin without renderer and presenter decisions |
| **Ordering Note** | This ADR adds combat-UI-specific decisions on top of ADR-005's general UI Toolkit stance; ADR-005 must remain Accepted. |

## Context

### Problem Statement
`combat-ui.md` is Approved with the cooldown arc renderer left implementation-defined (CR-CUI-19): "Acceptable approaches include a shader-based quad with a `_Progress` float uniform, or Painter2D `generateVisualContent`." Three implementation decisions must be formalized before skill bar stories can be written: (1) which radial arc rendering technique to use within UI Toolkit, (2) how NGO `SkillCooldownUpdate` messages drive per-frame VisualElement state updates, (3) which Unity 6.0 UI event names apply to skill bar interaction callbacks. Without this ADR, implementation teams would make inconsistent choices across the skill bar stories.

### Constraints
- Must use UI Toolkit (ADR-005 Accepted) — UGUI Canvas elements are forbidden for screen-space HUD elements
- Must satisfy ≤100 draw calls per frame mobile budget (`technical-preferences.md`)
- Must sustain 60fps on iPhone SE 3rd gen (A15 Bionic, iOS Metal TBDR) — AC-CUI-8
- `VisualElement.transform` setter is forbidden (ADR-005 forbidden pattern) — use `style.translate`
- `PickingMode.Ignore` must be set explicitly on every non-interactive leaf element (ADR-005 forbidden pattern)
- USS event handling must use Unity 6.0 names: `HandleEventTrickleDown`, `HandleEventBubbleUp`, `StopPropagation()` — NOT deprecated `ExecuteDefaultAction`, `ExecuteDefaultActionAtTarget`, `PreventDefault()`
- USS strict validation: invalid USS syntax blocks file import in Unity 6.3 — all USS must be valid before commit
- Expand/collapse animation is GDD-locked: 200ms USS `transition` on `height` (CR-CUI-8) — not a new ADR decision

### Requirements
- Render up to 8 concurrent radial cooldown arcs at 60fps; per-frame interpolation on `Time.time` (CR-CUI-19)
- Arc `fraction` computed from F-CUI-1: `fraction = (cooldownExpiryTick − clientCurrentTick) / totalCooldownTicks`, clamped to [0, 1]
- NGO `SkillCooldownUpdate` and `SkillCooldownSnapshot` messages must drive VisualElement updates without coupling the network layer directly to the UI layer
- Interaction callbacks (long-press binding, tap, drag disambiguation) must use the Unity 6.0 event API

## Decision

### Decision 1 — Cooldown Arc Renderer: Painter2D `generateVisualContent`

Each skill slot's cooldown overlay `VisualElement` implements `generateVisualContent` to draw a radial arc using `MeshGenerationContext.painter2D`. The arc draws clockwise from 12 o'clock at `fraction` from F-CUI-1. When `fraction ≤ 0`, the overlay is hidden (`display: none`); the draw callback does not fire.

```csharp
// Registered in slot initialization:
_arcOverlay.generateVisualContent += DrawCooldownArc;

private void DrawCooldownArc(MeshGenerationContext ctx)
{
    if (_fraction <= 0f) return;
    var painter = ctx.painter2D;
    var center = new Vector2(_arcOverlay.contentRect.width * 0.5f,
                             _arcOverlay.contentRect.height * 0.5f);
    float radius = Mathf.Min(_arcOverlay.contentRect.width,
                             _arcOverlay.contentRect.height) * 0.5f
                   - SkillBarConstants.ARC_STROKE_WIDTH * 0.5f;

    painter.strokeColor = _arcColor;
    painter.lineWidth = SkillBarConstants.ARC_STROKE_WIDTH;
    painter.lineCap = LineCap.Round;

    painter.BeginPath();
    // UI Toolkit Y-axis points downward: -90° = 12 o'clock (top of element).
    // Arc sweeps clockwise through fraction * 360°.
    painter.Arc(center, radius,
                startAngle: -90f,
                endAngle:   -90f + _fraction * 360f,
                ArcDirection.Clockwise);
    painter.Stroke();
}
```

`MarkDirtyRepaint()` is called on the overlay each frame in `SkillBarPresenter.Update()` while the slot is in `OnCooldown` state. Once `fraction` reaches 0, the presenter sets `display: none` and stops calling `MarkDirtyRepaint`.

### Decision 2 — Data-Flow: `SkillBarPresenter : MonoBehaviour`

A `SkillBarPresenter` MonoBehaviour owns the `UIDocument` reference and a `SkillBarState` value-type cache. NGO message handlers enqueue updates to a `Queue<SkillCooldownUpdate>` (main-thread-safe: NGO callbacks are dispatched on the Unity main thread via `NetworkManager.Update()`). The presenter drains the queue in `Update()` and flushes deltas to VisualElements.

The queue is a decoupling mechanism — it separates the timing of NGO callback dispatch from the timing of VisualElement updates, keeping the network and UI layers independently testable.

```csharp
public sealed class SkillBarPresenter : MonoBehaviour
{
    [SerializeField] private UIDocument _document;  // field only — 6.3 SerializeField rule

    private SkillBarState _state;
    private SkillBarState _renderedState;
    private readonly Queue<SkillCooldownUpdate> _pendingCooldowns = new();

    // Called by NGO message handler (always main thread — NetworkManager.Update)
    public void OnCooldownUpdate(SkillCooldownUpdate msg) =>
        _pendingCooldowns.Enqueue(msg);

    private void Update()
    {
        // Drain pending NGO cooldown messages
        while (_pendingCooldowns.TryDequeue(out var update))
            _state.ApplyCooldownUpdate(update);

        // Flush state delta to VisualElements
        for (int i = 0; i < 8; i++)
        {
            if (_state.Slots[i].Equals(_renderedState.Slots[i])) continue;
            FlushSlot(i, _state.Slots[i]);
        }
        _renderedState = _state;
    }
}
```

### Decision 3 — USS Event API: Unity 6.0 Names (Conformance Rule)

All UI Toolkit event callbacks in the skill bar must use the Unity 6.0 event names. The pre-6.0 names are deprecated and generate compile warnings.

| Purpose | Use (6.0+) | Do NOT use (pre-6.0) |
|---|---|---|
| Trickle-down event handling | `HandleEventTrickleDown(EventBase)` | `ExecuteDefaultAction(EventBase)` |
| Bubble-up event handling | `HandleEventBubbleUp(EventBase)` | `ExecuteDefaultActionAtTarget(EventBase)` |
| Stop event propagation | `StopPropagation()` | `PreventDefault()` |

Long-press detection (CR-CUI-11) and tap disambiguation (CR-CUI-16) use `PointerDownEvent`, `PointerMoveEvent`, `PointerCancelEvent`, and `PointerUpEvent` — confirmed unchanged in 6.0–6.3.

### Architecture Diagram

```
NGO NetworkBehaviour (main thread — NetworkManager.Update)
  SkillCooldownUpdate → OnCooldownUpdate(msg)
  SkillCooldownSnapshot → OnCooldownSnapshot(snapshot)
                                   │
                                   ▼
                       Queue<SkillCooldownUpdate> (main-thread; decoupling)
                                   │
                                   ▼
                       SkillBarPresenter : MonoBehaviour
                          Update() per frame
                          ┌─────────────────────────────┐
                          │  SkillBarState (struct)      │
                          │  Slots[8]: SlotState         │
                          │  BarIsExpanded: bool         │
                          └──────────────┬──────────────┘
                                        │ flush delta
                       ┌────────────────┴────────────────┐
                       ▼                                 ▼
             SlotVisualElement[0..7]              ExpandRowContainer
               ├─ iconElement                      USS transition: height 200ms
               ├─ stateOverlay (opacity/style)     TransitionEndEvent guard
               └─ arcOverlay (PickingMode.Ignore)
                    generateVisualContent
                    Painter2D.Arc() clockwise
                    MarkDirtyRepaint() per-frame while OnCooldown
```

### Key Interfaces

**`SkillBarState` (value type — no heap allocation on copy)**
```csharp
public struct SkillBarState
{
    public SlotState[] Slots;   // length 8; allocated once at init
    public bool BarIsExpanded;

    public void ApplyCooldownUpdate(SkillCooldownUpdate msg) { ... }
    public void ApplyCooldownSnapshot(SkillCooldownSnapshot snapshot) { ... }
}

public struct SlotState : IEquatable<SlotState>
{
    public SkillID BoundSkillId;         // 0 = empty
    public bool IsUnlocked;
    public uint CooldownExpiryTick;      // 0 = not on cooldown
    public SkillSlotVisualState Visual;  // Empty | Locked | Ready | OnCooldown | Disabled
}
```

**`SkillBarConstants` (static class — no instantiation)**
```csharp
public static class SkillBarConstants
{
    public const float ZONE_E_WIDTH_DP   = 292f;  // exposed to HUD for chat-collapse (CR-CUI-21)
    public const float ARC_STROKE_WIDTH  = 4f;    // dp — tuning knob (OQ-CUI-2 pending)
    // ARC_COLOR is data-driven — resolved from ArtConfig.asset (OQ-CUI-2)
}
```

#### SkillBar UIDocument — PanelSettings required configuration

```
PanelSettings asset (SkillBar_PanelSettings):
  clearColor         = false    ← inherit from ADR-005 rule; must not erase 3D scene
  clearDepthStencil  = false
  sortingOrder       = 1        ← RESERVED for Combat UI; HUD_PanelSettings occupies sortingOrder 0 (ADR-005)
  targetDisplay      = 0
  referenceResolution = (same as HUD_PanelSettings — match project reference resolution)
  scaleMode          = ScaleWithScreenSize
```

`sortingOrder = 1` ensures the Combat UI `UIDocument` renders above the HUD `UIDocument` when panels overlap. ADR-005 Decision reserves `sortingOrder = 0` for the HUD; any additional `UIDocument` in the project must use a non-zero value. If a future design requires Combat UI panels to render *below* HUD panels in a specific sub-region, use a negative `sortingOrder` and document the intent.

## Alternatives Considered

### Alternative A: HLSL Shader via Background-Image Material
- **Description**: Each arc overlay uses a custom URP HLSL shader applied as a background-image material. A `_Progress` float uniform is set per-frame via material property blocks on the VisualElement.
- **Pros**: GPU-side draw; trivially smooth at 60fps; zero CPU tessellation per frame.
- **Cons**: Requires a custom shader asset and unity-shader-specialist involvement; IL2CPP shader stripping must explicitly include the arc shader; material-property-block integration with UI Toolkit elements in Unity 6.3 is post-cutoff (HIGH risk — not in engine reference); one material instance per slot state variant.
- **Rejection Reason**: Post-cutoff risk for material-property-block UI Toolkit integration in 6.3 is HIGH and not covered by the engine reference docs. Painter2D is UI Toolkit's first-class custom drawing API — purpose-built for this pattern. Engineering overhead of a custom shader is not justified at 8 simple arcs on A15 Bionic hardware.

### Alternative B: Direct Writes from NGO Message Handler
- **Description**: The `NetworkBehaviour` receiving `SkillCooldownUpdate` directly accesses a cached `VisualElement` reference and writes properties inline.
- **Pros**: No presenter indirection; simpler code path.
- **Cons**: Couples the network layer to the UI layer; tight coupling makes the presenter logic non-testable without a running NGO session; changes to the VisualElement structure require edits in the network layer.
- **Rejection Reason**: Architectural coupling without benefit. NGO callbacks are on the main thread (via `NetworkManager.Update()`), so thread-safety is not the concern — but the coupling prevents independent testing and violates separation of concerns between the network and UI layers.

## Consequences

### Positive
- Painter2D arc draws are fully contained within UI Toolkit's retained-mode rendering — no additional render pass, no RenderTexture allocation, no shader asset to maintain
- `SkillBarPresenter` is independently unit-testable: inject mock `SkillCooldownUpdate` values, assert VisualElement property outputs; no NGO session or server required
- USS `transition: height 200ms` for expand/collapse is already specified in the GDD (CR-CUI-8) — no new animation framework required
- Clear ownership boundary: NGO layer enqueues to `Queue<T>`; presenter layer drains and flushes; no cross-layer VisualElement writes

### Negative
- `MarkDirtyRepaint()` on up to 8 overlay elements per frame triggers up to 8 tessellation cycles while all 8 slots are on cooldown (worst case). Must be validated against the 0.3ms HUD update budget from ADR-005. This is a pre-sprint blocking gate (CR-CUI-19).
- Painter2D draws are CPU-side. For 8 simple stroked arcs, this is negligible on A15 Bionic; if future designs add fill patterns or complex arc geometry, the budget may need re-evaluation.
- Expand/collapse `TransitionEndEvent` may not fire if the iOS panel is backgrounded mid-transition (OS suspension). CR-CUI-8 already documents the guard: "Guard against `TransitionEndEvent` not firing... by storing collapse-pending state explicitly." Implementation must follow this guard.

### Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Painter2D API surface changed in Unity 6.3 | MEDIUM | Verify `BeginPath()`, `Arc()`, `Stroke()` signature against Unity 6.3 upgrade guide before first skill bar implementation story |
| 8× `MarkDirtyRepaint()` per frame exceeds 0.3ms budget | MEDIUM | `performance-analyst` profiling on iPhone SE 3rd gen (A15) before sprint begins — blocking gate per CR-CUI-19 |
| USS `transition: height` `TransitionEndEvent` unreliable on iOS | LOW | Unverified on iOS device matrix; CR-CUI-8 guard (store collapse-pending state) mitigates the worst case; verify in device smoke test before UI sprint |
| USS syntax error blocks import in Unity 6.3 | LOW | Run USS linting in CI; fix all invalid USS before first commit to skill bar USS files |
| Deprecated event API names compile-warned | LOW | CI lint rule: reject `ExecuteDefaultAction`, `PreventDefault`, `ExecuteDefaultActionAtTarget` in combat UI C# files |

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|---|---|---|
| `combat-ui.md` | CR-CUI-19: "Renderer mechanism is implementation-defined — acceptable approaches include a shader-based quad with a `_Progress` float uniform, or Painter2D `generateVisualContent`" | This ADR chooses Painter2D `generateVisualContent`; provides the `DrawCooldownArc` method signature and `SkillBarConstants` |
| `combat-ui.md` | CR-CUI-19: "Arc position must update at 60fps using `Time.time`-based interpolation, not per server tick" | `SkillBarPresenter.Update()` is called every frame; `MarkDirtyRepaint()` triggers `generateVisualContent` each frame while `OnCooldown` |
| `combat-ui.md` | CR-CUI-8: Expand/collapse uses 200ms USS `height` transition; guard `TransitionEndEvent` not firing | Confirmed as USS `transition: height` (GDD-locked); `TransitionEndEvent` guard is a required implementation detail in the presenter |
| `combat-ui.md` | CR-CUI-11: Long-press binding — "`PointerCancelEvent` (system interrupt) cancels the long-press" | Confirmed: `PointerCancelEvent` is the correct Unity 6.3 API, unchanged since 6.0 |
| `skill-system.md` | CR-SK-12: `SkillCooldownUpdate` delivers `cooldownExpiryTick` (absolute server tick) | `SlotState.CooldownExpiryTick` stores the absolute tick; presenter computes `fraction = (expiryTick − clientCurrentTick) / totalCooldownTicks` per F-CUI-1 |
| `hud.md` | Zone E layout: 292dp footprint; Combat UI exposes this as a layout constant for chat-collapse (CR-CUI-21) | `SkillBarConstants.ZONE_E_WIDTH_DP = 292f` — HUD reads this constant for chat corridor computation |

## Performance Implications

- **CPU**: 8 Painter2D arc draws per frame worst case (all 8 slots on cooldown). Each draw: `BeginPath()` + `Arc()` + `Stroke()` ≈ <0.02ms on A15 Bionic; 8 draws ≈ <0.16ms. Estimated well within 0.3ms HUD budget. **Must be confirmed by `performance-analyst` profiling before implementation sprint (blocking CR-CUI-19 gate).**
- **Memory**: No heap allocation per frame — `MeshGenerationContext` is stack-allocated by UI Toolkit internally. `SkillBarState` is a value type; `Queue<T>` allocates on Enqueue but NGO messages arrive at ≤20Hz (server tick rate), not per render frame.
- **GPU**: Painter2D geometry is batched with other UI Toolkit geometry in the existing UIDocument draw call. No additional draw call per arc. 8 arcs share the UIDocument batch.
- **Network**: No change — `SkillCooldownUpdate` and `SkillCooldownSnapshot` messages are already defined by ADR-004 and skill-system.md.

## Migration Plan

Greenfield — no existing combat UI implementation. Implementation order:

1. Implement `SkillBarState` struct and `SkillBarPresenter` MonoBehaviour shell; wire to NGO message handlers; unit-test state mutation with no NGO session required
2. Implement 8 slot `VisualElement` trees with Painter2D `generateVisualContent`; validate arc draw on iPhone SE 3rd gen — **performance-analyst must sign off before this story is marked Done (CR-CUI-19 gate)**
3. Implement expand/collapse USS transition; validate `TransitionEndEvent` on iOS; implement collapse-pending guard (CR-CUI-8)
4. Implement long-press binding using `PointerDownEvent` / `PointerMoveEvent` / `PointerCancelEvent` (Unity 6.0 event names per Decision 3)
5. Validate full bar at 8 slots on cooldown at 60fps; validate AC-CUI-8

## Validation Criteria

- `performance-analyst` confirms 8 concurrent `Painter2D.Arc()` + `MarkDirtyRepaint()` per frame sustains 60fps at ≤0.3ms on iPhone SE 3rd gen (A15 Bionic) — **blocking gate for combat UI sprint start per CR-CUI-19**
- USS `transition: height 200ms` produces smooth expand/collapse on iOS Metal; `TransitionEndEvent` fires or collapse-pending guard activates; verified via device smoke test
- `SkillBarPresenter` unit tests: given mock `SkillCooldownUpdate` sequence, assert exact VisualElement property state after each `Update()` cycle; zero NGO session required
- All USS files import without error in Unity 6.3 (strict validation)
- Zero deprecated event API names (`ExecuteDefaultAction`, `PreventDefault`, `ExecuteDefaultActionAtTarget`) in any combat UI C# file — enforced by CI lint rule

## Related Decisions

- ADR-005: HUD UI Framework — UI Toolkit (Accepted): established the UI Toolkit layer this ADR builds on; all ADR-005 forbidden patterns (`VisualElement.transform` setter, `PickingMode` inheritance) apply unchanged to combat UI elements
- ADR-004: NGO (Accepted): establishes `SkillCooldownUpdate` and `SkillCooldownSnapshot` message delivery model via NGO server
- `design/gdd/combat-ui.md`: CR-CUI-8 (expand/collapse animation), CR-CUI-11 (long-press binding), CR-CUI-16 (tap pre-checks), CR-CUI-18 (cast result handling), CR-CUI-19 (cooldown renderer — implementation-defined, resolved by this ADR), CR-CUI-21 (ZONE_E_WIDTH_DP chat-collapse constant)
- `design/gdd/skill-system.md`: CR-SK-12 (`SkillCooldownUpdate` with `cooldownExpiryTick` absolute server tick)
- `design/gdd/hud.md`: Zone E layout constant (292dp footprint)
