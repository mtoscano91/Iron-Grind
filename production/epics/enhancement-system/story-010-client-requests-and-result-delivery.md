# Story 010: Client Requests — Codecs, Preview, Cancel and Rejections

> **Epic**: Enhancement System
> **Status**: Ready (`/story-readiness` 2026-10-09, after three decisions: generic outbox, the code name `EnhancementAttemptResultMessage`, `npcId` validation stays deferred) — rewritten 2026-10-09 against the TD-046 amendment of `networking-wire-protocol.md` (Approved, lean verify 2026-10-09)
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours

> **Split 2026-10-09 (user decision).** This story was "Client Requests and Result Delivery" and was Blocked on TD-046. The design gate closed 2026-10-09. Everything that needs a commit — the acknowledgment, the result after the commit, the CR-ENH-11 hold, the +9 broadcast delivery, the reconnect cases — moved to **Story 015** (Blocked on Story 011). The inventory sync messages moved to **Inventory Story 012**. This story keeps what needs no commit. The file name is unchanged.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-6 (two-tap irrevocability; `CancelEnhancement`), CR-ENH-15 steps 1 and 2, CR-ENH-16 and CR-ENH-17 (NPC session), UI-ENH-1 (`EnhancementStateUpdate`), UI-ENH-2 (`EnhancementAttemptResult`, rejection half), UI-ENH-4 (requests), EC-ENH-5, AC-ENH-6. `design/gdd/networking-wire-protocol.md` — the Enhancement System messages (amended 2026-10-09, TD-046), `EnhancementResultCode`, the NPC interaction messages, CR-NET-7.2, CR-NET-7.3, AC-NC-40, AC-NC-41 (broadcast half), AC-NC-42 (result-code half), AC-NC-48, AC-NC-49. `design/gdd/networking-session.md` — EC-NET-9 (duplicate `requestId`).
**Requirement**: `TR-enh-009` (request half), `TR-enh-008` (request messages)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**:
- **ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09)** — Decision 1 (the type table: `EnhancementAttemptRequest`, `EnhancementPreviewRequest` and `CancelEnhancement` are not held; `OpenNPCInteraction` and `CloseNPCInteraction` are not held), Decision 3 (one descriptor and one handler per type; the story that adds a client→server type adds its routing row, its row in the table of Decision 1 and its registration together).
- **ADR-012: Server/Client Assembly Boundary (Accepted 2026-10-08)** — message types and codecs a client decodes are shared; handlers and rules are server-only.
- **ADR-004: Networking Library (NGO)** — transport and envelope. No NGO call is written in this story.

**ADR Decision Summary**: every client request reaches its system through the inbound request dispatcher, with one descriptor and one handler per message type; a handler decodes the body with the message's codec and calls its system on the tick thread.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — codecs over spans and dispatcher handlers; no engine API. (ADR-004's NGO risk does not apply: no transport adapter exists yet and none is written here.)
**Engine Notes**: Follow the existing patterns: `SetTarget` / `SetTargetCodec` (message type and codec), `SetTargetRequestHandler` and `NetworkingCoreInboundRegistration` (handler and registration), `WireFixedPointCodec`, `WireEnumCodec` and `WireIdCodec`.

**Control Manifest Rules**:
- Required: NGO message handlers never call game-logic methods directly; for client requests the queue is the dispatcher's inbox — ADR-010, ADR-014
- Required: `EnhancementAttemptRequest` is not held; while the character's gate is closed `EnhancementService` rejects it as a concurrent attempt — ADR-014, ADR-011
- Forbidden: keeping `body` after a handler returns, `await` in a handler, reading `ICharacterMutationGate` from a handler — ADR-014, ADR-011
- Forbidden: class-typed event args — ADR-010

---

## Acceptance Criteria

*Wire names and schemas are those of `networking-wire-protocol.md`. `EnhancementAttemptRequest` is the GDD's `ConfirmEnhancement`. The criteria use the wire name `EnhancementAttemptResult`; in code that message is the type `EnhancementAttemptResultMessage` (see Implementation Notes). "Receives" and "sends" mean an entry enqueued on `IClientMessageOutbox` for the owning client's connection.*

