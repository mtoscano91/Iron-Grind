# Networking OWL Compensation

> **Status**: Approved (lean re-review Pass 2, 2026-05-18)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-18
> **Resolves**: networking-core.md Cluster B (B1–B3) — OWL wrap correction broken at design-target latency

## Sub-Document Reference

This primitive spec extracts and replaces the inline formula in **`networking-core.md` CR-NET-8.2**. `networking-core.md` CR-NET-8 (OWL threshold, hysteresis band, RTT probe mechanism, degraded-mode fallback) remains unchanged; only the compensation calculation and `BeatResolvedThisTick` implementation are relocated here.

---

## Overview

OWL (One-Way Latency) Compensation is the server-side mechanism that preserves Pillar 2 (Rhythm Mastery) for players on typical mobile connections (100–150ms RTT). Without compensation, a player tapping on-beat on their display has their `NotifySkillUsed` arrive ~50ms after the beat on the server — breaking the grace window for a correctly-timed tap. The original `BeatResolvedThisTick bool[]` implementation was broken at design-target latency: the flag is cleared at the start of each tick, and at 50ms OWL the RPC statistically arrives in the tick *after* the Beat fires, finding a cleared flag. This spec defines the corrected algorithm using `LastBeatServerTick uint[]`, the entity slot allocation contract, the tick-window constraint proof, and the adversarial case analysis that bounds the correction window.

---

## Player Fantasy

*(Delegates to `networking-core.md` Player Fantasy)* "The auto-attack clock isn't on your device. When you slot a skill between beats and it lands clean, you read the shared rhythm correctly." This primitive ensures that statement is true for players at design-target mobile latency — not just for players on a LAN.

---

## Detailed Rules

**CR-OWL-1 — LastBeatServerTick data structure**

Replace `BeatResolvedThisTick bool[]` (referenced in `networking-core.md` CR-NET-2) with `LastBeatServerTick uint[]`, indexed by entity slot (see CR-OWL-4 for slot allocation). Default value: `uint.MaxValue` (sentinel — entity has never fired a Beat). Updated atomically at Beat evaluation time:

```
LastBeatServerTick[slot] = ServerTickNumber
```

`ServerTickNumber` is the server's monotonically increasing tick counter (uint, never reset during a server process lifetime). `LastBeatServerTick` is never persisted — initialized to all `uint.MaxValue` entries on zone load. Ghost-session reconnect does NOT reset the slot: `LastBeatServerTick[slot]` retains its current tick value through `Disconnected_SessionActive → Reconnecting → Connected` (see EC-OWL-2). After TTL expiry or zone close, the slot is deallocated; any new slot allocation starts at `uint.MaxValue` by array initialization.

The `BeatResolvedThisTick(entityId)` call in the original CR-NET-8.2 formula is replaced by:

```
wrapCorrectionWindow = (LastBeatServerTick[slot] != uint.MaxValue)
                    AND ((ServerTickNumber - LastBeatServerTick[slot]) <= MAX_WRAP_WINDOW_TICKS)
```

> **Unsigned arithmetic note:** The subtraction `ServerTickNumber - LastBeatServerTick[slot]` uses C# unsigned integer arithmetic intentionally. Do not cast to `int` before comparing — a signed cast would misinterpret large values and break the tick-delta logic. The sentinel guard (`!= uint.MaxValue`) must be evaluated first; short-circuit evaluation ensures the subtraction is never performed against the sentinel, eliminating the startup false-positive at server ticks 0–1 (where `0 - uint.MaxValue = 1 ≤ 2` in uint arithmetic) and for freshly recycled mob slots.

---

**CR-OWL-2 — Corrected OWL compensation formula (replaces CR-NET-8.2 inline formula)**

Applied when a `NotifySkillUsed` RPC is received and the Auto-Attack system evaluates the grace window threshold. OWL compensation is only active when `OWL_seconds <= MAX_COMPENSATABLE_OWL_MS / 1000` (per `networking-core.md` CR-NET-8.3).

