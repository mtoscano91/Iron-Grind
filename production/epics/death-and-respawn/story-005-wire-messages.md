# Story 005: Death and Respawn Wire Messages

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-6, CR-DR-7, CR-DR-13, Event 5, AC-DR-11, AC-DR-NEW-1 to 3; `networking-wire-protocol.md` Death & Respawn schemas; `networking-relevance-filter.md` RFR-7
**Requirement**: `TR-dr-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture; ADR-014: Inbound Request Dispatch and Tick Order; ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: Messages are enqueued on the tick that produces them and flushed at the end of the tick's network step; outbound delivery goes through the zone's existing `ClientMessageOutbox`. Wire types live in `IronGrind.Foundation` only with a named client consumer.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (`BinaryPrimitives`, spans, `stackalloc`).

**Control Manifest Rules (this layer)**:
- Required: R-OD messages use MessageTypeIDs 0xE000–0xEFFF (wire protocol; memory note "message id range check"). Readiness 2026-11-18: 0xE160–0xE163 are free (highest nearby ID in use is 0xE151; Skill System reserves 0xE150–0xE15F).
- Required: a wire type goes in `IronGrind.Foundation` only with a named client consumer, added to the boundary test's allow-list (ADR-012); consumer: the client death overlay / VFX (presentation epic)

**Delivery scope (RFR-7 governs)**: `EntityDied` is a zone-wide R-OD broadcast to every client of the zone, including the dying player; no proximity or relevance-set filtering at MVP. GDD CR-DR-7 and the `TR-dr-005` text still say "relevance-filtered"; RFR-7 is the later, more specific rule. Fix that wording at the next GDD pass (advisory, not this story).

---

## Acceptance Criteria

- [x] **AC-1 Structs, codecs and routing**: in `src/Foundation/Networking/WireProtocol/`, each a `readonly struct` with `MessageTypeId`, `BodySize` and a codec (`WriteBody` / `ReadBody`) matching `networking-wire-protocol.md` byte for byte, little-endian:
  - `EntityDied` 0xE160, 4-byte body (`entityId`)
  - `DeathStateEntered` 0xE161, 5-byte body (`entityId`, `byte respawnTimerSeconds`)
  - `EntityRespawned` 0xE162, 10-byte body (`entityId`, position via `WireFixedPointCodec.EncodePosition` / `DecodePosition`)
  - `RespawnConfirmed` 0xE163, 4-byte body (`entityId`)

  Each is registered in `MessageRoutingRegistry` with `AddPriority` (R-OD priority path, not cap-exempt), under a comment "Death & Respawn Story 005 … 0xE160-0xE16F reserved for the epic": `EntityDied` and `EntityRespawned` → `MessageDirection.ServerToAllZoneClients`; `DeathStateEntered` and `RespawnConfirmed` → `MessageDirection.ServerToOwningClient`. The existing routing / criticality table tests pass with the new entries.
- [x] **AC-2 Output seam**: new `IDeathMessageOutput` in `src/ServerLogic/DeathAndRespawn/`:
  - `void SendDeath(EntityID entity, byte respawnTimerSeconds)`
  - `void SendRespawn(EntityID entity, Vector3 position)`

  `DeathAndRespawnService` takes it as a new last constructor parameter (`ArgumentNullException` on null) and calls it from the two existing placeholders (`EnqueueDeathMessages`, `EnqueueRespawnMessages`). `respawnTimerSeconds` = `(byte)Mathf.RoundToInt(config.RespawnDelaySeconds)`, computed once in the constructor (the constructor's `Validate()` already bounds the seconds). Service tests use a recording fake.
- [x] **AC-3 Death messages (CR-DR-6, CR-DR-7, AC-DR-NEW-1, AC-DR-NEW-2)**: on a non-ghost death the service calls `SendDeath(entity, respawnTimerSeconds)` exactly once, in the death tick (inside the T-1 handler, after the state and seam calls). A duplicate death (already Dead / Respawning), a mob or an unknown id calls nothing.
- [x] **AC-4 Respawn messages (CR-DR-13, AC-DR-NEW-3)**: at T-3 the service calls `SendRespawn(entity, state.RespawnPosition)` exactly once, in the T-3 tick, after the slot-state step. It is called even if an earlier T-3 step threw (Story 004's per-step catch). The position is the one teleported to: `(0,0,0)` when the anchor was never resolved (Story 004 AC-2 fallback). No call while the player is still retrying the anchor.
- [x] **AC-5 Ghost (AC-DR-11, Event 5)**: a ghost death calls neither `SendDeath` nor `SendRespawn` (the ghost path never reaches T-1's send or T-3).
- [x] **AC-6 Production emitter**: new `DeathMessageEmitter : IDeathMessageOutput` (`src/ServerLogic/DeathAndRespawn/`), constructed from `ZoneSlotPool` and `ClientMessageOutbox` (null → `ArgumentNullException`). Patterns: zone-wide sends like `AppearanceChangedSender`, owner-only sends like `SkillUnlockOutputEmitter`.
  - `SendDeath`: `DeathStateEntered` to the dying player's client (`Enqueue`), then `EntityDied` to every client (`EnqueueToAllClients`, the dying player included).
  - `SendRespawn`: `EntityRespawned` to every client (`EnqueueToAllClients`), then `RespawnConfirmed` to the respawning player's client (`Enqueue`).
  - All `isCapExempt: false`. Bodies are encoded into `stackalloc` buffers.
- [x] **AC-7 Disconnected recipient**: zone-wide sends reach only clients currently in the outbox (a transport disconnect removes the client, so it is skipped with no error). An owner-only send is skipped silently (no log) when the entity has no pool slot or its client is not in the outbox (`TryGetSequenceCounter` false). The reconnect snapshot covers the skipped player (Story 007).
- [x] **AC-8 Fault containment**: a send fault never escapes the emitter; the T-1 handler and the T-3 respawn complete as normal. The outbox buffers in `Enqueue` and contains its own send faults at `Flush` (it logs them itself), so the emitter never throws and logs nothing of its own. The emitter also keeps a defensive catch (logged once per emitter, `[DeathMessageEmitter] …Threw`), as `SkillUnlockOutputEmitter` does; it is not reachable through the sealed outbox and is not tested (code review 2026-11-18).
- [x] **AC-9 Position overflow**: an `EntityRespawned` position outside ±327.67 m on any axis is clamped by `WireFixedPointCodec.EncodePosition`, which logs its single aggregated `[WireFixedPointCodec] EncodePosition` warning; the message is still sent.
- [x] **AC-10 Boundary test**: the four structs and four codecs are on the Foundation allow-list in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`, each with its consumer comment (`// consumer (planned): client death overlay / VFX (… ; Death & Respawn Story 005)`); `AssemblyBoundary_Tests` pass.