- [ ] **Fixed-size codecs (AC-NC-40, without `InventoryFullSync`)**: each of the seven messages round-trips every field and its encoded body length is exactly: `EnhancementAttemptRequest` 6, `EnhancementRequestReceived` 5, `EnhancementAttemptResult` 6, `CancelEnhancement` 0, `EnhancementPreviewRequest` 2, `EnhancementStateUpdate` 7, `EnhancementPreviewRejected` 3 bytes. `EnhancementStateUpdate` with `P_s = 0.65`, `P_d = 0.35` encodes `pSuccess = 6500`, `pDestruction = 3500`.
- [ ] **Broadcast codec (AC-NC-41, broadcast half)**: `ServerBroadcast_Enhancement9` with (a) two empty strings, (b) two 24-byte ASCII strings, (c) a 24-byte ASCII `playerName` and a 30-byte ASCII `itemName`, (d) a 24-byte ASCII `playerName` and an `itemName` of 22 ASCII bytes followed by two 3-byte characters → body lengths 4, 52, 52 and 50 bytes; in (c) the name decodes as its first 24 bytes; in (d) as its first 22 bytes.
- [ ] **Unknown result code (AC-NC-42, result-code half)**: an `EnhancementAttemptResult` or `EnhancementPreviewRejected` whose `resultCode` byte is 10 or 255 decodes as a rejection with the unknown-code flag set, logs one anomaly, and does not throw.
- [ ] **NPC interaction codecs**: `OpenNPCInteraction` round-trips `npcId` with a 4-byte body; `CloseNPCInteraction`, `NPCInteractionOpened` and `RejectedNotInTownHub` have no body.
- [ ] **NPC session messages**: `OpenNPCInteraction` from a character in the town hub calls Story 006's tracker and the owning client receives exactly one `NPCInteractionOpened`; from a character outside it, exactly one `RejectedNotInTownHub` and no session. `CloseNPCInteraction` clears the session and sends nothing.
- [ ] **Preview (AC-NC-48, UI-ENH-1)**: an `EnhancementPreviewRequest` for a selection that passes the CR-ENH-15 step 2 checks → exactly one `EnhancementStateUpdate` echoing both slot indices, with `currentLevel` and the F-ENH-4 table values for that level (level 4 → 6500 / 3500; level 2 → 8500 / 1500), and zero `EnhancementPreviewRejected`. A selection that fails a check → exactly one `EnhancementPreviewRejected` with that check's `Rejected*` code and zero `EnhancementStateUpdate`. In both cases the bag, the slot locks and the NPC session are unchanged.
- [ ] **Preview during the character's own attempt**: with an attempt pending for the character, a preview is answered with `EnhancementPreviewRejected { RejectedConcurrentAttempt }`; no bag content of the pending attempt is sent.
- [ ] **Cancel (AC-ENH-6, AC-NC-48)**: a `CancelEnhancement`, sent before any request or after an accepted `EnhancementAttemptRequest`, adds no message to the owning client's capture and changes no server state: no slot was locked, the scroll is in the bag, the level is unchanged.
- [ ] **Rejected request**: an `EnhancementAttemptRequest` that fails a CR-ENH-15 step 2 check → exactly one `EnhancementAttemptResult { requestId, resultCode, newLevel = 0 }` with that check's `Rejected*` code and the request's `requestId`; zero `EnhancementRequestReceived`; the attempt-start seam is not called; the bag is unchanged. A slot index out of range is `RejectedItemNotFound` / `RejectedScrollNotFound`.
- [ ] **Duplicate request (AC-NC-49)**: with the character's `LastEnhancementRequestID` equal to `X`, an `EnhancementAttemptRequest` with `requestId = X` and otherwise valid slots → no `EnhancementRequestReceived`, no `EnhancementAttemptResult`, exactly one `DuplicateEnhancementRequest` anomaly, the attempt-start seam is not called, and the bag is unchanged. The check runs before validation.
- [ ] **Valid request is handed on once**: an `EnhancementAttemptRequest` that is not a duplicate and passes the step 2 checks reaches the attempt-start seam exactly once with the sender's `CharacterID`, the `requestId` and the two slot indices; this story sends nothing for it.
- [ ] **Cap exemption flag**: a rejection `EnhancementAttemptResult` is enqueued with `isCapExempt` true (CR-NET-7.7); `EnhancementStateUpdate`, `EnhancementPreviewRejected`, `NPCInteractionOpened` and `RejectedNotInTownHub` are enqueued with `isCapExempt` false.
- [ ] **Registration**: the five client→server types have a routing row and are registered with the dispatcher with `HeldDuringIrreversibleWrite` false and the body bounds of ADR-014 Decision 1 (6, 2, 0, 4, 0); registration succeeds in a development build. Each of the five has its own `RpcTypeTag` member with no rate limit, and the existing test that no `RpcTypeTag` member makes `Evaluate` throw passes with them. The seven server→client types have a `MessageRoutingRegistry` row (R-OD, server→client).
- [ ] **Assembly boundary**: every type this story adds to `IronGrind.Foundation` (the twelve message types, their codecs, and the moved `EnhancementResultCode`) has an entry in the shared allow-list of `AssemblyBoundaryLists.cs` with its client consumer named in a comment; the boundary test passes.
- [ ] **Stale references removed**: no comment in `src/` names `EnhancementOutcomeBroadcast` or an `(entityId, itemId)` enhancement request, and `EnhancementResultCode.cs` no longer calls its values provisional.

