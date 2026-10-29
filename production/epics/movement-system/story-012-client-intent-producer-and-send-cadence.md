# Story 012: Client Intent Producer and Send Cadence

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core (client)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-5, CR-MOV-8 (facing-only message), CR-MOV-10, EC-MOV-15, EC-MOV-21; AC-MOV-1 (wire part), 14
**Requirement**: `TR-mov-009`

**ADR Governing Implementation**: ADR-004 (NGO tick = `TickNumber`; U-U over custom messages); ADR-012
**ADR Decision Summary**: `MovementIntentMessage.TickNumber` is the server tick the intent is for, from NGO's `NetworkTickSystem`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — the client has no zone connection yet; the send goes to a seam
**Engine Notes**: the real send (NGO custom message through the project envelope) comes with a client networking epic; this story stops at `IMovementIntentSink.Send(in MovementIntentMessage)`.

**Control Manifest Rules (this layer)**:
- Forbidden: allocation per tick on the send path
- Required: the C→S envelope (sequence number, SenderEntityID) is added by the transport, not by this producer

---

## Acceptance Criteria

- [ ] `MovementIntentProducer.OnTick(uint serverTick)` builds at most one `MovementIntentMessage` per tick from Stories 010/011 (direction, facing) and hands it to `IMovementIntentSink`.
- [ ] **AC-MOV-14**: under continuous input, exactly one message per tick (20 Hz): over a simulated 5-second window (100 ticks) the count is 100 (the 95–105 band of the GDD covers device jitter; the deterministic test asserts 100).
- [ ] Suppression: no joystick input and auto-run off and facing unchanged since the last send → no message that tick (CR-MOV-10 "may suppress"). **Readiness decides** whether one final zero-direction message is sent on release (the server already treats a missing message as zero input).
- [ ] Facing change with no movement (camera rotated, joystick at rest, auto-run off) → a message with dir (0, 0) and the new facing.
- [ ] **CR-MOV-8**: while Prohibited, direction changes are not sent as movement; a facing change sends dir (0, 0) + the new `FacingAngle`.
- [ ] **AC-MOV-1 (wire) / EC-MOV-21**: a dead-zone input → dirX = dirZ = 0. Any direction past the dead zone encodes to at least one non-zero short (a test sweeps angles at the dead-zone edge).
- [ ] `TickNumber` = the `serverTick` argument (the caller supplies NGO's server tick estimate).
- [ ] Direction is encoded with `MovementIntentMessageCodec.EncodeDirection(dir, out wasNonUnit)` (Story 002); when `wasNonUnit` is true the producer sends the substituted `(32767, 0)` and logs one non-unit-direction anomaly warning (wire GDD pre-encode guard).
- [ ] **EC-MOV-15**: on reconnect the producer starts fresh (auto-run off, no last-sent facing).

---

## Implementation Notes

- Files: `src/Client/Movement/MovementIntentProducer.cs`, `IMovementIntentSink.cs`.
- Encode with Story 002's helpers (Foundation) — no second encoder.
- AC-MOV-22 (≤ 0.5 ms per frame on a reference device) is **deferred** to on-device profiling (epic Definition of Done); an EditMode allocation test stands in.

---

## Out of Scope

- The real network send (client networking epic)
- Story 013: calling `OnTick` from the client loop

---

## QA Test Cases

- **AC-MOV-14**: 100 ticks of held joystick → 100 sink calls with TickNumbers 1..100 in order.
- **Suppression**: 10 idle ticks → 0 calls; camera rotated on tick 5 → 1 call on tick 5 with dir (0, 0).
- **Release**: held for ticks 1–5, released at tick 6 → the readiness rule for tick 6; ticks 7+ → no calls.
- **Prohibited**: joystick held + Prohibited → no movement direction sent; facing changed → dir (0, 0) + facing.
- **EC-MOV-21**: radius 80, dead zone 0.15 — offsets of 12 px at 0°, 1°, …, 359° → every message has a non-zero dirX or dirZ.
- **Auto-run**: flag on, no touch → one message per tick with the facing direction.
- **No allocation**: 100 ticks inside `Is.Not.AllocatingGCMemory()` with a non-allocating fake sink.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Client/Movement/ClientMovement_IntentProducer_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 010, 011
- Unlocks: Story 013