---

## Implementation Notes

- Do not build a new send path: the emitter calls `ClientMessageOutbox.Enqueue` / `EnqueueToAllClients` directly; the outbox's end-of-tick `Flush` delivers them (ADR-010, ADR-014).
- Order inside one call is fixed by AC-6 (owner message first on death, zone message first on respawn), so tests can assert it.
- The emitter is not wired into `ZoneServerComposition` here; Story 008 composes it with the service.
- "Not cap-exempt" (AC-1) is the `isCapExempt: false` argument on each enqueue; `MessageRoutingEntry` has no cap-exempt field, so the routing test checks channel, priority path and direction only (code review 2026-11-18).
- `SelfPositionUpdate` suppression while Dead (wire protocol, AC-NC-39) does not apply yet: nothing in production emits `SelfPositionUpdate` (client-side prediction is not on the roadmap; checked at readiness 2026-11-18). Record this in the Completion Notes; no code.
- **Performance**: no allocation on the send path (stack buffers, no closures, no LINQ); cost O(zone clients) per zone-wide send, only on a death or respawn event.

---

## Out of Scope

- Client decoding and the death overlay (presentation epics)
- Composition wiring of `DeathMessageEmitter` (Story 008)
- Story 007: reconnect snapshot fields
- Proximity filtering of `EntityDied` (RFR-7: out of MVP scope)
- GDD CR-DR-7 / `TR-dr-005` "relevance-filtered" wording fix (next GDD pass)

---

## QA Test Cases

*Lean mode: refined at `/story-readiness` 2026-11-18.*

**Codecs** (`DeathAndRespawnMessages_Codec_tests.cs`)
- **AC-1a**: each of the four: encode → decode equals input; `WriteBody` returns `BodySize` (4 / 5 / 10 / 4); exact bytes for a known value (e.g. entity 0x01020304 → `04 03 02 01`; respawnTimerSeconds 7 at offset 4; position (12.75, 0, −85.23) → posX 1275, posY 0, posZ −8523 at offsets 4/6/8).
- **AC-1b**: the four IDs are 0xE160–0xE163, distinct, and each is registered in `MessageRoutingRegistry` with the direction in AC-1, priority path, not cap-exempt.
- **AC-9**: `EntityRespawned` with x = 400 → posX = 32767 and one `[WireFixedPointCodec] EncodePosition` warning (`LogAssert.Expect`).