```
// OWL_seconds: server's authoritative OWL estimate — never client-submitted.
// _cycleTimer: server-authoritative tracked value — never client-submitted.
// slot: entity slot index for entityId per CR-OWL-4.

signedAdjusted = _cycleTimer - OWL_seconds

wrapCorrectionWindow = (LastBeatServerTick[slot] != uint.MaxValue)
                    AND ((ServerTickNumber - LastBeatServerTick[slot]) <= MAX_WRAP_WINDOW_TICKS)

if (signedAdjusted < 0 AND wrapCorrectionWindow):
    adjustedCycleTimer = CycleDuration + signedAdjusted
    // Recovers estimated pre-wrap position in the prior cycle.
else:
    adjustedCycleTimer = max(signedAdjusted, 0)
    // Early tap (signedAdjusted > 0): no wrap needed.
    // Very early tap (signedAdjusted ≤ 0, no recent Beat): clamp to 0.

graceTriggers = adjustedCycleTimer > (BaseGraceThreshold × CycleDuration)
```

The wrap path (`signedAdjusted < 0 AND wrapCorrectionWindow`) recovers the estimated cycle position of the player's tap in the prior cycle, compensating for timer reset during packet transit.

---

**CR-OWL-3 — Tick-window constraint and design-target coverage proof**

*Tick-interval determinism assumption:* This proof assumes `_cycleTimer` advances by exactly `1.0 / TICK_RATE_HZ` seconds per tick (fixed-step server tick loop). If the server uses variable-step advancement, the tick boundary calculations below do not hold and this proof requires re-derivation.

At `TICK_RATE_HZ = 20` (50ms ticks) and `MAX_WRAP_WINDOW_TICKS = 2`, the wrap correction window is 100ms after the Beat.

*Design-target coverage:* A player tapping during the grace window (cycle ≥ `BaseGraceThreshold × CycleDuration = 0.92s`) at OWL = 75ms (150ms RTT — the design-target ceiling) sends `NotifySkillUsed` at cycle time 0.97s. The packet arrives at server time 0.97 + 0.075 = 1.045s. The Beat fires at 1.0s. The packet arrives 45ms after the Beat — within tick T+1 (50ms tick boundary). `ServerTickNumber - LastBeatServerTick = 1 ≤ 2` → wrap correction activates ✓.

Worst case: player taps at exactly 0.92s (grace threshold entry), OWL = 75ms. Packet arrives at 0.92 + 0.075 = 0.995s — *before* the Beat fires (Beat at 1.0s). `signedAdjusted = 0.995 - 0.075 = 0.92 > 0` → no wrap correction needed; normal path activates ✓.

*Coverage floor:* Wrap correction is guaranteed to activate for all OWL ≤ 75ms (a Beat-boundary packet arrives ≤1.5 ticks after the Beat, within the 2-tick window). For OWL in the range 76–120ms, a packet arriving more than 2 ticks (>100ms) after the Beat will not receive wrap correction even though CR-NET-8.3 has not yet suspended compensation. This degradation is acceptable: at OWL > 100ms, grace window timing precision has already decreased, and CR-NET-8.3's 120ms suspension threshold was chosen with this coverage cliff in mind.

*Threshold consistency:* At `MAX_COMPENSATABLE_OWL_MS = 120ms`, compensation is suspended by `networking-core.md` CR-NET-8.3 regardless of this formula. A Beat-boundary packet at 120ms OWL arrives 120ms after the Beat (2.4 ticks). Since compensation is already suspended, the formula is never evaluated — the window boundary and the compensation threshold do not conflict.

**Upper bound constraint on MAX_WRAP_WINDOW_TICKS:**

```
MAX_WRAP_WINDOW_TICKS ≤ floor(MAX_COMPENSATABLE_OWL_MS / (1000 / TICK_RATE_HZ))
                       = floor(120 / 50) = 2
```

Do not raise `MAX_WRAP_WINDOW_TICKS` above this bound. Above it, the correction window extends into OWL ranges where CR-NET-8.3 has already suspended compensation — the two mechanisms become inconsistent. **Re-derive this bound when `MAX_COMPENSATABLE_OWL_MS` or `TICK_RATE_HZ` change** — the bound is a function of both registry constants, not a fixed value.

---

