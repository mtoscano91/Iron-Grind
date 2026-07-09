# Story 023: OWL Wrap-Correction Compensation Formula

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-owl-compensation.md`
**Requirement**: `TR-net-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 3 — the application-level `RttProbe`/echo is the authoritative OWL source (not NGO transport RTT, which seeds only). This formula consumes that authoritative OWL estimate.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (pure C# math, no engine API)
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: `MAX_WRAP_WINDOW_TICKS ≤ floor(MAX_COMPENSATABLE_OWL_MS / (1000/TICK_RATE_HZ))` — hard derived upper bound, currently `floor(120/50)=2` — source: CR-OWL-3

---

## Acceptance Criteria

*From `design/gdd/networking-owl-compensation.md` and `networking-core.md`, scoped to this story:*

- [ ] **AC-OWL-01** [BLOCKING] (Logic): Given slot 5, `LastBeatServerTick[5]=400`, `ServerTickNumber=401`, `_cycleTimer=0.02s`, `OWL_seconds=0.05s`, `CycleDuration=1.0s`, `BaseGraceThreshold=0.92`, `MAX_WRAP_WINDOW_TICKS=2`, when compensation evaluates, then `wrapCorrectionActive=true` (401-400=1≤2), `adjustedCycleTimer=1.0+(0.02-0.05)=0.97`, `graceTriggers=(0.97>0.92)=true`.
- [ ] **AC-OWL-02** [BLOCKING] (Logic): Given the same values but `LastBeatServerTick[5]=380` (21 ticks ago), then `wrapCorrectionActive=false` (21>2), `adjustedCycleTimer=max(0.02-0.05,0)=0`, `graceTriggers=false`.
- [ ] **AC-OWL-05** [BLOCKING] (Integration): Given a test client with server-estimated OWL=50ms (via `IZoneTestConfigurator.SetClientOWL`), `LastBeatServerTick` set to the tick immediately preceding current (via `SetLastBeatServerTick`), `_cycleTimer=0.02s`, when the server evaluates a `NotifySkillUsed` RPC, then wrap correction activates, `adjustedCycleTimer=0.97`, grace triggers — verified via `OnSkillGraceWindowEvaluated(entityId, adjustedTimer, graceTriggers)`.
- [ ] **AC-NC-29** [BLOCKING] (Logic, `networking-core.md`'s own restatement of this same formula — implemented once here, satisfies both docs' ACs): Same worked example as AC-OWL-01/02 — wrap case triggers skill; 10-ticks-ago case triggers auto-attack instead.

---

## Implementation Notes

*Derived from CR-OWL-2, F-OWL-1:*

```
wrapCorrectionActive = (signedAdjusted < 0)
                    AND (LastBeatServerTick[slot] != uint.MaxValue)
                    AND ((ServerTickNumber - LastBeatServerTick[slot]) <= MAX_WRAP_WINDOW_TICKS)
// where signedAdjusted = _cycleTimer - OWL_seconds

adjustedCycleTimer = wrapCorrectionActive
    ? CycleDuration + signedAdjusted
    : max(signedAdjusted, 0)

graceTriggers = adjustedCycleTimer > (BaseGraceThreshold × CycleDuration)
```
- Only active when `OWL_seconds <= MAX_COMPENSATABLE_OWL_MS/1000` (CR-NET-8.3 threshold, Story 024's concern — this story's formula still runs, but Story 024 gates whether it's called at all above threshold).
- Unsigned arithmetic for the tick-delta subtraction must not underflow — reuse Story 022's slot-array sentinel guard, evaluated via short-circuit before the tick-delta comparison.
- CR-OWL-5 adversarial-case reasoning (documented for context, not separately testable beyond AC-OWL-01/02/05): the 2-tick wrap window is not exploitable because OWL is server-computed from RTT probes (not client-reported), and by tick T+2 `_cycleTimer≈0.1s` — for any legitimate `OWL_seconds≤0.1s`, `signedAdjusted≥0` by then, making the window inert for honest play.
- This formula is called from the `NotifySkillUsed` RPC handler (a future Auto-Attack Combat epic concern) — this story implements and tests the formula itself against injected values, not the RPC handler wiring (that's out of scope, belongs to Auto-Attack Combat).

---

## Out of Scope

*Handled by neighbouring stories:*

- `LastBeatServerTick` data structure and slot allocation — Story 022 (this story only reads it)
- CR-NET-8.3 threshold suspension and hysteresis — Story 024
- The `NotifySkillUsed` RPC handler itself and grace-window consumption in Auto-Attack Combat — future Auto-Attack Combat epic

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/OwlCompensation_WrapCorrectionFormula_tests.cs`

- **AC-OWL-01**: Given the wrap-case worked example, then all three outputs match exactly.
- **AC-OWL-02**: Given the no-recent-Beat worked example, then all three outputs match exactly.
- **AC-OWL-05**: Given the integration-level injected OWL/LastBeatServerTick, then `OnSkillGraceWindowEvaluated` fires with the correct values.
- **AC-NC-29**: Given the root GDD's identical worked example (wrap vs. 10-ticks-ago contrast), then both sub-cases resolve correctly.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/OwlCompensation_WrapCorrectionFormula_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 022 (`LastBeatServerTick`), Story 001 (`IZoneTestConfigurator.SetClientOWL`/`SetLastBeatServerTick`), Story 002 (`OnSkillGraceWindowEvaluated` observer hook)
- Unlocks: Story 024 (threshold suspension wraps around this formula); future Auto-Attack Combat epic's `NotifySkillUsed` handler