**Service** (`DeathAndRespawn_WireMessages_tests.cs`, recording `IDeathMessageOutput` fake)
- **AC-2**: null output → `ArgumentNullException`; config 7.0 s → `respawnTimerSeconds` 7; 5.4 s → 5; 9.6 s → 10 (avoid .5 values: `Mathf.RoundToInt` rounds half to even).
- **AC-3**: P dies → one `SendDeath(P, 7)` in the death tick; second death event for P → still one; mob / unknown id → none.
- **AC-4**: tick to `RespawnTick` → one `SendRespawn(P, respawnPoint)` in that tick, after the slot state is Connected; vitals seam throws → `SendRespawn` still called once; anchor unresolved and retrying → no call; retries exhausted → `SendRespawn(P, Vector3.zero)`.
- **AC-5**: ghost death → zero `SendDeath` and zero `SendRespawn`, also after ticking past the delay.

**Emitter** (`DeathAndRespawn_WireMessages_tests.cs`, real `ClientMessageOutbox` with a recording send seam, flushed)
- **AC-6**: zone with clients P, Q, R. `SendDeath(P, 7)` → P receives `DeathStateEntered` then `EntityDied(P)`; Q and R receive only `EntityDied(P)`. `SendRespawn(P, pos)` → P, Q, R receive `EntityRespawned(P, pos)`; only P receives `RespawnConfirmed(P)`, after its `EntityRespawned`.
- **AC-7**: P removed from the outbox (still in the pool) → `SendDeath(P, 7)` sends `EntityDied` to Q and R, nothing to P, no log (`LogAssert.NoUnexpectedReceived`); entity with no pool slot → zone-wide still sent, owner message skipped, no log.
- **AC-8**: send seam that throws → no exception out of `SendDeath` / `SendRespawn`; only the outbox's own error log at `Flush` (expected exactly), no `[DeathMessageEmitter]` log, no packet delivered.

**Boundary**
- **AC-10**: `AssemblyBoundary_Tests` pass with the eight new Foundation types.

---

## Test Evidence

**Story Type**: Logic
**Required evidence** — must exist and pass:
- `tests/EditMode/Networking/DeathAndRespawnMessages_Codec_tests.cs` (beside `AppearanceChangedEvent_Codec_tests.cs`)
- `tests/EditMode/DeathAndRespawn/DeathAndRespawn_WireMessages_tests.cs` (service + emitter)
- `tests/EditMode/Architecture/AssemblyBoundary_Tests.cs` (existing, with the updated allow-list)

**Status**: [x] Created and passing

---

## Dependencies

- Depends on: Stories 002, 003, 004 (all Complete)
- Unlocks: Story 008

---

## Completion Notes
**Completed**: 2026-11-18
**Criteria**: 10/10 passing (all automated)
**Deviations**: Advisory — codecs decode with `TryReadBody` (bool, rejects a wrong length) like `AppearanceChangedEventCodec`, not `ReadBody`. AC-8 reworded at code review: the outbox contains its own send faults, so the emitter's catch is defensive and untested. "Not cap-exempt" is the `isCapExempt: false` enqueue argument; `MessageRoutingEntry` has no such field, so the routing test does not assert it. AC-NC-39 (`SelfPositionUpdate` suppression while Dead) needs no code: nothing in production emits `SelfPositionUpdate`. GDD CR-DR-7 and `TR-dr-005` still say "relevance-filtered"; the implementation follows RFR-7 (zone-wide) — fix the wording at the next GDD pass. Pillars: zone-wide messages `SocialSignals`, owner-only `Infrastructure`.
**Test Evidence**: Logic — `tests/EditMode/Networking/DeathAndRespawnMessages_Codec_tests.cs` (10 [Test] + 16 [TestCase]), `tests/EditMode/DeathAndRespawn/DeathAndRespawn_WireMessages_tests.cs` (25 [Test] + 3 [TestCase]); full EditMode `-testCategory "!Database"` 7992/7992 (2026-11-18 13:53:04Z)
**Code Review**: Complete (`/code-review` with unity-specialist CLEAN + qa-tester GAPS; all required changes and suggestions applied). Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