---

## Implementation Notes

- **Message types and codecs** go in `src/Foundation/Networking/WireProtocol/`, one type and one codec per message, as `SetTarget` / `SetTargetCodec`. Twelve messages: the eight Enhancement messages and the four NPC interaction messages. The codecs of the three messages this story never sends (`EnhancementRequestReceived`, a `Success` / `Destruction` `EnhancementAttemptResult`, `ServerBroadcast_Enhancement9`) are written here because AC-NC-40 and AC-NC-41 are codec criteria; Story 015 sends them.
- **`MessageTypeID` values** are not assigned by any ADR. Use the provisional convention of the existing message types (see `SetTarget.MessageTypeId` and its remark) and say so in each type's doc comment.
- **`EnhancementResultCode` moves to `Foundation`** (settled at `/story-readiness` 2026-10-09). With this story the enum appears in two server→client messages a client decodes, and ADR-012 Decision 3 rule 3 places enums that appear in wire messages in `IronGrind.Foundation`. `git mv` the file with its `.meta` from `src/ServerLogic/EnhancementSystem/` to `src/Foundation/EnhancementSystem/` (a new folder, with its own `.meta`); the namespace `IronGrind.EnhancementSystem` is unchanged. Update the boundary test's lists.
- **Wire type name in code** (user decision 2026-10-09). `IronGrind.EnhancementSystem.EnhancementAttemptResult` (the service's result struct, Story 004) keeps its name. The wire message `EnhancementAttemptResult` is the type `EnhancementAttemptResultMessage`, with the codec `EnhancementAttemptResultMessageCodec`; its doc comment states the wire name. The other eleven types use their wire names.
- **Preview needs a public query.** `EnhancementService.ValidateAttempt` is `internal`. Add a public read-only method that returns the validation code and, when valid, the item's level and the two table probabilities. It must not lock, consume or change anything.
- **`outcome` is not serialized** (UI-ENH-2): the decoder derives it from `resultCode`.
- **Duplicate check.** `EnhancementRequestDeduplicator` (Networking Core Story 015) exposes the character's `LastEnhancementRequestId`. The handler compares the request's `requestId` with it before validation. Writing the new id with the commit is Story 015's; this story only reads. How the handler finds the character's deduplicator is a small lookup interface defined here.
- **Attempt-start seam.** One interface with one method (character, `requestId`, item slot, scroll slot), defined here; a recording test double in this story's fixture; the production implementation is Story 015's.
- **`npcId` validation stays deferred** (user decision 2026-10-09). Story 006 deferred it to this story (TD-058 item 5, npc-shop.md OQ-NS-1); no NPC registry exists and the wire protocol defines no rejection for an unknown id. The handler passes any `npcId` to the tracker, as today. TD-058 item 5 stays open, for NPC authoring.
- **Outbound messages — `IClientMessageOutbox`** (user decision 2026-10-09). No server→client send path exists in code: no handler sends a reply today, and `INetworkTestObserver` has no callback that carries a message body. This story defines the seam in `src/ServerLogic/Networking/Outbound/`: one interface, `IClientMessageOutbox`, with one method, `Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)`. An implementation copies `body` before it returns; a handler keeps nothing. It is generic on purpose: Story 015 and Inventory Story 012 use the same interface, and the transport adapter story (ADR-014) writes the production implementation. Tests use a recording double (client, type, a copy of the body, the flag) and decode the captured body with the message's codec. `INetworkTestObserver` is not used in this story.
- **ADR-001's general limit of 10 requests per second per character** is cited by the wire protocol for `EnhancementPreviewRequest`, as for `BuyRequest`, `SellRequest` and `UseItemRequest`. No request type has a rate-limit tag for it (ADR-014 Decision 1; user decision 2026-10-09 for the preview). Nothing to implement here.
- **Stale comments to fix**: `INetworkTestObserver.cs`, `CommitBeforeBroadcastSequencer.cs`, `PriorityPathQueue.cs`, `QueuedMessage.cs`, `NetworkingTestHarness_Observer_tests.cs`, `WireProtocol_PriorityPathCap_tests.cs`. Comments only — the item-id based `INetworkTestObserver` enhancement callback signatures are replaced by Story 015.

---

## Out of Scope

- **Story 015**: `EnhancementRequestReceived` after CR-ENH-15 step 4; the result of an accepted attempt after the commit; the CR-ENH-11 hold of inventory messages; `ServerBroadcast_Enhancement9` delivery; AC-ENH-13 and AC-ENH-18; recording `LastEnhancementRequestID`; the `INetworkTestObserver` enhancement callbacks
- **Story 011**: the orchestration between `BeginAttempt` and `CompleteAttempt` / `RollBackAttempt`
- **Inventory Story 012**: `InventorySlotUpdate` and `InventoryFullSync` — codecs and sending
- Stories 003–007: all server-side behaviour behind the messages
- Story 009: holding other requests during an attempt
- Enhancement UI epic: screens, probability display, the heightened destruction warning (AC-ENH-25, 26, 31; UI-ENH-5 to UI-ENH-9)
- The transport adapter (ADR-014) and the production implementation of `IClientMessageOutbox`
- `npcId` validation (TD-058 item 5; NPC authoring)

---

## QA Test Cases

**Files** (to be created): `tests/EditMode/Networking/WireProtocol_EnhancementMessages_tests.cs` (codecs); `tests/EditMode/Integration/EnhancementSystem/Enhancement_ClientMessages_integration_tests.cs` (handlers, through the dispatcher harness of `InboundDispatchTestDoubles.cs`).

- **Body lengths and round trip** — one case per fixed-size message (`[TestCase]`), sizes as in the first criterion; boundary values for `requestId` (1, `uint.MaxValue`) and slot indices (0, 19, 255).
- **Fixed-point probabilities** — 0.65 / 0.35 → 6500 / 3500; 1.0 → 10000; 0 → 0.
- **Broadcast** — cases (a)–(d) of AC-NC-41.
- **Unknown result code** — bytes 10 and 255, for both messages.
- **NPC messages** — in the hub → `NPCInteractionOpened`, session active; outside → `RejectedNotInTownHub`, no session; close → session cleared, no message.
- **Preview, valid** — +4 item → `currentLevel` 4, 6500 / 3500; +2 item → 8500 / 1500; indices echoed; bag and locks unchanged.
- **Preview, rejected** — tier mismatch → `EnhancementPreviewRejected { RejectedTierMismatch }`; no NPC session → `RejectedNoNPCSession`; item at the maximum level → `RejectedAtMaxLevel`.
- **Preview during an attempt** — `BeginAttempt` done, not completed → `RejectedConcurrentAttempt`.
- **Cancel** — before any request, and after a valid request reached the seam → capture unchanged, bag unchanged.
- **Rejected request** — tier mismatch → one result with `RejectedTierMismatch`, `newLevel` 0, the request's `requestId`; no acknowledgment; seam not called. Item slot index 20 → `RejectedItemNotFound`.
- **Duplicate** — as stated in the criterion; then a request with `requestId = X + 1` reaches the seam.
- **Valid request** — slots (0, 1), `requestId` 7 → seam called once with (sender, 7, 0, 1); capture empty.
- **Cap exemption flag** — one case per message this story sends: rejection result → true; state update, preview rejection, `NPCInteractionOpened`, `RejectedNotInTownHub` → false.
- **Registration** — the five descriptors are present with held false, the stated body bounds and their own `RpcTypeTag` member; an over-length body is dropped at intake; all twelve types have a routing row with the expected direction.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_ClientMessages_integration_tests.cs` and `tests/EditMode/Networking/WireProtocol_EnhancementMessages_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (validation), Story 004 (`IsAttemptInProgress`, the pending attempt), Story 006 (NPC session tracker); Networking Core Story 015 (`EnhancementRequestDeduplicator`) and Story 036 (request dispatcher) — all Complete. The TD-046 design gate is closed (wire protocol Approved 2026-10-09). No dependency on Story 011.
- Unlocks: Story 015 (result delivery — also needs Story 011 and Inventory Story 012); Enhancement UI epic (request side)
