# Story 012: Client Intent Producer and Send Cadence

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (7 gaps) → fixed → Ready
> **Layer**: Core (client)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-5, CR-MOV-8 (facing-only message), CR-MOV-10, EC-MOV-15, EC-MOV-21; AC-MOV-1 (wire part), 14
**Requirement**: `TR-mov-009` (advisory: not in `docs/architecture/tr-registry.yaml` yet, like the other TR-mov ids)

**ADR Governing Implementation**: ADR-004 (NGO tick = `TickNumber`; U-U over custom messages); ADR-012
**ADR Decision Summary**: `MovementIntentMessage.TickNumber` is the server tick the intent is for, from NGO's `NetworkTickSystem`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — the client has no zone connection yet; the send goes to a seam
**Engine Notes**: the real send (NGO custom message through the project envelope) comes with a client networking epic; this story stops at `IMovementIntentSink.Send(in MovementIntentMessage)`.

**Control Manifest Rules (this layer)**:
- Forbidden: allocation per tick on the send path
- Required: the C→S envelope (sequence number, SenderEntityID) is added by the transport, not by this producer

---

## API and Send Rule (readiness decisions 1–7)

Namespace `IronGrind.Client.Movement`, in `src/Client/Movement/`:

- **`IMovementIntentSink { void Send(in MovementIntentMessage message); }`** — seam; tests use a fake.
- **`MovementIntentProducer(AutoRunState state, IMovementIntentSink sink)`** — both not null (`ArgumentNullException`). The producer drives Story 011's state (decision 1).
- **`OnTick(uint serverTick, JoystickState joystick, Vector3 cameraFlatForward)`** — called once per network tick by Story 013 with `router.Joystick` and `yaw.FlatForward`. Steps:
  1. **Repeated tick** (decision 7): if a tick was already processed and `serverTick <= lastProcessedTick`, do nothing (no `state.Tick`, no send). A forward jump is fine.
  2. `state.Tick(joystick, cameraFlatForward)`; record `serverTick` as the last processed tick.
  3. `facing = MovementIntentMessageCodec.EncodeFacing(state.Facing.YawDeg)`.
  4. **Send rule** (decision 2): send when `state.MoveDirection != Vector3.zero`, **or** `facing != lastSentFacing` (encoded shorts compared — decision 3), **or** nothing has been sent since construction / `Reset()`. Otherwise no message.
  5. Build with `BuildMessage(state.MoveDirection, state.Facing.YawDeg, serverTick)` and call `sink.Send`; store `lastSentFacing`.
- **`internal static MovementIntentMessage BuildMessage(Vector3 moveDirection, float facingDeg, uint serverTick)`** (decision 5): `EncodeDirection(new Vector2(moveDirection.x, moveDirection.z), out wasNonUnit)`; when `wasNonUnit` the codec's `(32767, 0)` is sent and **one `Debug.LogWarning` per substituted message** is logged; `FacingAngle = EncodeFacing(facingDeg)`; `TickNumber = serverTick`. Tests call it directly (`IronGrind.Client` has `InternalsVisibleTo` for the EditMode test assembly).
- **`Reset()`** (decision 6): forgets the last sent facing and the last processed tick, and calls `state.Reset()`. Story 013 calls it on zone entry and reconnect (EC-MOV-15).
- **Release** (decision 2): no extra zero-direction message. The server treats a missing message as zero input (CR-MOV-10); a release normally turns the facing to the camera forward (Story 011), which sends one (0, 0) facing message by the rule above.
- **Prohibited** (decision 4): no branch here. While Prohibited, Story 011's `AutoRunState` gives `MoveDirection = zero` and keeps updating the facing, so the rule above sends only facing changes, with dir (0, 0) (CR-MOV-8).

---

## Acceptance Criteria

