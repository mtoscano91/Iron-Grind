# Story 009: `AppearanceChangedEvent` Zone Delivery

> **Epic**: Equipment System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-11, Visual/Audio Requirements, Player Fantasy (others see the gear); `design/gdd/networking-wire-protocol.md` — `AppearanceChangedEvent`; `design/gdd/networking-channel-contract.md` — `AppearanceChangedEvent` row (S→ALL, R-OD, P1)
**Requirement**: `TR-equip-007` (wire half; the encoding is Story 005)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-004: Networking Library (NGO); ADR-015: Cross-Zone Message Relay (for the zone client sink only)
**ADR Decision Summary**: The project's own codecs ride NGO custom messages (ADR-004 Amendment 1). Server-to-many sends in a zone go through the zone's outbox; `IZoneClientBroadcastSink.EnqueueToAllClients` is the existing "all clients of this zone" path (ADR-015 Decision 1). This event is zone-local — it does **not** go through the cross-zone relay.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-004 — NGO 2.13.3 is post-cutoff)
**Engine Notes**: No direct NGO call; reuse the sink. Verify, at `/story-readiness`, that `EnqueueToAllClients` reaches only this zone's clients (its contract, not the relay's `IServerWideBroadcaster`).

**Control Manifest Rules (this layer)**:
- Required: subscribe in the constructor, unsubscribe in `Dispose()`; no lambda capture (ADR-010); tick thread only
- Forbidden: `IServerWideBroadcaster` for a zone-local event (it would send to every zone)
- Guardrail: 5-byte body, no allocation per send (reuse a body buffer, as `Enhancement9BroadcastSender` does)

---

## Acceptance Criteria

*From the GDDs above, scoped to this story:*

