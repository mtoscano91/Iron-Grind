# Story 025: Message Criticality/Channel Routing Table & Unclassified-Message Fallback

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-message-criticality.md` + `design/gdd/networking-channel-contract.md`
**Requirement**: `TR-net-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 2 — the three CR-NET-3 channel types map to NGO `NetworkDelivery` values; this story implements the message-type→channel lookup that decides which mapping applies to any given `MessageTypeID`.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: The concrete `NetworkDelivery` enum binding is deferred to implementation-time engine verification (same caveat as Story 006); this story's routing table is transport-agnostic.

**Control Manifest Rules (Foundation layer)**:
- Required: every message type dispatched must have exactly one row in the criticality/routing table before its schema is accepted — source: MCR-2
- Required: unclassified message types route to R-U as the safe default and log an anomaly (not silently dropped) — source: EC-MCR-1
- Required: higher-guarantee channel wins when a message serves multiple pillars with conflicting requirements (R-OD > R-U > U-U), except explicitly-documented self-correcting exceptions — source: MCR-3

---

## Acceptance Criteria

*From `design/gdd/networking-message-criticality.md` and `networking-channel-contract.md`, scoped to this story:*

- [ ] **AC-MCR-03** [BLOCKING] (Logic): Given a `MessageTypeID` with no MCR-2 row, when the dispatcher attempts to register a handler, then in debug builds registration raises a `PendingSchemaDispatch` fatal error at startup; in release builds the message routes to R-U and an `UnclassifiedMessageType` anomaly is logged. No crash occurs in either configuration.
- [ ] **AC-MCR-04** [BLOCKING] (CI): Given the set of `MessageTypeID` values defined in the wire protocol and the set of MCR-2 rows, when a CI check compares both sets, then every defined `MessageTypeID` appears in exactly one MCR-2 row (or is explicitly `*schema pending*`); any mismatch fails the CI gate.
- [ ] **AC-MCR-06** [BLOCKING] (Logic): Given any message tagged with multiple pillars, when the MCR-3 resolution rule applies, then the Channel column reflects the highest-guarantee channel among tagged pillars, except for the three explicitly-documented MCR-3 exceptions (`GoldSyncEvent`, `LootBidUpdate`, `PartyMemberHealthUpdate`).
- [ ] **AC-CCR-01** [BLOCKING] (CI): Given the full codebase and docs, when a CI text search runs for `"server SessionHandshake"`, then no match is found — all server→client zone-entry references use `SessionReady`/`ZoneStateSnapshot` (CCR-2 disambiguation).
- [ ] **AC-CCR-02** [BLOCKING] (Logic): Given a connection where `HeartbeatMessage` and `RttProbeEcho` are the only two messages sent, when the server inspects `SequenceNumber` envelope fields, then `RttProbeEcho.SequenceNumber = HeartbeatMessage.SequenceNumber + 1` (shared per-connection counter, CCR-1 — not per-message-type counters).
- [ ] **AC-CCR-09** [BLOCKING] (CI): Given the MCR-2 and CCR-3 row sets, when compared, then every non-pending MCR-2 message has exactly one CCR-3 routing entry (direction, channel, context); any MCR-2 message with no CCR-3 entry fails the CI gate.

---

## Implementation Notes

*Derived from MCR-1/2/3/5, CCR-1/3/4:*

- Implement the MCR-2/CCR-3 tables as a single authoritative data structure (a static registry keyed by `MessageTypeID` — Config/Data in nature, even though the story is typed Logic for its fallback/CI-check behavior), containing: pillar tag(s), delivery guarantee, channel (R-OD/R-U/U-U), direction (C→S/S→C/S→ALL/S→RELEVANT/S→PARTY), delivery context (P1/RU-B/CC-B/POS-B). This is the single source of truth every message-sending call site consults — do not hardcode channel choices per message elsewhere in the codebase.
- **MCR-3 resolution**: default rule is "highest guarantee wins" (R-OD > R-U > U-U) when a message serves multiple pillars with conflicting requirements. Three explicit exceptions where self-correcting R-U data stays R-U despite a Pillar-1 tag, because the ultimate economic outcome is separately guaranteed by another R-OD message: `GoldSyncEvent` (guaranteed via `GoldSyncEvent` forced-delivery + no economic authority in the R-U copy itself — the actual mutation already happened server-side), `LootBidUpdate` (guaranteed via `AuctionResolved`), `PartyMemberHealthUpdate` (guaranteed via `GhostPromotionEvent` for the death-transition edge, HP itself self-corrects).
- **EC-MCR-1 unclassified fallback**: debug builds fail loudly at startup registration (`PendingSchemaDispatch` fatal — catches the gap during development); release builds degrade gracefully (route to R-U, log `UnclassifiedMessageType` with the `MessageTypeID`) rather than crash or silently drop.
- **CCR-1 shared counter**: `SequenceNumber` is one counter per connection, shared across ALL message types from that endpoint — never a per-message-type counter. This reuses Story 003's envelope and Story 005's stale-discard helpers; this story's contribution is proving the *shared* (not per-type) semantics.
- **CCR-2 naming disambiguation**: enforce via a CI grep check that no code or doc uses "server SessionHandshake" — the correct concepts are `SessionHandshake` (C→S only), `SessionReady` (S→C), and `ZoneStateSnapshot` (reassembled from fragments, not itself a wire message type).
- AC-MCR-04/CCR-09's CI checks can share one script: compare the full `MessageTypeID` enum/registry against both the MCR-2-equivalent and CCR-3-equivalent sections of this story's unified registry (since this story merges both tables into one structure, these two ACs become one consistency check in practice — implement once, satisfy both).

---

## Out of Scope

*Handled by neighbouring stories:*

- `GoldSyncEvent`'s forced-delivery mechanism itself — Story 026
- `SelfDamageEvent`/`DamageEvent` delivery exclusivity — Story 027
- Every individual downstream system's specific message rows — added incrementally by each system's own future epic, following this story's registry pattern (a new message requires a row here before its schema is accepted, per MCR-2's own rule)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/MessageRouting_CriticalityChannelTable_tests.cs`

- **AC-MCR-03**: Given an unregistered `MessageTypeID`, then debug builds fatal-error at registration; release builds route to R-U with a logged anomaly.
- **AC-MCR-04**/**AC-CCR-09**: Given the full registry, then a completeness-check script finds zero unmapped `MessageTypeID`s.
- **AC-MCR-06**: Given a multi-pillar message without a documented exception, then its channel equals the max-guarantee channel among its pillars.
- **AC-CCR-01**: Given a text search for "server SessionHandshake", then zero matches.
- **AC-CCR-02**: Given a Heartbeat+RttProbeEcho-only connection, then the SequenceNumbers are sequential (shared counter proven).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/MessageRouting_CriticalityChannelTable_tests.cs` — must exist and pass; CI scripts for AC-MCR-04/CCR-01/CCR-09 added to `.github/workflows/tests.yml`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (envelope), Story 005 (stale-discard/shared counter)
- Unlocks: Story 026, Story 027, Story 028 (all reference this registry for their own messages' routing)
