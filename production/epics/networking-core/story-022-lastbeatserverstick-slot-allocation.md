# Story 022: LastBeatServerTick Slot Allocation & Data Structure

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-owl-compensation.md`
**Requirement**: `TR-net-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: The RTT/OWL measurement API this data structure feeds is governed by ADR-004 Decision 3 (application-level `RttProbe` is authoritative; NGO transport RTT seeds only).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (pure C# data structure, no engine API)
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: `LastBeatServerTick` replaces `BeatResolvedThisTick bool[]` — a per-session `uint[]` indexed by entity slot, sentinel `uint.MaxValue` — source: CR-OWL-1
- Required: array size `MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE`, initialized to `uint.MaxValue` at zone creation — source: CR-OWL-4

---

## Acceptance Criteria

*From `design/gdd/networking-owl-compensation.md`, scoped to this story:*

- [ ] **AC-OWL-03** [BLOCKING] (Logic): Given an entity with `LastBeatServerTick[slot]=uint.MaxValue` (never fired a Beat), any `ServerTickNumber` including 0 and 1, when compensation is evaluated with `signedAdjusted<0`, then `wrapCorrectionActive=false` (sentinel guard short-circuits before the subtraction is evaluated) — this must hold explicitly at `ServerTickNumber=0` and `=1` to prove no unsigned-underflow false positive.
- [ ] **AC-OWL-04** [BLOCKING] (Logic): Given `LastBeatServerTick[slot]=T`, `MAX_WRAP_WINDOW_TICKS=2`, when `ServerTickNumber=T+2` (boundary), then `wrapCorrectionActive=true` (inclusive boundary); when `ServerTickNumber=T+3`, then `false`.
- [ ] **AC-OWL-06a** [BLOCKING] (Logic): Given a player entity's slot assigned at `PlayerJoinedZone`, when the player disconnects and reconnects within the ghost-session TTL, then the slot index is identical before and after, and `LastBeatServerTick[slot]` retains the value from the most recent Beat fired during the ghost period.
- [ ] **AC-OWL-06b** [BLOCKING] (Logic): Given a mob entity occupying slot S that despawns, when S returns to the free list and is reallocated to a new mob, then `LastBeatServerTick[S]=uint.MaxValue` at the moment of reallocation (sanitization invariant applied on free-list return).
- [ ] **AC-OWL-06c** [BLOCKING] (Logic): Given a player entity in `Disconnected_SessionActive` (ghost period) with Beats firing, when `LastBeatServerTick[slot]` is read during the ghost period, then the value reflects the tick of the most recent Beat fired (not reset or reused during TTL).

---

## Implementation Notes

*Derived from CR-OWL-1, CR-OWL-4, EC-OWL-1, EC-OWL-2, EC-OWL-3:*

- `LastBeatServerTick`: a per-zone `uint[MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE]`, initialized to `uint.MaxValue` at zone creation. Updated atomically at Beat evaluation time: `LastBeatServerTick[slot] = ServerTickNumber` (this hooks into Story 009's tick-driven Beat evaluation phase — add the update call there once this story's array exists).
- Never persisted — reinitialized to `uint.MaxValue` on zone load; ghost-session reconnect does NOT reset the slot (persists through `Disconnected_SessionActive → Reconnecting → Connected`); slot deallocation (TTL expiry/zone close) resets the freed slot to `uint.MaxValue`.
- **Slot allocation contract** (CR-OWL-4): players — slot allocated at `PlayerJoinedZone`, deallocated at `PlayerLeftZone` or session TTL expiry, preserved during the ghost period (not reassigned until TTL expiry or zone close). Mobs — slot allocated at spawn (Zone Instancing's future responsibility), deallocated at death/despawn, returned to a free list and reused; `MAX_MOBS_PER_ZONE` is a placeholder tuning knob pending the Zone Instancing GDD.
- **Sanitization invariant**: before a slot returns to the free list, it MUST be reset to `uint.MaxValue` — this is what prevents a recycled mob slot from producing a stale-tick false positive for the next mob assigned to it.
- **EC-OWL-1 (uint wrap)**: at `TICK_RATE_HZ=20`, `uint.MaxValue` ticks ≈ 6.8 years — out of scope for MVP; the sentinel creates a theoretical false-positive at the first ticks after a `ServerTickNumber` wrap, accepted as a non-practical MVP concern (flag for `ulong` promotion if long-running servers ever become a deployment target).
- **EC-OWL-3 (sentinel guard)**: the guard `(LastBeatServerTick[slot] != uint.MaxValue)` must be evaluated FIRST via short-circuit — without it, at server ticks 0-1 the raw uint arithmetic `0 - uint.MaxValue = 1 ≤ 2` would produce a false-positive `wrapCorrectionActive=true` for every sentinel slot.

---

## Out of Scope

*Handled by neighbouring stories:*

- The wrap-correction compensation formula itself (F-OWL-1) — Story 023
- The tick loop's Beat-evaluation phase this array's update hooks into — Story 009
- Real mob spawn/despawn slot allocation — future Zone Instancing epic (mock provider only, here)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/OwlCompensation_SlotAllocation_tests.cs`

- **AC-OWL-03**: Given sentinel value at `ServerTickNumber=0` and `=1`, then `wrapCorrectionActive=false` in both cases, subtraction never evaluated.
- **AC-OWL-04**: Given the exact T+2/T+3 boundary, then the inclusive/exclusive behavior holds precisely.
- **AC-OWL-06a**: Given a ghost-reconnect cycle, then the slot index is unchanged and the tick value is preserved.
- **AC-OWL-06b**: Given a mob slot free-list return then reallocation, then the slot is `uint.MaxValue` at reallocation time.
- **AC-OWL-06c**: Given a ghost period with Beats firing, then reads reflect the most recent Beat tick.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/OwlCompensation_SlotAllocation_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009 (tick loop's Beat evaluation phase), Story 001 (`IZoneTestConfigurator.SetLastBeatServerTick`)
- Unlocks: Story 023 (wrap-correction formula reads this array), Story 017 (ghost period Beat continuation)

---

## Completion Notes
**Completed**: 2026-07-20
**Criteria**: 5/5 passing (AC-OWL-03, AC-OWL-04, AC-OWL-06a, AC-OWL-06b, AC-OWL-06c) — none deferred
**Deviations**:
- ADVISORY: `TR-net-007` not present in `docs/architecture/tr-registry.yaml` — systemic registry gap tracked since Story 001 (same as "TR-net-006"), not new to this story.
- ADVISORY: AC-OWL-06a/06c's "ghost period" proof is structural (no `DeallocatePlayerSlot` call between allocation and simulated reconnect), not a real integration test against a zone session manager — logged as **TD-024**.
**Test Evidence**: Logic — `tests/EditMode/Networking/OwlCompensation_SlotAllocation_tests.cs`, 27 tests, all 5 blocking ACs covered.
**Code Review**: Complete — unity-specialist + qa-tester (parallel). unity-specialist found 1 Required Change (missing "never allocated" guard on `DeallocateMobSlot`, creating a latent double-allocation hazard); applied, plus 3 qa-tester-suggested regression tests. Final verdict: APPROVED.
