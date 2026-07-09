# Story 007: R-U/U-U Batch Framing, Buffer Pooling & Overflow Drop Policy

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md`
**Requirement**: `TR-net-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 4 — project-owned batch framing sits inside the message body NGO transports; NGO does not see or manage sub-message structure.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: Must NOT use `ArrayPool<T>.Shared` for batch buffers — GC spike risk under IL2CPP (CR-NET-7.7). Use 3 pre-allocated fixed-size buffers per client connection instead.

**Control Manifest Rules (Foundation layer)**:
- Required: max message body 512 bytes, max total message 522 bytes — source: CR-NET-7.6
- Required: Path 2a (R-U) one packet/tick/client; Path 2b (U-U) two separate packets/tick/client (CycleBroadcast + Position) — source: CR-NET-7.7
- Forbidden: `ArrayPool<T>.Shared` for batch buffers — source: CR-NET-7.7

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md`, scoped to this story:*

- [ ] **AC-NC-19** [BLOCKING]: Given three sequential gold mutations on the same character (add 100g, spend 50g, add 200g), when three `GoldSyncEvent` sub-messages are captured from the R-U batch, then each `NewBalance` field equals the absolute post-mutation balance (100g, 50g, 250g) — never deltas.
- [ ] **AC-NC-21** [BLOCKING] (Integration): Given a deterministic 50-client fixture in active combat for 200 consecutive ticks with 10 `DamageEvent` sub-messages/tick zone-wide, when total outbound bytes per client are measured, then no client's total exceeds 200×1,500 bytes across the window.
- [ ] **AC-NC-33** [BLOCKING] (Integration, wire-protocol's numbering — distinct from `networking-ghost-session.md`'s unrelated ghost ACs sharing similarly-shaped IDs): Given a load fixture at Scenario C density (n=50, 10 DamageEvent/tick) for 100 ticks, when every R-U/CycleBroadcast/Position packet body is measured, then none exceeds `MAX_MESSAGE_BODY_BYTES` (512 bytes).
- [ ] **AC-BUF-1** [BLOCKING]: Given a zone at `MAX_PLAYERS_PER_ZONE` capacity, when buffer pool allocation is attempted for a new connection beyond the pool's pre-allocated capacity (`MAX_PLAYERS_PER_ZONE × 3`), then the connection is rejected at the transport layer before session establishment and a `BufferPoolExhausted` critical anomaly is logged.

---

## Implementation Notes

*Derived from CR-NET-7.6/7.7:*

- Path 2a (R-U batch): one reliable packet/tick/client — 12-byte header (10B envelope + 2B sub-message count) + repeated `[uint16 length][MessageTypeID][payload]`. Canonical serialization order (highest priority first): damage events → EntityHealthUpdate → PartyMemberHealthUpdate → SelfPositionUpdate → SkillCastResult → GoldSyncEvent → SkillCooldownUpdate → LootBidUpdate → ConnectionQualityUpdate.
- Overflow drop order (lowest priority first, when the 512-byte cap is hit): LootBidUpdate → SkillCooldownUpdate → GoldSyncEvent (until `GOLD_MAX_CONSECUTIVE_DROP` forces R-OD delivery — see Story 026) → SkillCastResult → SelfPositionUpdate → EntityHealthUpdate → PartyMemberHealthUpdate → ConnectionQualityUpdate.
- Path 2b: two separate U-U packets — CycleBroadcast (`0x0103`, never dropped, drives Rhythm Mastery) and Position (`0x0102`, sorted ascending EntityID, drops highest-ID-first on overflow).
- DamageEvent intra-class overflow: holds at most ⌊(512−12)/18⌋ entries, drops oldest-first, logs `DamageEventIntraclassOverflow` (best-effort/cosmetic channel — acceptable).
- Buffer allocation: exactly 3 pre-allocated fixed-size buffers per client connection (R-U, CycleBroadcast, Position), each `MAX_MESSAGE_BODY_BYTES+12` bytes, allocated at zone creation, released at zone close. Pool size = `MAX_PLAYERS_PER_ZONE × 3`. Never `ArrayPool<T>.Shared`.
- `GoldSyncEvent` must always encode the absolute `newBalance`, never a delta — this is a hard invariant checked by AC-NC-19 and reiterated project-wide (ADR-010, Currency System stories).

---

## Out of Scope

*Handled by neighbouring stories:*

- Priority-path (R-OD) queue — Story 006
- Relevance-filtered entity sets that determine *which* entities' HP appears in the batch — Story 028
- The specific overflow-drop escalation to forced R-OD delivery for gold — Story 026

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/WireProtocol_BatchFraming_tests.cs`

- **AC-NC-19**: Given 3 sequential mutations, then 3 captured `GoldSyncEvent`s carry absolute balances 100/50/250, not deltas.
- **AC-NC-21**: Given the 50-client/200-tick fixture, then no client exceeds 300,000 total bytes.
- **AC-NC-33**: Given the Scenario C 100-tick fixture, then no packet body exceeds 512 bytes.
- **AC-BUF-1**: Given a zone at buffer-pool capacity, when one more connection is attempted, then it's rejected pre-session with a logged `BufferPoolExhausted` anomaly.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Networking/WireProtocol_BatchFraming_tests.cs` OR documented playtest evidence in `production/qa/evidence/` (load-fixture ACs require a running tick loop; unit-level sub-message ordering/overflow-drop logic may additionally have EditMode coverage)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (envelope/primitives), Story 006 (priority-path queue this batch sits alongside)
- Unlocks: Story 026 (GoldSyncEvent forced delivery), Story 028 (relevance-filtered HP delivery into this batch)