**CR-OWL-4 — Entity slot allocation contract**

Entity slots are zero-based indices into the zone's `LastBeatServerTick` array. Slots are allocated at entity appearance and stable until the entity leaves or the zone closes.

**Player entities:** Slot allocated at `PlayerJoinedZone` processing. Slot deallocated at `PlayerLeftZone` processing or session TTL expiry. While a player's session is in `Disconnected_SessionActive` (ghost period, 5-minute TTL), their slot is preserved — the ghost entity remains in the zone and its `LastBeatServerTick` continues updating. Slot is NOT reassigned until TTL expires or the zone closes.

**Mob entities:** Slot allocated at mob spawn (Zone Instancing system responsibility). Slot deallocated at mob death or despawn. Slots are returned to a free list and reused. Zone Instancing GDD must specify max concurrent mobs per zone before final array size is known; `MAX_MOBS_PER_ZONE` is a tuning knob (see below) until that GDD is authored.

**Sanitization invariant:** Before a slot is returned to the free list, `LastBeatServerTick[slot]` MUST be reset to `uint.MaxValue`. A freshly recycled slot must never carry a stale tick value from the previous mob occupant — the sentinel guard in CR-OWL-1 depends on this invariant to prevent false-positive wrap correction for newly spawned mobs.

**Array size:** `LastBeatServerTick[MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE]`. Initialized to `uint.MaxValue` entries at zone creation.

**EntityID → slot mapping:** Maintained by the server's zone session manager. The mapping is an internal server concern — never transmitted to clients.

---

**CR-OWL-5 — Adversarial case analysis**

The 2-tick wrap correction window does not create an exploitable advantage for malicious clients. Proof:

1. Wrap correction activates only when `signedAdjusted < 0`, requiring `_cycleTimer < OWL_seconds`.
2. After a Beat at tick T, `_cycleTimer` resets to ~0 and advances by `deltaTime` (50ms) each tick. At tick T+2 (100ms after Beat), `_cycleTimer ≈ 0.1s`.
3. For a client with `OWL_seconds ≤ 0.1s`: `signedAdjusted = 0.1 - OWL_seconds ≥ 0` at tick T+2 → wrap correction does not apply. The window is inert for legitimate OWL values ≤ 100ms by tick T+2.
4. For a client with `OWL_seconds > 0.1s` (OWL > 100ms): compensation is approaching or past the suspension threshold (120ms). A malicious client cannot inflate the server's OWL estimate — OWL is computed by the server from RTT probes (see `networking-core.md` CR-NET-8.1), not reported by the client.
5. A client sending `NotifySkillUsed` at cycle position 0.02s (early, not in grace window) at tick T+2: `_cycleTimer ≈ 0.1s`. `signedAdjusted = 0.1 - OWL_seconds > 0` for any OWL ≤ 100ms → no wrap correction, no exploit.

The only remaining attack surface is OWL spoofing at the RTT probe level, which is a transport-layer security concern outside the scope of this spec (see open questions).

---

## Formulas

**F-OWL-1 — Wrap correction activation condition**

```
wrapCorrectionActive = (signedAdjusted < 0)
                    AND (LastBeatServerTick[slot] != uint.MaxValue)
                    AND ((ServerTickNumber - LastBeatServerTick[slot]) <= MAX_WRAP_WINDOW_TICKS)
```

Variables:
- `signedAdjusted = _cycleTimer - OWL_seconds` (seconds; negative means timer is behind OWL estimate)
- `ServerTickNumber` (uint) — monotonically increasing server tick counter
- `LastBeatServerTick[slot]` (uint) — `ServerTickNumber` at which the entity's most recent Beat fired; `uint.MaxValue` if never fired
- `MAX_WRAP_WINDOW_TICKS` (uint) — tuning knob, default 2

> **Unsigned arithmetic note:** The subtraction `ServerTickNumber - LastBeatServerTick[slot]` must be evaluated as unsigned integer arithmetic. The sentinel guard (`!= uint.MaxValue`) must be evaluated first — short-circuit evaluation ensures the subtraction is never performed against the sentinel value.