- [x] `AppearanceChangedEvent` codec: `{uint entityId, byte equipmentAppearanceFlags}` — 5-byte body, round-trips; wrong length rejected
- [x] An `AppearanceChangedSender` subscribes to `EquipmentService.OnAppearanceChanged` (Story 005) and enqueues one `AppearanceChangedEvent` to all clients of the zone, including the equipping player, with the new flags byte and the player's `EntityID`
- [x] Sent only when the byte changed — never for `NoChange`, a failure, or an equip that leaves the byte the same (driven by Story 005's raise rule)
- [x] The sink is called with `isCapExempt: false` (readiness decision 2026-10-28): CR-NET-7.7 reserves the cap exemption for `EnhancementRequestReceived`, `EnhancementAttemptResult` and bulk-transfer fragments — "No other message class receives this exemption"; the channel contract's R-OD P1 does not make a message exempt. ADR-015 Decision 1 and `Enhancement9BroadcastSender` pass `false` for zone-wide sends. Asserted by a test
- [x] A restore on login (Story 007) sends nothing; the joining player's byte reaches others through `EntityState.equipmentAppearanceFlags` in the zone snapshot
- [x] `Dispose()` unsubscribes; a send after dispose does not happen
- [x] An exception from the sink is contained (one error logged per caught exception) and does not change the equip result

---

## Implementation Notes

*Derived from ADR-004 / ADR-015 and the `Enhancement9BroadcastSender` precedent:*

- Foundation: `AppearanceChangedEvent.cs`, `AppearanceChangedEventCodec.cs`, a `MessageRoutingRegistry` entry (server → client, R-OD).
- **Message type id (readiness decision 2026-10-28):** `AppearanceChangedEvent.MessageTypeId = 0xE142` — next in the equipment block after `EquipRequest 0xE140` / `EquipResultMessage 0xE141` (Story 008). R-OD on Path 1 (priority, not cap-exempt), registered with `AddPriority` and `MessageDirection.ServerToAllZoneClients`. The wire protocol's Path 1 list names it (GDD note 2026-10-28).
- **Recipients (verified at readiness 2026-10-28):** `ClientMessageOutbox.EnqueueToAllClients` walks only the outbox's own client set, which is this zone's clients (ADR-015 Decision 1); it does not publish to the relay. That set includes a client still loading, before `SessionReady` (accepted, ADR-015 Decision 1), so a loading client can receive this event for an entity it does not know yet — the client-side unknown-EntityID buffering rule (`PlayerJoinedZone` row, networking-channel-contract.md) covers it; nothing to do server-side.
- ServerLogic: `src/ServerLogic/EquipmentSystem/AppearanceChangedSender.cs` — `IDisposable`, one per zone, constructed in the zone composition root after the `EquipmentService`.
- **Forward dependency — not in this story:** the snapshot's player-state source. `IZoneEntityStateSource.TryGetPlayerState` is implemented only by `LoggingZoneEntityStateSource` (placeholder), so the zone snapshot does not yet carry the Equipment System's byte. When the real source is built it must read `IEquipmentService.GetAppearanceFlags`. OQ-EQS-6 (full snapshots always carry the byte) stays open with Networking Core.
- The wire protocol's "future: emit after an Enhancement outcome that changes PrestigeBand" cannot happen — equipped items cannot be enhanced (CR-EQS-1). No hook needed.

---

## Out of Scope

- The real `IZoneEntityStateSource` and snapshot wiring (forward dependency above)
- Client-side rendering of the byte (VFX / Inventory UI)

---

## QA Test Cases

**Files**: `tests/EditMode/Networking/AppearanceChangedEvent_Codec_tests.cs` (new) and `tests/EditMode/EquipmentSystem/EquipmentSystem_AppearanceChangedSender_tests.cs` (new), with a fake `IZoneClientBroadcastSink` recording calls.

- **Codec** — round trip for `entityId = uint.MaxValue, flags = 0xFF` and `0, 0x00`; 4- and 6-byte bodies rejected.
- **Equip sends** — equipping a Dark Steel Fire sword → exactly one `EnqueueToAllClients` with type id `0xE142`, body `{entityId, 0xC8}` (no armor) and `isCapExempt == false`.
- **No send on no change** — `NoChange`, `StatRequirementNotMet`, and a Bronze Helmet equip that leaves the byte unchanged → zero sink calls.
- **Swap and unequip** — +0 → +7 Iron Sword swap sends `0x44`; unequip sends `0x00` (AC-EQS-30 (c)–(d) seen on the wire).
- **Restore** — `RestoreSlots` → zero sink calls.
- **Dispose** — after `Dispose`, an equip that changes the byte → zero sink calls.
- **Sink throws** — equip still returns `Success`; one error logged.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: both test files above — must exist and pass

**Status**: [x] Created — `AppearanceChangedEvent_Codec_tests.cs` 10 cases, `EquipmentSystem_AppearanceChangedSender_tests.cs` 17 tests; all passing (EditMode 4434 / 0 failed, 2026-10-28)

---

## Dependencies

- Depends on: Story 005 (the event), Story 008 (message type id allocation and composition-root wiring)
- Unlocks: None

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 7/7 passing, all covered by automated tests (none deferred)
**Deviations** (advisory, user accepted 2026-10-28, no tech debt):
1. **Not wired into the zone composition root.** The Implementation Notes say the sender is constructed after the `EquipmentService`; `ZoneServerComposition` still uses `StandInZoneEquipmentService`, so `AppearanceChangedSender` is built only by tests (the `Enhancement9BroadcastSender` precedent). Joins the server-bootstrap carry-over from Story 008.
2. **Exception containment lives in the service.** The sender does not catch; `EquipmentService.Raise` catches and logs once through `Debug.LogException`, and the equip result stands. `Raise` invokes the multicast delegate in one try/catch, so a throwing subscriber stops later ones — the sender is the only `src` subscriber today; if a second appears, `Raise` should catch per handler.
3. **Snapshot byte still a forward dependency** (as written in the Implementation Notes): the real `IZoneEntityStateSource` must read `IEquipmentService.GetAppearanceFlags`; OQ-EQS-6 stays open.
**Scope**: also touched `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (the two new public Foundation types).
**Test Evidence**: Integration — `tests/EditMode/Networking/AppearanceChangedEvent_Codec_tests.cs`, `tests/EditMode/EquipmentSystem/EquipmentSystem_AppearanceChangedSender_tests.cs`
**Code Review**: Complete (2026-10-28, APPROVED WITH SUGGESTIONS → all applied: `WriteBody` exception doc, reused-buffer remark, 5 tests)
