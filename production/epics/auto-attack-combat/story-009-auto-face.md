# Story 009: Auto-Face

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rules 20–22, AC-18, AC-19; `design/gdd/hit-detection.md` — CR-HD-7, CR-HD-9, AC-HD-18, AC-HD-19; `design/gdd/networking-wire-protocol.md` — `AutoFaceEvent`
**Requirement**: `TR-aa-008` (placeholder)

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

- `AutoFaceEvent` schema is at `networking-wire-protocol.md:410`. Message id goes next to `HitCheckResult` `0x0306`; check the registry. `RUBatchCategory` has no `AutoFaceEvent` member; settle at `/story-readiness` which category it uses or whether one is added, then send through `IRUBatchCollector.Enqueue`.
- `InjectFacingOverride` queues a one-tick override used on the next tick (CR-MOV-9). Combat runs after movement (Story 006), so the override lands on the next tick's movement. The event describes the direction being applied.
- Settle at `/story-readiness`: does a first auto that misses out of range count as the first auto? Recommendation: yes, since Rule 20 fires on the Beat, not on a hit, and it turns the player toward the target.
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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006; Movement `InjectFacingOverride` (Complete)
- Unlocks: Skill System facing (AC-HD-19 b), Enemy AI facing (AC-HD-19 c)