**Worked example — design-target OWL, wrap case:**
- `_cycleTimer = 0.02s`, `OWL_seconds = 0.05s`
- `signedAdjusted = -0.03` (negative → wrap candidate)
- Beat fired at tick 400, current tick = 401 → `401 - 400 = 1 ≤ 2` → `wrapCorrectionActive = true`
- `adjustedCycleTimer = 1.0 + (-0.03) = 0.97`
- `0.97 > 0.92 × 1.0` → **skill triggers** ✓

**Worked example — no recent Beat (correct early tap rejection):**
- Same `_cycleTimer = 0.02s`, `OWL_seconds = 0.05s`
- Last Beat was tick 380, current tick = 401 → `401 - 380 = 21 > 2` → `wrapCorrectionActive = false`
- `adjustedCycleTimer = max(-0.03, 0) = 0`
- `0 > 0.92` → **auto-attack fires** ✓ (no Beat recently — tap is early, not wrap-case)

**Worked example — normal case (no wrap needed):**
- `_cycleTimer = 0.97s`, `OWL_seconds = 0.05s`
- `signedAdjusted = 0.92` (positive → no wrap)
- `adjustedCycleTimer = 0.92`
- `0.92 > 0.92` → **false** → auto-attack fires (at exact threshold; `>` is strict)
- With `_cycleTimer = 0.98s`: `signedAdjusted = 0.93 > 0.92` → **skill triggers** ✓

---

## Edge Cases

**EC-OWL-1 — uint overflow on ServerTickNumber**

`ServerTickNumber` is a `uint` (32-bit unsigned). At `TICK_RATE_HZ = 20`, `uint.MaxValue = 4,294,967,295` ticks = ~6.8 years of continuous uptime. Wrap is out of scope for MVP.

The `LastBeatServerTick` sentinel `uint.MaxValue` creates a theoretical false-positive at the first ticks after a `ServerTickNumber` wrap: `ServerTickNumber - uint.MaxValue = 1 ≤ 2` → wrap correction could incorrectly activate. Given the 6.8-year wrap period this is not a practical MVP concern. If long-running servers become a deployment target, promote `ServerTickNumber` and `LastBeatServerTick` to `ulong`.

---

**EC-OWL-2 — Ghost-session reconnect**

When a ghost entity reconnects (`Disconnected_SessionActive → Reconnecting → Connected`, per `networking-session.md` CR-NET-6), their slot is preserved and `LastBeatServerTick[slot]` retains the value from the most recent Beat during the ghost period. This is correct behavior: the entity remained in `COMBAT_ACTIVE` during the ghost period (if applicable), Beats continued firing, and `LastBeatServerTick` reflects the current entity combat state — not the client connection state.

On reconnect, the first `NotifySkillUsed` from the reconnected client is evaluated against the current `LastBeatServerTick`, which may reflect a Beat that fired during the ghost period. If that Beat is within `MAX_WRAP_WINDOW_TICKS`, wrap correction activates. This is acceptable: the Beat is real (the server fired it), and the player's first post-reconnect tap deserves fair evaluation.

---

**EC-OWL-3 — Entity with no Beat history**

If `LastBeatServerTick[slot] = uint.MaxValue` (zone-fresh player, freshly allocated mob slot, or freshly sanitized free-list slot per CR-OWL-4 sanitization invariant), the explicit sentinel guard `(LastBeatServerTick[slot] != uint.MaxValue)` in CR-OWL-1 evaluates to `false` immediately, short-circuiting the entire expression. `wrapCorrectionActive = false` for all such entities — the tick-delta subtraction is never evaluated.

*Startup false-positive eliminated:* Without the sentinel guard, at server ticks 0–1 the uint arithmetic `0 - uint.MaxValue = 1 ≤ 2` would produce a false-positive `wrapCorrectionActive = true` for all sentinel slots. The explicit guard prevents this for any `ServerTickNumber`. The CR-OWL-4 sanitization invariant (reset to `uint.MaxValue` on free-list return) extends the same protection to all recycled mob slots.

---

## Dependencies

### Upstream

