# Story 024: OWL Threshold Suspension & Hysteresis Signal

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-core.md`
**Requirement**: `TR-net-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 3 — the RTT/OWL estimate this threshold consumes is the application-level `RttProbe` measurement.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (pure C# state machine, no engine API)
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: at `OWL > MAX_COMPENSATABLE_OWL_MS/1000`, no compensation applied — `adjustedCycleTimer = _cycleTimer` raw — source: CR-NET-8.3
- Required: ±`OWL_HYSTERESIS_BAND_MS` hysteresis band around the threshold to prevent signal oscillation — source: CR-NET-8.3

---

## Acceptance Criteria

*From `design/gdd/networking-core.md`, scoped to this story — root GDD's own "AC-NC-31," distinct from wire-protocol.md's identically-numbered AC-NC-31 owned by Story 004 (documented ID collision, flagged at epic creation):*

- [ ] **AC-NC-31-HYSTERESIS** [BLOCKING] (Logic): Given a test client whose OWL estimate rises from 80ms to 150ms (`MAX_COMPENSATABLE_OWL_MS=120ms`, `OWL_HYSTERESIS_BAND_MS=15ms`), when OWL crosses 135ms (entry threshold), then the server emits `ConnectionQualityUpdate{rhythmCompensationActive=false}`. When OWL subsequently drops below 105ms (exit threshold), then the server emits `ConnectionQualityUpdate{rhythmCompensationActive=true}`. No `ConnectionQualityUpdate` is emitted for OWL changes remaining within the band (e.g. 110ms→125ms→130ms).

---

## Implementation Notes

*Derived from CR-NET-8.3:*

- Above threshold (`OWL > MAX_COMPENSATABLE_OWL_MS/1000` seconds, default 120ms), no OWL compensation is applied at all — Story 023's formula is not called; `adjustedCycleTimer = _cycleTimer` raw, unadjusted.
- **Hysteresis band** (`OWL_HYSTERESIS_BAND_MS`, default 15ms): a session enters uncompensated mode (compensation OFF) when OWL rises above `MAX_COMPENSATABLE_OWL_MS + OWL_HYSTERESIS_BAND_MS` (135ms default); exits uncompensated mode (compensation ON) only when OWL falls below `MAX_COMPENSATABLE_OWL_MS - OWL_HYSTERESIS_BAND_MS` (105ms default). While OWL sits between 105-135ms, the mode does not change — this is what prevents rapid flapping of the signal near the boundary.
- `ConnectionQualityUpdate{bool rhythmCompensationActive}` is emitted ONLY when the mode actually changes (ON↔OFF transition) — never on every OWL sample, and never broadcast to the zone (sent only to the affected client; other players have no visibility into another player's OWL status).
- Degraded-mode fallback (documented for HUD/UX context, not a testable server behavior in this story): when uncompensated, the player still participates fully in combat via auto-attack — only Rhythm Mastery skill triggers become unreliable. The HUD's presentation of this signal ("skill timing may be less reliable," not "you are broken") is a UI concern outside this story's scope.
- Implement as a small state machine per client-session: `{ compensationActive: bool }`, updated on each new OWL sample via the hysteresis comparison above, emitting the wire event only on an actual flip.

---

## Out of Scope

*Handled by neighbouring stories:*

- The wrap-correction formula this threshold gates — Story 023
- HUD/UX presentation of the connection-quality signal — future HUD/UI work (already covered by existing HUD design docs per project history)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/OwlCompensation_ThresholdHysteresis_tests.cs`

- **AC-NC-31-HYSTERESIS**: Given the OWL trajectory 80→150→(drop)→below 105, then exactly two `ConnectionQualityUpdate` emissions occur (false then true) at the correct crossing points; given an in-band trajectory (110→125→130), then zero emissions occur.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/OwlCompensation_ThresholdHysteresis_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 023 (the formula this threshold gates)
- Unlocks: None — completes the OWL Compensation cluster

---

## Completion Notes
**Completed**: 2026-07-20
**Criteria**: 1/1 passing (AC-NC-31-HYSTERESIS)
**Deviations**:
- ADVISORY: `TR-net-007` — systemic registry gap tracked since Story 001, shared as one umbrella requirement with Stories 022/023.
- ADVISORY: Story 002's `NetworkingTestHarness_Observer_tests.cs` exhaustive-reset test wasn't extended to cover the new `OnConnectionQualityUpdateEmitted` callback — logged as **TD-025**.
**Test Evidence**: Logic — `tests/EditMode/Networking/OwlCompensation_ThresholdHysteresis_tests.cs`, 12 tests, the single blocking AC covered.
**Code Review**: Complete — unity-specialist (CLEAN) + qa-tester (TESTABLE, 4 named gaps). 2 Required Changes (entry-boundary mutation-testing hole, dedicated first-sample-crossing test) + 2 Suggestions (idempotency test, doc-comment parity with Story 023) all applied and independently verified. Final verdict: APPROVED. **This closes the OWL Compensation sub-cluster (022-024) — all 3 stories now Complete.**
