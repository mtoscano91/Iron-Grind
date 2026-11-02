# Story 009: Auto-Face

> **Epic**: Auto-Attack Combat
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rules 20–22, AC-18, AC-19; `design/gdd/hit-detection.md` — CR-HD-7, CR-HD-9, AC-HD-18, AC-HD-19; `design/gdd/networking-wire-protocol.md` — `AutoFaceEvent`
**Requirement**: `TR-aa-008`

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: `AutoFaceEvent` is an R-U batch message to the whole zone, with fixed-point quaternion and direction.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: consumer only.

**Control Manifest Rules (this layer)**:
- Required: facing is replicated by `AutoFaceEvent`, never in `EntityPositionUpdate`
- Required: quaternion and direction guards of CR-NET-7.2
- Forbidden: allocation per event

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md` and `hit-detection.md`, scoped to this story:*

- [ ] On the first auto attempt after activation that passes Rule 10 Step 1 (user decision 2026-10-30: the first auto, not toggle ON), the attacker's facing and movement direction are set toward the target, through `IMovementSystem.InjectFacingOverride`.
- [ ] Later autos do not change facing or movement direction, until the next activation (Rule 21, AC-18, AC-19).
- [ ] The snap fires at any angle, including 180° (Rule 22).
- [ ] `AutoFaceEvent` is a wire type with a `MessageTypeId`, a 18-byte body, a codec, a routing row, and goes to all zone clients through the R-U batch (AC-HD-18).
- [ ] The per-tick position stream's byte count does not change when facing changes (AC-HD-18).
- [ ] An auto swing after the first does not change `FacingAngle` (AC-HD-19, trigger (a) as amended).
- [ ] A one-tick override applies on the tick after the event is emitted, without breaking the client's next `MovementIntentMessage`.

---

## Implementation Notes

- `AutoFaceEvent` schema is at `networking-wire-protocol.md:410`. MessageTypeId `0x0307`, next to `HitCheckResult` `0x0306` (decided at `/story-readiness` 2026-11-02). Append `RUBatchCategory.AutoFaceEvent = 10` after `HitCheckResult` so existing values stay put; the writer emits it right after `HitCheckResult`. Send to every zone client through `IRUBatchCollector.Enqueue`.
- `InjectFacingOverride` queues a one-tick override used on the next tick (CR-MOV-9). Combat runs after movement (Story 006), so the override lands on the next tick's movement. The event describes the direction being applied.
- An out-of-range miss counts as the first auto (user decision at `/story-readiness` 2026-11-02): the snap fires on the first attempt that passes Rule 10 Step 1, whatever Step 2 returns, because Rule 20 fires on the Beat, not on a hit. The later in-range hit does not snap again.
- A zero XZ direction is a consumed no-op in `InjectFacingOverride`; send no event then.
- AC-HD-19(b), a skill cast, belongs to the Skill System; (c) to Enemy AI.

---

## Out of Scope

- Client 50 ms ease; visual review of the 180° snap (OQ-7)
- Skill-cast and monster facing
- Frame-capture evidence for AC-18 and AC-19; server-side tests stand in here, and client visuals are recorded in Combat UI

---

## QA Test Cases

- **Codec**: round trip; quaternion and direction guards reject bad values; a zero direction is not written.
- **First auto**: activate, fire → one `InjectFacingOverride` toward the target and one `AutoFaceEvent` per client.
- **Second auto**: facing changed by the player → no override, no event.
- **Re-activation**: toggle OFF then ON → the next auto snaps again.
- **New target**: re-selecting is an activation → snap again.
- **Prohibited first auto**: no snap until prohibition clears.
- **Out-of-range first auto**: the first attempt misses out of range → snap and event; the later in-range hit → no snap, no event.
- **180°**: facing directly away → snap applied.
- **Same XZ**: attacker on the target → no override, no event, no NaN.
- **AC-HD-18**: count the position packet's bytes with and without a facing change → equal.
- **Zone-wide**: attacker and bystander each get the event.
- **Pipeline**: through `ZoneTickPipeline`, the override changes facing on tick N+1.
- **Allocation**: emit inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_AutoFace_tests.cs` — must exist and pass

**Status**: [x] Created and passing (31 [Test] + 5 [TestCase])

---

## Dependencies

- Depends on: Story 006; Movement `InjectFacingOverride` (Complete)
- Unlocks: Skill System facing (AC-HD-19 b), Enemy AI facing (AC-HD-19 c)

---

## Completion Notes
**Completed**: 2026-11-02
**Criteria**: 7/7 passing (all covered by automated tests; client frame capture for AC-18 / AC-19 is out of scope, Combat UI)
**Deviations**:
- Advisory: readiness decisions (user, 2026-11-02): an out-of-range miss counts as the first auto; `RUBatchCategory.AutoFaceEvent = 10`, MessageTypeId `0x0307`, written after `HitCheckResult`, sent zone-wide (attacker included). `networking-wire-protocol.md` AutoFaceEvent note amended.
- Advisory: drop order puts `AutoFaceEvent` right after `LootBidUpdate`, before `HitCheckResult` (implementer's choice; matches best-effort in `networking-message-criticality.md`).
- Advisory: production wiring is asserted by type only (`AutoAttackFaceOutput` is an `AutoFaceEmitter`); EditMode has no NavMesh, so the built zone cannot drive a real event. The emit is tested with the real `MovementService`, `PositionBroadcastStep` and `RUBatchOutboundStep`.
- Advisory: the `_faceArmed` clears in `ReleasePlayer` and `GoIdle` are unobservable (every activation re-arms); a release-then-reactivate test pins exactly one snap.
- Advisory: `TryReadAutoFaceEvent` does not check the sub-message type id or the entity id on read, same as `TryReadHitCheckResult`; the writer rejects an invalid entity id.
- OQ-HD-4 (AutoFaceEvent for facing changes after the first beat, e.g. skill casts) stays open for the Skill System.
**Test Evidence**: Integration: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_AutoFace_tests.cs` (31 [Test] + 5 [TestCase], incl. a real `ZoneTickPipeline` N / N+1 test and batch drop-order tests) + one test in `tests/EditMode/Networking/ZoneServer_CompositionWiring_tests.cs`. EditMode 5858 / 5729 / 0 failed / 129 skipped, run after the last edit.
**Code Review**: Complete: `/code-review` CHANGES REQUIRED (docs + wiring test), "fix all", items 1-8 applied, item 3 by type, item 9 reduced to writer / layout tests (readers do not validate, as before). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
