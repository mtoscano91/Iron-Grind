# Story 010: Party Health Stream

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — Interactions (`PartyMemberHealthUpdate` bandwidth management); `networking-wire-protocol.md` `PartyMemberHealthUpdate` (0x0305, R-U batch, 20-byte body); `networking-relevance-filter.md` EC-RFR-2
**Requirement**: `TR-party-011` (health stream)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Outbound R-U batch content is built in the outbound phase each tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: absolute HP / MP values, never deltas
- Guardrail: ≤ 3 updates per member per tick at `MaxPartySize` 4; zero allocation per tick

---

## Acceptance Criteria

- [ ] **AC-1 Recipients**: each tick, for each real party, every recipient whose status is Online or Dead receives `PartyMemberHealthUpdate` for each other member whose status is Online or Dead; none for or to Ghost / OutOfZone; solo parties send nothing.
- [ ] **AC-2 Values**: HP, MP, MaxHP and MaxMP are absolute current values from Character Stats; a Dead member is sent with HP 0 (EC-RFR-2).
- [ ] **AC-3 Delta threshold**: an update is suppressed when `|HP − lastSentHP| < HpDeltaThresholdFraction × MaxHP` (default 5 %) — and the MP equivalent if the GDD applies it (confirm at readiness); the last-sent value is per (recipient, member).
- [ ] **AC-4 Full suppression**: when HP == MaxHP and MP == MaxMP the update is suppressed entirely — except the first update after the member was last sent below full (so the bar reaches full).
- [ ] **AC-5 Batch**: updates go into the R-U batch through the existing batch step in the documented order, using the existing 0x0305 codec.

---

## Implementation Notes

- Find how other R-U sub-messages are added (`RUBatchOutboundStep`, `RUBatchCategory`); the routing entry for 0x0305 already says `ServerToParty`.
- AC-4's "reach full" case is not in the GDD text; it follows from "absolute values" + suppression (otherwise a bar at 96 % would never show full). Confirm at readiness.

---

## Out of Scope

- `PartyStateUpdate` (Story 008)
- Client party panel (presentation)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: party of 3 (A Online, B Dead, C Ghost) → A and B receive about each other; nobody about C; C receives nothing.
- **AC-2**: values match stats; B HP 0.
- **AC-3**: MaxHP 100, change 4 → suppressed; change 5 → sent.
- **AC-4**: full HP / MP → none; drop to 90 then back to 100 → one update with 100, then none.
- **AC-5**: batch contains the sub-messages after `EntityHealthUpdate`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_HealthStream_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004, 008
- Unlocks: Story 011
