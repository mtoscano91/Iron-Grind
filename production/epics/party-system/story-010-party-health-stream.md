# Story 010: Party Health Stream

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — Interactions (`PartyMemberHealthUpdate` bandwidth management, with the 2026-11-19 Story 010 readiness note); `networking-wire-protocol.md` `PartyMemberHealthUpdate` (0x0305, R-U batch, 20-byte body) and R-U serialization order; `networking-relevance-filter.md` EC-RFR-2
**Requirement**: `TR-party-011` (health stream)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: One tick runs completion callbacks → client requests → simulation → outbound; R-U batch content is queued before the outbound writers flush.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: absolute HP / MP values, never deltas
- Guardrail: ≤ 3 updates per member per tick at `MaxPartySize` 4; zero allocation per tick after warm-up

---

## Acceptance Criteria

- [x] **AC-1 Recipients**: `PartyHealthStreamStep.Run(currentTick)` visits every real party in `Active` state (solo parties and `Disbanding` parties send nothing). For each ordered pair (recipient R, member M), R ≠ M, it considers an update only when both R's and M's derived status is Online or Dead. Nothing is sent about or to a Ghost or OutOfZone member. A recipient with no pool slot or no outbox connection is skipped silently.
- [x] **AC-2 Values**: the update carries M's `EntityID` and absolute values from `IZoneEntityStatReader`: `currentHP = FloorToInt(GetCurrentHP)`, `maxHP = GetEffectiveStat(MaxHP)`, `currentMP = FloorToInt(GetCurrentMP)`, `maxMP = GetEffectiveStat(MaxMP)`. A member whose derived status is Dead is sent with `currentHP = 0` whatever the stat reads (EC-RFR-2).
- [x] **AC-3 Send rule**: a last-sent record (currentHP, maxHP, currentMP, maxMP) is kept per (R, M). With `f = HpDeltaThresholdFraction` (existing knob, default 0.05), an update is sent when any of these is true, and otherwise suppressed:
  1. no record exists for (R, M) (first send, even at full);
  2. `maxHP` or `maxMP` differs from the record;
  3. `|currentHP − lastHP| ≥ f × maxHP`, or `|currentMP − lastMP| ≥ f × maxMP` (compared in float; a max ≤ 0 makes any change of that value qualify);
  4. HP and MP are both full and the record was not both full (the bar reaches full);
  5. `currentHP` is 0 and `lastHP` was not, or `lastHP` was 0 and `currentHP` is not.
  A sent update overwrites the record. Values equal to the record never send.
