# Story 016: `ServerBroadcast_Enhancement9` Delivery

> **Epic**: Enhancement System
> **Status**: Ready — `/story-readiness` 2026-10-16: NEEDS WORK, closed the same day by the user decisions below. Unblocked 2026-10-16 when Networking Core Story 040 (the ADR-015 Migration Plan step 1 relay) was Complete.
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-16
> **Estimate**: 8 hours

> **Created 2026-10-16 by splitting Story 015 (user decision).** Story 015 keeps everything the owning client receives because its attempt was committed; this story is the +9 broadcast every online player receives.

> **User decisions 2026-10-16 (`/story-readiness`).**
> 1. **Trigger: subscribe to the existing event.** A new `Enhancement9BroadcastSender` subscribes, with a method group, to `EnhancementService.OnEnhancementBroadcastLevelReached` (Story 007), which `CompleteAttempt` raises only after a successful write. Story 015's `EnhancementAttemptStarter` is not edited.
> 2. **Names: a new lookup; a missing name skips the broadcast.** `ICharacterNameLookup.TryGetCharacterName(CharacterID, out string)`, implemented by `CharacterPersistenceService` from its session entry. No character name, or no item definition, → no broadcast, one warning logged; the outcome is unaffected (EC-ENH-8).
> 3. **The observer-callback criterion moves out** of this story (it concerns `EnhancementAttemptResult`, not the broadcast, and needs a GDD edit to `networking-test-harness.md`). It is a follow-up item in the Enhancement `EPIC.md`.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-14 (the +9 server broadcast to all online players), CR-ENH-15 step 8 (broadcast only after the commit), UI-ENH-3 (`ServerBroadcast_Enhancement9 { playerName, itemName }`), EC-ENH-8 (a delivery failure does not affect the outcome), AC-ENH-18 (delivery half; the server-side trigger is Story 007). `design/gdd/networking-wire-protocol.md` — `ServerBroadcast_Enhancement9` (R-OD, server → all connected clients; body 4–52 bytes, two strings of at most 24 UTF-8 bytes), AC-NC-47 (not cap-exempt). `design/gdd/networking-channel-contract.md` — its row (`S→ALL`, every zone; ADR-015 is the cross-zone path).
**Requirement**: `TR-enh-010` — "Server broadcast at +9: `ServerBroadcast_Enhancement9 { playerName, itemName }` to all online players, once per successful +9 transition, only after the commit (CR-ENH-14, CR-ENH-15 step 8)"
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**:
- **ADR-015: Cross-Zone Message Relay** — Decision 1: the sender calls `IServerWideBroadcaster.Broadcast` on the tick; local delivery through the zone outbox (not cap-exempt), then publish; `Broadcast` never throws. Decision 6: called only after the commit. Built by Networking Core Story 040 (Complete).
- **ADR-011** — the event is raised from `CompleteAttempt`, which runs in the commit-success path on the tick, after the write.
- **ADR-010** — a direct subscription to a typed event; `readonly struct` args; no lambda capture for a persistent subscription.
- **ADR-004** — envelope and channel (the outbox of Networking Core Story 039).
- **ADR-012** — the sender and the lookup are `IronGrind.ServerLogic`.

**ADR Decision Summary**: on a committed +8 → +9 success, `EnhancementService` raises `OnEnhancementBroadcastLevelReached`; the sender resolves the two names, encodes the body, and calls `IServerWideBroadcaster.Broadcast`, which delivers to this zone's clients and relays to every other zone.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — no engine API; tests run on Story 040's `InMemoryCrossZoneRelayBus` and Story 039's outbox with a recording send seam. The Npgsql transport (Networking Core Story 041) is not needed.
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (v2026-10-16)**:
- Required (Feature): a feature that announces to every player of every zone calls `IServerWideBroadcaster.Broadcast` from the commit-success path of its irreversible outcome (ADR-011), never from `BeginAttempt`; its type must be on `CrossZoneRelayAllowlist` — ADR-015, ADR-011
- Required (Foundation): the recipient set is every client in the zone's outbox; it is never derived from `MessageDirection` alone — ADR-015
- Forbidden: lambda captures for persistent event subscriptions; class-typed event args — ADR-010
- Forbidden: `await` in tick-driven code — ADR-011
- Presentation rule, **not this story**: the client handler of `ServerBroadcast_Enhancement9` tolerates arrival before `SessionReady` — ADR-015 (its own client story)

