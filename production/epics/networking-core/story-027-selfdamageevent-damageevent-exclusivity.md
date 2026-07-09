# Story 027: SelfDamageEvent vs DamageEvent Delivery Exclusivity

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-message-criticality.md` + `design/gdd/networking-channel-contract.md`
**Requirement**: `TR-net-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR — this is a routing/delivery-scope rule layered on top of Story 025's channel table, specific to the attacker's own combat feedback.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: `SelfDamageEvent` (R-OD) delivered to the attacker's own client exclusively; `DamageEvent` (R-U) delivered to all zone clients EXCEPT the attacker — source: MCR-2, CCR-3
- Forbidden: `DamageEvent` substituted as a fallback for a missed `SelfDamageEvent` — a `DamageEvent` matching the attacker's own ID never arrives at the attacker's client, so this fallback path is structurally unreachable — source: EC-MCR-2

---

## Acceptance Criteria

*From `design/gdd/networking-message-criticality.md` and `networking-channel-contract.md`, scoped to this story:*

- [ ] **AC-NC-37** [BLOCKING] (Integration, wire-protocol's numbering): Given clients A (attacker) and B (target), when A attacks B in a single tick, then A's capture contains exactly one `SelfDamageEvent` (R-OD) with the correct attacker/target IDs and does NOT receive a `DamageEvent` for that attack; B's capture contains exactly one `DamageEvent` (R-U) with the same pair and NO `SelfDamageEvent`.
- [ ] **AC-MCR-02** [BLOCKING] (Integration): Given a client landing a successful auto-attack timing window, when the server processes Beat resolution, then the attacker's own client receives `SelfDamageEvent` via R-OD within 2 tick periods (100ms), and its `finalDamage` matches the Damage Calculation system's computed value for that hit.
- [ ] **AC-MCR-05** [BLOCKING] (Integration — `CycleTimerBroadcast` loss tolerance, adjacent Pillar-2 concern sharing this story's delivery-guarantee theme): Given a client receiving `CycleTimerBroadcast` at 20Hz, when 3 consecutive packets are dropped, then the charge bar continues advancing via client-side linear interpolation, and on the 4th packet's arrival the position differs from server-authoritative by ≤10% of a full cycle.
- [ ] **AC-MCR-08** [BLOCKING] (Integration — non-freeze/non-reset guarantee, companion to AC-MCR-05): Given the same 3-consecutive-drop scenario, then the charge bar position is monotonically non-decreasing throughout the drop window (never freezes or resets to zero).
- [ ] **AC-CCR-04** [BLOCKING] (Integration): Given a player landing a hit, when `SelfDamageEvent`/`DamageEvent` are emitted, then `SelfDamageEvent` reaches exactly one client (the attacker) and `DamageEvent` reaches all zone clients except the attacker — no client receives both for the same `(attackerEntityId, targetEntityId, ServerTickNumber)` triple.
- [ ] **AC-CCR-07** [BLOCKING] (Integration — stale-discard applied to this delivery path): Given `GoldSyncEvent`/`EntityHealthUpdate` delivered out of order (simulated reordering), when the client's deserializer processes them, then `IsNewerVersion`/`IsTickExpired` (Story 005) is used — no raw `uint` comparison path exists.
- [ ] **AC-CCR-08** [BLOCKING] (Integration): Given an `EntityPositionUpdate` stream interrupted for 3 ticks then resumed, then the resumed packet alone produces a correct absolute position (no delta-decoding dependency on the dropped packets).

---

## Implementation Notes

*Derived from MCR-2, MCR-5a, CCR-3, EC-MCR-2, EC-CCR-2:*

- **Recipient-set construction**: `SelfDamageEvent`'s recipient set must be built as a singleton `{attackerEntityId}` on the server side — NOT derived from filtering the zone-wide broadcast list down to one entry. Constructing it as a filter risks a future refactor accidentally widening the filter; constructing it as an explicit singleton makes the exclusivity structural.
- **EC-MCR-2 (missed SelfDamageEvent)**: if the attacker's client doesn't receive `SelfDamageEvent` within 5 tick periods (250ms) of the triggering Beat, it must suppress the damage number entirely — no phantom display, and critically no fallback to `DamageEvent` (structurally impossible anyway, since `DamageEvent` is never sent to the attacker). Also suppress any `SelfDamageEvent` whose `ServerTickNumber` is >5 ticks stale on arrival.
- **EC-CCR-2 (wrong-recipient defense)**: the receiving client asserts `attackerEntityId == localPlayerEntityId` on any `SelfDamageEvent` receipt; a mismatch logs `SelfDamageDirectionViolation` and suppresses display — this is a client-side sanity check layered on top of the server-side singleton-recipient guarantee, not a substitute for it.
- **MCR-5a self-correcting tolerance** (`CycleTimerBroadcast`): loss is tolerated for ≤3 consecutive packets via client-side linear interpolation from the last two received values; the 10% tolerance bound assumes cycle speed ≤1.0 full cycle/second. This message must never be omitted server-side under congestion (Story 007 already enforces this at the batch-framing layer — this story only tests the client-side interpolation/recovery behavior).
- **CCR-4 R-U/U-U invariants** (reiterated here since they gate AC-CCR-07/08): R-U receivers must not assume ordering and must use version/tick comparison, never raw `uint`; U-U messages must be self-contained (no delta encoding, no sequence-dependent state) so a resumed stream after loss produces a correct display from that one packet alone.

---

## Out of Scope

*Handled by neighbouring stories:*

- The message routing table these rules layer on top of — Story 025
- The batch framing / overflow-drop mechanics that could cause the packet loss these ACs test recovery from — Story 007
- Real Damage Calculation system logic — existing/future Combat epics (this story only proves wire delivery scope and client recovery behavior)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/MessageRouting_SelfDamageExclusivity_tests.cs`

- **AC-NC-37**/**AC-CCR-04**: Given an attack from A on B, then delivery-scope exclusivity holds exactly as described.
- **AC-MCR-02**: Given a successful Beat-resolved hit, then `SelfDamageEvent` arrives within 100ms with the correct damage value.
- **AC-MCR-05**/**AC-MCR-08**: Given 3 dropped `CycleTimerBroadcast` packets, then interpolation keeps the bar moving monotonically and converges within 10% on packet 4.
- **AC-CCR-07**: Given reordered `GoldSyncEvent`/`EntityHealthUpdate`, then stale-discard uses tick/version comparison, never raw `uint`.
- **AC-CCR-08**: Given a 3-tick `EntityPositionUpdate` gap then resume, then the resumed packet alone yields a correct absolute position.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Networking/MessageRouting_SelfDamageExclusivity_tests.cs` OR documented playtest evidence

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 025 (routing table), Story 005 (stale-discard helpers), Story 007 (batch framing this loss-tolerance builds on)
- Unlocks: None — completes the Message Routing cluster