| System | Dependency |
|--------|-----------|
| `networking-core.md` CR-NET-8 | OWL threshold (`MAX_COMPENSATABLE_OWL_MS`), hysteresis band, RTT probe mechanism, degraded-mode fallback — all unchanged. This spec replaces only the inline formula in CR-NET-8.2. |
| `networking-core.md` CR-NET-2 | `ServerTickNumber` is defined and advanced here; `LastBeatServerTick` is updated in the Beat evaluation phase of the tick loop (replaces `BeatResolvedThisTick` update). |
| `networking-session.md` | Ghost-session reconnect behavior (EC-OWL-2) — slot preservation during `Disconnected_SessionActive`. |

### Downstream (reads this spec)

| System | What changes |
|--------|-------------|
| `networking-core.md` CR-NET-8.2 | Must cite this spec as the implementation source; replace inline formula with: "OWL compensation formula and `LastBeatServerTick` data structure defined in `networking-owl-compensation.md` CR-OWL-1 and CR-OWL-2." |
| `networking-test-harness.md` | AC-NC-29 cites "inject the `BeatResolvedThisTick` flag via test harness." Must be updated: replace `IZoneTestConfigurator.SetBeatResolvedFlag(EntityID, bool)` with `IZoneTestConfigurator.SetLastBeatServerTick(EntityID, uint)`. AC-OWL-05 adds `INetworkTestObserver.OnSkillGraceWindowEvaluated(EntityID entityId, float adjustedTimer, bool graceTriggers)`. |
| Zone Instancing GDD (Not Started) | Must provide `MAX_MOBS_PER_ZONE` value for the slot array size. Until authored, the placeholder default (150) applies. |

---

## Tuning Knobs

| Knob | Default | Safe Range | Gameplay Impact |
|------|---------|------------|-----------------|
| `MAX_WRAP_WINDOW_TICKS` | 2 | `[1, floor(MAX_COMPENSATABLE_OWL_MS / (1000 / TICK_RATE_HZ))]` | Number of ticks after a Beat during which the wrap correction path is active. **Hard upper bound derived from registry constants:** `floor(MAX_COMPENSATABLE_OWL_MS / (1000 / TICK_RATE_HZ)) = floor(120 / 50) = 2`. Do not raise above this (see CR-OWL-3). **Re-derive when `MAX_COMPENSATABLE_OWL_MS` or `TICK_RATE_HZ` change.** 1 = 50ms window (covers OWL ≤ 25ms — LAN only). 2 = 100ms window (covers design-target 100ms RTT). |
| `MAX_MOBS_PER_ZONE` | 150 | [50, 500] | Slot array size = `MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE`. Confirmed at 150 by Zone Instancing GDD (CR-ZI-8 step 10). Affects only memory allocation (`uint × (50 + 150) = 800 bytes per zone`), not algorithm behavior. Changing requires updating F-NET-9 in `networking-core.md`. |

---

## Acceptance Criteria

**AC-OWL-01 (Logic)** — Given: entity at slot 5, `LastBeatServerTick[5] = 400`, current `ServerTickNumber = 401`, `_cycleTimer = 0.02s`, `OWL_seconds = 0.05s`, `CycleDuration = 1.0s`, `BaseGraceThreshold = 0.92`, `MAX_WRAP_WINDOW_TICKS = 2`. When the server evaluates the OWL compensation formula. Then: `wrapCorrectionActive = true` (sentinel guard passes; 401-400=1≤2; **wrap path exercised**), `adjustedCycleTimer = 1.0 + (0.02 - 0.05) = 0.97`, `graceTriggers = (0.97 > 0.92) = true`. Unit-testable without a running server — inject values directly.

**AC-OWL-02 (Logic)** — Given: same entity and values, but `LastBeatServerTick[5] = 380` (21 ticks ago), `ServerTickNumber = 401`. When the server evaluates compensation. Then: `wrapCorrectionActive = false` (21 > 2; **normal path exercised**), `adjustedCycleTimer = max(0.02 - 0.05, 0) = 0`, `graceTriggers = false`. Unit-testable.