---

## Acceptance Criteria

*Every test runs in EditMode with two zones, A and B, each a `RelayTestZone` (Story 040's doubles: outbox with a recording send seam, `ServerWideBroadcaster`, inbox, pipeline) connected through one `InMemoryCrossZoneRelayBus`. The enhancement attempt runs in zone A through the real `EnhancementService` and `EnhancementCommitOrchestrator` with a controllable persistence double (Story 015's integration doubles). "Receives" means sent by that zone's outbox flush.*

- [ ] **AC-ENH-18 (delivery)**: zone A has clients 1 (the owner) and 2, zone B has client 3. A committed +8 → +9 success of `"TestPlayer"`'s `"Dark Steel Sword"` → clients 1 and 2 each receive exactly one `ServerBroadcast_Enhancement9 { playerName: "TestPlayer", itemName: "Dark Steel Sword" }` in A's tick of the commit, and client 3 exactly one in B's next tick; nothing more over 3 further ticks.
- [ ] **Owner reconnected during the commit**: the owner's client is removed from A's outbox and added back under a new id while the write is held; on commit the new id receives the broadcast once and the old id receives nothing.
- [ ] **Only +9**: a committed +7 → +8 success, a +9 → +10 success, and a destruction each send no broadcast to any client.
- [ ] **Only after the commit (CR-ENH-15 step 8)**: while the write is held, no client in either zone receives the broadcast; a write that completes with a non-Success code sends none, ever.
- [ ] **Delivery failure isolated (EC-ENH-8)**: (a) a send seam that throws for client 2 → client 1 and client 3 still receive the broadcast and the owner still receives its `EnhancementAttemptResult`; (b) a publisher that throws → A's clients still receive it, the outcome and the result message are unchanged; neither throws out of the tick.
- [ ] **Not cap-exempt (AC-NC-47)**: client 2's priority queue holds exactly 8 non-exempt messages when the +9 commits → A's flush of that tick sends client 2 the 8 and no broadcast; the broadcast is in client 2's next flush. The owner's cap-exempt `EnhancementAttemptResult` is unaffected.
- [ ] **Names**: `playerName` is `ICharacterNameLookup.TryGetCharacterName(charId)` and `itemName` is `IItemDatabase.TryGetItem(itemId)`'s `DisplayName`. A 30-byte item name arrives cut to 24 UTF-8 bytes by `ServerBroadcast_Enhancement9Codec` (Story 010); a name of 24 bytes arrives whole.
- [ ] **Missing name or item**: the lookup returns false for the character, or the item database has no definition → no broadcast to any client, one warning naming the character and item ids, and the outcome and the owner's result message are unchanged.
- [ ] **Subscription**: `Enhancement9BroadcastSender` subscribes to `OnEnhancementBroadcastLevelReached` with a method group in its constructor and unsubscribes in `Dispose`; after `Dispose` a +9 commit sends no broadcast. A sender that throws inside its handler does not change the outcome (the service's `Raise` contains it, EC-ENH-8) — asserted with a throwing broadcaster.
- [ ] **Name lookup**: `CharacterPersistenceService.TryGetCharacterName` returns the loaded character's name while its session entry exists and false after the session is released; the existing `CharacterPersistence` tests pass unedited.
- [ ] **Existing tests**: the tests of `Enhancement_ResultDelivery_integration_tests.cs`, `EnhancementSystem_OutcomeEvents_tests.cs` and Story 040's `CrossZoneRelay_tests.cs` pass unedited.

---

## Implementation Notes

- **The sender.** `Enhancement9BroadcastSender : IDisposable` in `src/ServerLogic/EnhancementSystem/`. Constructor: `(EnhancementService service, IServerWideBroadcaster broadcaster, ICharacterNameLookup names, IItemDatabase items)`; subscribes `service.OnEnhancementBroadcastLevelReached += OnLevelReached` (a method, not a lambda). `OnLevelReached(EnhancementBroadcastEventArgs e)`: resolve both names (on failure log one warning and return), encode with `ServerBroadcast_Enhancement9Codec.WriteBody` into a buffer of `ServerBroadcast_Enhancement9.MaxBodySize` (a field, or `stackalloc` — no allocation per broadcast beyond what the name strings already are), call `broadcaster.Broadcast(ServerBroadcast_Enhancement9.MessageTypeId, body)`. `Broadcast` never throws; the service's `Raise` contains anything else (EC-ENH-8).
- **Why this point is after the commit.** `CompleteAttempt` raises the event; the orchestrator calls `CompleteAttempt` only from the commit-success completion callback on the tick (ADR-011, Story 015), before `DeliverResult`. So the owner receives the broadcast, in the same flush, before or with its own result: the result is cap-exempt and goes first; the broadcast follows the cap. `BeginAttempt` never raises it.
- **Name lookup.** `ICharacterNameLookup` in `src/ServerLogic/CharacterPersistence/` (provider-owned; both sides are `ServerLogic`), one method `bool TryGetCharacterName(CharacterID id, out string name)`. `CharacterPersistenceService` implements it from `_sessions[id].CharacterName` (tick thread only, like the rest of the service). Return false for an unknown id or an empty name.
- **Item name.** `ItemID` in the event args is the item definition id the item database knows; `IItemDatabase.TryGetItem(ItemID, out ItemDefinition?)` → `DisplayName`. A false return or a null definition is "missing".
- **Recipient set.** Do not filter by `MessageDirection` or by session state: `Broadcast` reaches every client in each zone's outbox, clients still loading included (ADR-015 Decision 1, accepted).
- **Composition.** No composition root exists yet; tests construct the sender. The composition-root story wires one sender per zone.

**Performance**: one broadcast per +9 success (a few per hour server-wide); per broadcast, two lookups, one encode of ≤ 52 bytes, then Story 040's cost. No per-tick work.

---

## Out of Scope

- Story 015: acknowledgment, result, held inventory updates, reconnect full sync, `LastEnhancementRequestID`
- Story 007: the +9 trigger event itself (unchanged)
- Networking Core Stories 040 (Complete) and 041: the relay and its Npgsql transport
- The client handler for `ServerBroadcast_Enhancement9` and its presentation (Enhancement UI, VFX and Audio epics), including tolerating arrival before `SessionReady`
- The observer-callback signatures (`INetworkTestObserver.OnServerEnhancementOutcomeSerialized`, `OnClientEnhancementOutcomeReceived`; `networking-test-harness.md`) — follow-up item in the Enhancement `EPIC.md` (user decision 3)
- The server composition root

---

## QA Test Cases

**Files** — to be created: `src/ServerLogic/EnhancementSystem/Enhancement9BroadcastSender.cs`, `src/ServerLogic/CharacterPersistence/ICharacterNameLookup.cs`, `tests/EditMode/Integration/EnhancementSystem/Enhancement_Broadcast_integration_tests.cs`. To be edited: `src/ServerLogic/CharacterPersistence/CharacterPersistenceService.cs` (implements the lookup); new tests for the lookup in the existing CharacterPersistence test file or the new file. Doubles from `EnhancementTestDoubles.cs` and `CrossZoneRelayTestDoubles.cs` (extend only by adding).

- **AC-ENH-18** — A {1, 2}, B {3}: +8 → +9 commit → 1 and 2 once in A's tick, 3 once in B's next tick; 3 more ticks → nothing else; body decodes to the two names.
- **Reconnect** — owner removed and re-added as a new id during the held write → new id once, old id nothing.
- **Only +9** — +7 → +8, +9 → +10, a destruction → no broadcast anywhere.
- **Held / failed** — held write → nothing in either zone; release with a non-Success code → nothing, then 3 more ticks → nothing.
- **Seam throws for client 2** — 1 and 3 receive; owner's result still sent.
- **Publisher throws** — A's clients receive; B's do not; outcome and result unchanged.
- **AC-NC-47** — 8 queued for client 2 → broadcast in client 2's next flush; owner's exempt result in the commit tick.
- **Names** — 24-byte names whole; 30-byte item name cut to 24 bytes.
- **Missing** — no name → no broadcast, one warning; no item definition → the same.
- **Dispose** — after `Dispose`, a +9 commit → nothing.
- **Throwing broadcaster** — outcome and result unchanged; the service logs the contained exception.
- **Lookup** — loaded → the name; released → false; unknown id → false.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_Broadcast_integration_tests.cs` — must exist and pass, with the full EditMode suite.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Networking Core Story 040 (Complete 2026-10-16); Story 015 (Complete); Story 007 (Complete); Story 010 (Complete); ADR-015 (Accepted 2026-10-16)
- Unlocks: Enhancement UI epic (broadcast presentation)
