# Story 016: `ServerBroadcast_Enhancement9` Delivery

> **Epic**: Enhancement System
> **Status**: Not yet Ready — unblocked 2026-10-16: Networking Core Story 040 (the ADR-015 Migration Plan step 1 relay) is Complete. Run `/story-readiness` (criteria, estimate, risk and manifest rules still to set). Before ADR-015, ADR-002 (one zone server process per active zone, line 189) left no way for a message to reach the clients of other zone processes.
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: not estimated — set at `/story-readiness` once the relay ADR is Accepted

> **Created 2026-10-16 by splitting Story 015 (user decision).** Story 015 keeps everything the owning client receives because its attempt was committed; this story is the +9 broadcast every online player receives.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-14 (the +9 server broadcast), CR-ENH-15 step 8 (broadcast only after the commit), UI-ENH-3 (`ServerBroadcast_Enhancement9`), EC-ENH-8 (a delivery failure does not affect the outcome), AC-ENH-18 (delivery half; its server-side half is in Story 007). `design/gdd/networking-wire-protocol.md` — `ServerBroadcast_Enhancement9` (R-OD, server → all connected clients; 4–52 byte body), AC-NC-47 (the broadcast is not cap-exempt).
**Requirement**: `TR-enh-010` — "Server broadcast at +9: `ServerBroadcast_Enhancement9 { playerName, itemName }` to all online players, once per successful +9 transition, only after the commit (CR-ENH-14, CR-ENH-15 step 8)"

**ADR Governing Implementation**:
- **ADR-002: NavMesh Service Execution Contract** — one zone process per active zone (the reason this story is blocked).
- **ADR-007: Hosting Backend** — every zone process on one VPS, PostgreSQL at localhost from each; input to the relay decision.
- **ADR-011** — the broadcast is raised from the commit-success delivery, on the tick, after the write.
- **ADR-004 (NGO)** — transport and envelope.
- **ADR-015: Cross-Zone Message Relay** (Accepted 2026-10-16) — the story calls `IServerWideBroadcaster.Broadcast` from Story 015's commit-success delivery; the broadcaster enqueues for this zone's clients (`IZoneClientBroadcastSink`, not cap-exempt) and publishes to the other zones; EditMode tests use `InMemoryCrossZoneRelayBus` (two zones, no database). Clients not yet sent `SessionReady` also receive the broadcast (ADR-015 Decision 1, accepted). The relay itself is built by Networking Core stories (ADR-015 Migration Plan steps 1–2), not here.

**Engine**: Unity 6.3 LTS | **Risk**: to be set at `/story-readiness` (depends on the relay ADR; ADR-004's NGO risk is HIGH)

**Control Manifest Rules**: to be listed at `/story-readiness`. Known to apply: never use class-typed event args — ADR-010; never use lambda captures for persistent event subscriptions — ADR-010; no `await` in tick-driven code — ADR-011.

---

## What is blocking

1. ADR-015 Accepted — done 2026-10-16. Remaining: the relay of its Migration Plan step 1 (the game-side relay, EditMode-testable, no database) — Networking Core Story 040 (`story-040-cross-zone-relay-game-side.md`) — **Complete 2026-10-16**. Step 2 is Networking Core Story 041. Step 2 (the Npgsql transport) is not needed for this story's tests.
2. ~~**Story 015**~~ (Complete 2026-10-16) provides the commit-success delivery point this story hooks into.

---

## Acceptance Criteria

*To be re-checked at `/story-readiness` once the relay ADR is Accepted.*

- [ ] **AC-ENH-18 (broadcast delivery)**: on a committed +8 → +9 success every connected client, in every zone, receives exactly one `ServerBroadcast_Enhancement9 { playerName: "TestPlayer", itemName: "Dark Steel Sword" }`, the owner's new connection included if it reconnected during the commit; none is sent for +7 → +8 or +9 → +10.
- [ ] **Only after the commit (CR-ENH-15 step 8)**: with the commit held open no client receives the broadcast; a commit that returns a non-Success code sends none.
- [ ] **Delivery failure isolated (EC-ENH-8)**: a send failure to some clients (or to a whole zone, once the relay exists) does not change the outcome, and the other clients still receive the broadcast.
- [ ] **Broadcast is not cap-exempt (AC-NC-47)**: client A's Path 1 queue holds exactly 8 non-exempt messages and a +8 → +9 commits before the flush → that tick's capture holds the 8 messages and no broadcast; the broadcast is in the next tick's capture.
- [ ] **Names**: `playerName` is the character's name and `itemName` the item's `ItemDefinition.DisplayName`; strings longer than 24 UTF-8 bytes are cut by the codec (Story 010).
- [ ] **Observer callbacks (moved from Story 015 on 2026-10-16)**: `INetworkTestObserver.OnServerEnhancementOutcomeSerialized` and `OnClientEnhancementOutcomeReceived` drop the provisional `itemId` and carry the fields of the messages they observe; `networking-test-harness.md` gives the new signatures; the existing harness tests pass with them. The fields are named at this story's `/story-readiness`.

---

## Implementation Notes

- **Broadcast scope (moved from Story 015; from the Story 010 code review, 2026-10-09).** The routing row of `ServerBroadcast_Enhancement9` has the direction `MessageDirection.ServerToAllZoneClients`, because the enum mirrors the five CCR-3 direction codes and the channel contract codes this message `S→ALL (all connected clients, every zone)`. The sender must enqueue it for every connected client in every zone (AC-ENH-18), not only for the clients of the sender's zone; do not derive the recipient set from the direction value alone.
- `ClientMessageOutbox` is per zone (`IZoneOutboundStep`): delivery inside a zone goes through it; delivery to other zones goes through the relay the ADR defines.
- **Names (moved from Story 015).** The +9 trigger of Story 007 (`EnhancementBroadcastEventArgs`) carries ids; resolve the names here (see Story 007's note on name lookup). The character name is held in the persistence session entry; the item name is `ItemDefinition.DisplayName`.
- The broadcast is raised from Story 015's commit-success delivery, never from `BeginAttempt`.

---

## Out of Scope

- Story 015: acknowledgment, result, held inventory updates, reconnect full sync, `LastEnhancementRequestID`
- Story 007: the +9 trigger event itself
- The relay infrastructure beyond what the ADR assigns to this story
- Enhancement UI, VFX and Audio epics: how the broadcast is presented

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_Broadcast_integration_tests.cs` (to be created).

- **AC-ENH-18** — three connected clients in two zones → each receives one broadcast with the two names; +7 → +8 → none; one client's send fails → the outcome stands and the others receive it.
- **Commit held / failed** — no broadcast while held; none after a non-Success commit.
- **AC-NC-47** — as stated in the criterion.
- **Long names** — a 30-byte item name arrives cut to 24 UTF-8 bytes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_Broadcast_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-015 (Accepted 2026-10-16); Networking Core Story 040 (Complete 2026-10-16); Story 015 (Complete); Story 007 (Complete); Story 010 (Complete)
- Unlocks: Enhancement UI epic (broadcast presentation)