- [ ] `MovementIntentProducer.OnTick(serverTick, joystick, cameraFlatForward)` ticks `AutoRunState` once and hands at most one `MovementIntentMessage` per tick to `IMovementIntentSink`; a repeated or older `serverTick` does nothing.
- [ ] **AC-MOV-14**: under continuous input, exactly one message per tick (20 Hz): over a simulated 5-second window (100 ticks) the count is 100 (the 95–105 band of the GDD covers device jitter; the deterministic test asserts 100).
- [ ] Suppression (CR-MOV-10 "may suppress"): direction zero and encoded facing unchanged since the last send → no message that tick. The first tick after construction or `Reset()` always sends. No extra message on release.
- [ ] Facing change with no movement (camera rotated, joystick at rest, auto-run off) → a message with dir (0, 0) and the new facing.
- [ ] **CR-MOV-8**: while Prohibited, direction changes are not sent as movement; a facing change sends dir (0, 0) + the new `FacingAngle`.
- [ ] **AC-MOV-1 (wire) / EC-MOV-21**: a dead-zone input → dirX = dirZ = 0. Any direction past the dead zone encodes to at least one non-zero short (a test sweeps angles at the dead-zone edge).
- [ ] `TickNumber` = the `serverTick` argument (the caller supplies NGO's server tick estimate); `FacingAngle` = `EncodeFacing(Facing.YawDeg)`.
- [ ] Direction is encoded with `MovementIntentMessageCodec.EncodeDirection(dir, out wasNonUnit)` (Story 002); when `wasNonUnit` is true the producer sends the substituted `(32767, 0)` and logs one non-unit-direction warning per substituted message (wire GDD pre-encode guard).
- [ ] **EC-MOV-15**: `Reset()` starts fresh — auto-run off (`AutoRunState.Reset()`), no last sent facing, no last processed tick.
- [ ] No allocation on `OnTick` with a non-allocating sink.

---

## Implementation Notes

- Files: `src/Client/Movement/MovementIntentProducer.cs`, `IMovementIntentSink.cs`.
- Encode with Story 002's helpers (Foundation) — no second encoder.
- AC-MOV-22 (≤ 0.5 ms per frame on a reference device) is **deferred** to on-device profiling (epic Definition of Done); an EditMode allocation test stands in.
- Readiness decisions (2026-10-29, user: "continue" = apply all seven): (1) the producer takes `AutoRunState` and receives joystick + camera forward per call (no router/camera dependency); (2) send when moving, when the encoded facing changed, or on the first tick; no release message; (3) facing compared as the encoded short; (4) Prohibited needs no branch (Story 011 output); (5) `internal static BuildMessage` makes the non-unit path testable; (6) `Reset()` for zone entry / reconnect; (7) a repeated or older tick is ignored.

---

## Out of Scope

- The real network send (client networking epic)
- Story 013: calling `OnTick` and `Reset` from the client loop

---

## QA Test Cases

- **AC-MOV-14**: 100 ticks of held joystick → 100 sink calls with TickNumbers 1..100 in order.
- **First tick**: new producer, no input → tick 1 sends one message, dir (0, 0), facing 0.
- **Suppression**: ticks 2–10 idle → 0 more calls; camera rotated before tick 5 → 1 call on tick 5 with dir (0, 0) and the new facing.
- **Release**: joystick held at 90° (camera forward fixed) for ticks 1–5, released at tick 6 → tick 6 sends dir (0, 0) with the camera facing (facing changed); ticks 7+ → no calls. Held at 0° then released → tick 6 sends nothing (facing unchanged).
- **Prohibited**: joystick held + Prohibited → messages have dir (0, 0); facing changed by the joystick or a camera rotation → dir (0, 0) + the new facing; facing unchanged → no message.
- **EC-MOV-21**: radius 80, dead zone 0.15 — offsets of 12 px at 0°, 1°, …, 359° → every message has a non-zero dirX or dirZ. Dead-zone offset (11 px) → dir (0, 0).
- **Auto-run**: flag on, no touch → one message per tick with the facing direction.
- **Repeated tick**: `OnTick(5)` twice → one call; `OnTick(4)` after it → nothing; `OnTick(8)` → sends (jump fine).
- **Non-unit**: `BuildMessage(new Vector3(2, 0, 2), 0, 1)` → `(32767, 0)` and one warning (`LogAssert.Expect(LogType.Warning, …)`).
- **Reset**: after sends, `Reset()` → the next tick sends again even with unchanged facing; auto-run flag off; `OnTick` with a smaller tick than before is accepted.
- **Null ctor**: null state or null sink → `ArgumentNullException`.
- **No allocation**: 100 ticks inside `Is.Not.AllocatingGCMemory()` with a non-allocating fake sink.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Client/Movement/ClientMovement_IntentProducer_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 010, 011 (all Complete)
- Unlocks: Story 013