**AC-OWL-03 (Logic)** — Given: entity with `LastBeatServerTick[slot] = uint.MaxValue` (never fired a Beat), any `ServerTickNumber` including 0 and 1. When the server evaluates compensation with `signedAdjusted < 0`. Then: `wrapCorrectionActive = false` (sentinel guard short-circuits; subtraction not evaluated). Unit-testable — initialize the array with `uint.MaxValue` and verify; include `ServerTickNumber = 0` and `ServerTickNumber = 1` as explicit sub-cases covering the startup false-positive scenario.

**AC-OWL-04 (Logic)** — Given: `LastBeatServerTick[slot] = T`, `MAX_WRAP_WINDOW_TICKS = 2`. When `ServerTickNumber = T + 2` (boundary). Then: `wrapCorrectionActive = true` (boundary is inclusive). When `ServerTickNumber = T + 3`. Then: `wrapCorrectionActive = false`. Unit-testable.

**AC-OWL-05 (Integration)** — Given: test client with server-estimated OWL = 50ms (`IZoneTestConfigurator.SetClientOWL(entityId, 0.05f)`), `LastBeatServerTick` set to the tick immediately preceding the current tick (`IZoneTestConfigurator.SetLastBeatServerTick(entityId, ServerTickNumber - 1)`), `_cycleTimer = 0.02s`. When the server evaluates a `NotifySkillUsed` for this entity. Then: wrap correction activates, `adjustedCycleTimer = 0.97`, grace triggers. Verified via `INetworkTestObserver.OnSkillGraceWindowEvaluated(entityId, adjustedTimer, graceTriggers)`. *Requires additions to `networking-test-harness.md`: `IZoneTestConfigurator.SetLastBeatServerTick(EntityID, uint)` and `INetworkTestObserver.OnSkillGraceWindowEvaluated(EntityID, float, bool)`.*

**AC-OWL-06a (Logic)** — Given: a player entity whose `PlayerJoinedZone` has been processed (slot assigned). When the player disconnects and reconnects within the ghost-session TTL (`Disconnected_SessionActive → Reconnecting → Connected`). Then: the slot index for that entity is identical before and after the reconnect cycle; `LastBeatServerTick[slot]` retains the value from the most recent Beat fired during the ghost period. Unit-testable via mock zone session manager.

**AC-OWL-06b (Logic)** — Given: a mob entity occupying slot S that is despawned (death or zone cleanup). When slot S is returned to the free list and subsequently reallocated to a new mob entity. Then: `LastBeatServerTick[S] = uint.MaxValue` at the moment of reallocation (CR-OWL-4 sanitization invariant applied on free-list return). Unit-testable via mock zone instancing system; verify the sentinel reset occurs before the slot becomes available for reuse.

**AC-OWL-06c (Logic)** — Given: a player entity in `Disconnected_SessionActive` state (ghost period) with Beats firing. When `LastBeatServerTick[slot]` is read during the ghost period. Then: the value reflects the tick of the most recent Beat fired (ghost entity remains in COMBAT_ACTIVE; slot is not reset or reused during TTL). Unit-testable via mock session manager with ghost-state injection.

**AC-OWL-07 (CI)** — Given: the registry constants `MAX_COMPENSATABLE_OWL_MS` and `TICK_RATE_HZ`. When the CI pipeline runs the networking constraint verification step. Then: the pipeline asserts `MAX_WRAP_WINDOW_TICKS ≤ floor(MAX_COMPENSATABLE_OWL_MS / (1000 / TICK_RATE_HZ))` and fails the build if violated. This prevents changes to registry constants from silently invalidating the OWL compensation window without a corresponding review of CR-OWL-3.

---

## Open Questions

**OQ-OWL-1 — RTT probe spoofing (security):** CR-OWL-5 establishes that the server's OWL estimate is authoritative (computed from RTT probes, not client-reported). However, a client that can manipulate network timing (VPN, deliberate jitter injection) could inflate the server's OWL estimate server-side, causing the server to over-compensate. This expands the effective grace window beyond `MAX_WRAP_WINDOW_TICKS`. Mitigation options: OWL estimate clamping; OWL estimate history smoothing; anomaly logging when OWL changes rapidly. Resolution: Networking ADR (security section). Non-blocking for this GDD.