- [x] **AC-4 Full suppression**: when HP == MaxHP and MP == MaxMP and the record is already both full with the same maxima, nothing is sent — however small or large the time since the last send.
- [x] **AC-5 Record lifetime**: a (R, M) record that was not considered this tick (pair broke up, either side Ghost / OutOfZone, party disbanded, a player left the zone) is dropped at the end of `Run`, so the next eligible tick sends afresh (rule 1). Records are kept in storage sized at construction for `zoneCapacity × (MaxPartySize − 1)` pairs; beyond that a pair is still sent every tick it is eligible but not remembered, with one warning per `Run`.
- [x] **AC-6 Batch**: each update goes through `IRUBatchCollector.Enqueue(clientId, RUBatchCategory.PartyMemberHealthUpdate, PartyMemberHealthUpdate.MessageTypeId, body)`, body written with `BatchSubMessageCodec.WritePartyMemberHealthUpdateBody` into a stack buffer. Through the real `RUBatchOutboundStep`, the flushed batch holds the sub-messages after any `EntityHealthUpdate` (the writer's category order).
- [x] **AC-7 Robustness and cost**: one member's failure (stat reader throws) is logged once per `Run` and does not stop other pairs or the record sweep; a member entry with an invalid entity is skipped (defensive: `GetPartyMembersWithStatus` takes entities from the party slots, so a member cannot have one — no test). `Run` allocates nothing after the first tick at full capacity (GC test). The constructor validates its arguments (`ArgumentNullException`, capacity < 1 → `ArgumentOutOfRangeException`) and rejects an invalid config (`ArgumentException`).

---

## Readiness Decisions (2026-11-19)

- **D1 MP threshold** — the GDD names an HP threshold only; MP uses the same fraction, otherwise an MP-only change would never be shown. A change of either maximum always sends.
- **D2 First send** — `PartyStateUpdate` carries no HP, so the first eligible tick of a pair always sends, even at full health (join, reconnect, back from OutOfZone / Ghost, Dead → Online recipient keeps its record since Dead is eligible).
- **D3 Edges** — "reach full" and "cross 0" always send, so a bar never sticks at 96 % or shows a dead member at 3 %.
- **D4 Placement** — `PartyHealthStreamStep` is not an `IZoneOutboundStep` (`ZoneOutboundSteps` is the fixed writer flush). Story 011 calls `Run` after the party `Poll` and state broadcast `Flush`, before the zone outbound step, so statuses are current and the R-U step flushes the updates the same tick.
- **D5 Disbanding** — no health stream for a `Disbanding` party (matches `PartyBroadcastStep`).
- **D6 Units** — values are ints (`FloorToInt`, CR-NET-7.2); the threshold uses the current max.
- **D7 Allocation** — pair records pre-sized; party ids copied into a reusable buffer (`CopyRealPartyIds`, grow-on-demand like `PartyBroadcastStep`); members read with `GetPartyMembersWithStatus` into a `stackalloc` span; client resolved via `PartyRegistry.TryGetEntity` → `ZoneSlotPool.TryGetPlayer` → `ClientMessageOutbox.TryGetSequenceCounter` (as `OutboxPartyStateMessageSink`).
- **D8** — `RelevanceFilter.BuildHealthUpdateSubMessages` is not used (no production caller, allocates per call); `EntityHealthUpdate` still has no production producer (not this story).

---

## Implementation Notes

- New in `src/ServerLogic/PartySystem/`: `PartyHealthStreamStep` (constructor: `PartyRegistry`, `IZoneEntityStatReader`, `ZoneSlotPool`, `ClientMessageOutbox`, `IRUBatchCollector`, `PartySystemConfigValues`, `int zoneCapacity`). If the pool / outbox pair is awkward to build in tests, put the client lookup behind a small seam (`IPartyClientResolver`) with the production adapter doing the three-step lookup.
- Pair record key: (recipient `CharacterID`, member `CharacterID`); a struct record with the four ints plus a "seen this tick" stamp (`currentTick`) for the sweep. Sweep without allocation (e.g. parallel arrays + index map, or a dictionary with a pre-sized removal buffer).
- Derived status comes from `GetPartyMembersWithStatus` (Ghost > OutOfZone > Dead > Online, Story 009).
- No wire change: 0x0305 and its codec exist (`BatchSubMessageCodec.WritePartyMemberHealthUpdateBody`); routing entry already `ServerToParty`.

---

## Out of Scope

- `PartyStateUpdate` (Story 008)
- Composition wiring and phase position (Story 011)
- `EntityHealthUpdate` production (self / target slots) — Networking / HUD work
- Client party panel (presentation)

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-19.*

- **AC-1**: party A Online, B Dead, C Ghost, D OutOfZone → A↔B only (2 updates on first tick); nothing about or to C / D. Solo player → nothing. Disbanding party → nothing. Recipient with no pool slot → skipped, no log.
- **AC-2**: values match the fake stat reader (floats 99.7 → 99); B Dead with stat HP 12 → sent HP 0; EntityID is the member's.
- **AC-3**: MaxHP 100 / MaxMP 50: HP −4 → none; HP −5 → sent; MP −2 → none; MP −3 (≥ 2.5) → sent; MaxHP 100 → 110 at same HP → sent; HP 96 → 100 with MP full → sent (rule 4); HP 3 → 0 → sent (rule 5); 0 → 2 → sent; unchanged values → none; f = 0 → any change sends, equal values do not.
- **AC-4**: full and recorded full → none over 10 ticks; drop to 90 → sent 90; back to 100 → one update 100, then none.
- **AC-5**: first tick at full → sent (rule 1). C goes Ghost, back Online → sent afresh on return. Member leaves and rejoins → sent afresh. Records for a disbanded party are dropped (count back to 0). Capacity 1 pair with 2 eligible pairs → one warning per Run, unremembered pair sent every tick.
- **AC-6**: through a real `RUBatchOutboundStep` + recording seam: batch contains 0x0305 sub-messages with the decoded values; with an `EntityHealthUpdate` also enqueued, 0x0305 comes after it.
- **AC-7**: stat reader throws for B → A's update about C still sent, one error; GC: 4-member party, 20 ticks of changing HP, `GC.GetAllocatedBytesForCurrentThread` delta 0 after warm-up; null args / capacity 0 / invalid config throw.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_HealthStream_tests.cs` — must exist and pass

**Status**: [x] Created and passing (2026-11-19)

---

## Dependencies

- Depends on: Stories 004, 008, 009 (derived status)
- Unlocks: Story 011

---

## Completion Notes

**Completed**: 2026-11-19
**Criteria**: 7/7 passing (all automated)
**Deviations / implementation decisions (advisory)**:
- A pair is "considered" (stamped) only when the recipient has a client and the member's stats were read; a missing client, a throwing resolver or a throwing stat read drops that pair's record, so the next eligible tick sends afresh. One error per `Run` for any throw.
- Wire values clamped: a NaN or ≤ 0 HP / MP is sent as 0, negative maxima as 0 (not in the GDD; keeps garbage off the wire).
- Public read-only `RecordCount` / `RecordCapacity` for tests (not in the spec).
- Client lookup behind a new seam `IPartyClientResolver` with production `PoolPartyClientResolver` (registry → pool → outbox, as `OutboxPartyStateMessageSink`).
- Records: pre-sized `Dictionary<ulong,int>` (recipient << 32 | member) over parallel arrays, swap-remove sweep in `finally`; threshold compare uses an explicit float cast (Mono kept wider precision and missed exactly 5 %).
- AC-7 "unknown entity" is a defensive guard (member entities come from party slots), not tested.
**Test Evidence**: Logic — `tests/EditMode/PartySystem/PartySystem_HealthStream_tests.cs` (55 cases); PartySystem 492/492; full EditMode `-testCategory "!Database"` 8586/8586 (2026-11-19)
**Code Review**: Complete (`/code-review` 2026-11-19: R1–R3 + all suggestions applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — Lean mode
**Story 011 hand-off**: build `PartyHealthStreamStep(registry, ZoneEntityStateSource, new PoolPartyClientResolver(registry, pool, outbox), ruBatch, config, zone player capacity)`; call `Run(tick)` after the party `Poll` and state broadcast `Flush`, before `ZoneOutboundSteps`.
