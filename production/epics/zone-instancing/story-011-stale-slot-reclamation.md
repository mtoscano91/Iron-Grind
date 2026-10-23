# Story 011: Stale Slot Reclamation and Entry Stall (CR-ZI-17, CR-ZI-8 step 11, F-ZI-2)

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-23) — written 2026-10-22; rewritten at `/story-readiness` 2026-10-23 after the `ZoneEntryAck` GDD revision (Zone Instancing Approved 2026-10-23)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 11 hours (codecs 2 h, leave path + ack 3 h, cases a/b/c 4 h, tests 2 h)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 step 11, CR-ZI-17 (cases a/b/c, points 1–8, "Not stale"), F-ZI-2, EC-ZI-13, EC-ZI-14, AC-ZI-14, AC-ZI-28, AC-ZI-29, AC-ZI-30, Tuning Knobs (`ZONE_ENTRY_STALL_TIMEOUT_SECONDS`); `design/gdd/networking-wire-protocol.md` — `PlayerLeftZone`, `ZoneEntryAck`, `ZoneFullResponse` (fourth case, `Other`); `design/gdd/networking-session-token.md` CR-TOK-6 (per-case token rule)
**Requirement**: `TR-zi-016` *(epic placeholder)*

**ADR Governing Implementation**: ADR-011 (main-thread mutation, tick-counted timers); ADR-014 (inbound dispatch — the ack is a connection-level message); ADR-004 (wire codecs); ADR-010 (Party by direct call); ADR-012 (assembly boundary)
**ADR Decision Summary**: slot mutations and the stall timer run on the tick thread; the ack is routed like the other connection-level zone messages; `NotifyZoneExit` is a direct call on `IPartyZonePresence`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — no engine API; codecs follow Story 006's span-based pattern.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` for the zone side; codecs in `IronGrind.Foundation` next to `PlayerJoinedZone` — ADR-012
- Required: direct calls on injected interfaces — ADR-010
- Forbidden: `await` in tick-driven code — ADR-011
- Guardrail: the stall check is O(entering slots) at most once per tick; no allocation per tick

---

## Acceptance Criteria

**Wire**
- [ ] **Codecs**: `PlayerLeftZone { EntityID entityId; DisconnectType disconnectType; }` (5-byte body, S→C) and `ZoneEntryAck { ZoneID zoneId; uint snapshotVersion; }` (8-byte body, C→S) with `WriteBody` / `TryReadBody` codecs in the Story 006 style (round trip, short buffer rejected, unknown `DisconnectType` → `Timeout = 1` per wire protocol line 274). Provisional ids **0xE134** (`ZoneEntryAck`) and **0xE135** (`PlayerLeftZone`), added to `MessageRoutingRegistry` and to the TD-011 provisional-id comment and register row.
- [ ] **ADR-014**: Decision 1 table gets a `ZoneEntryAck` row (connection-level, tick thread) — dated note in the ADR.

**Entering phase and ack (CR-ZI-8 step 11, AC-ZI-29, EC-ZI-13)**
- [ ] A slot is **entering** from allocation (Story 009 step 4/5) until a `ZoneEntryAck` naming this zone arrives from the slot's connection; the allocation tick is recorded. Steps 6–10 (Story 010) do not wait for the ack.
- [ ] **AC-ZI-29**: an ack for its zone from an entering slot marks it acked; the stall timer no longer reclaims it. An ack from a connection with no slot, an acked slot, a reclaimed slot, or naming another zone changes nothing and logs exactly one `UnexpectedZoneEntryAck` warning per connection, however many it sends. `snapshotVersion` is not checked (logged only).

**Leave path (CR-ZI-17 points 1–5)** — one internal entry point used by all three cases, reused by Story 013:
- [ ] In order: (1) entity off the tick roster (`ZoneTickRoster.Deactivate`) or out of the pending list if step 10 has not run; (2) `PlayerLeftZone(entityId, DisconnectType)` to every other connected zone client that was sent `PlayerJoinedZone` for it; (3) `IPartyZonePresence.NotifyZoneExit` only when `TryGetFormalParty` is true; (4) slot zeroed and freed, directory entry removed, occupancy change raised (Story 001: may start the T-7 idle timer). (5) **No `SaveSession`** — the persistence fake records no call.
- [ ] Reclaiming a slot that holds no entity / was already freed is a no-op (no second `PlayerLeftZone` or `NotifyZoneExit`).

**Case (a) — stall timeout (AC-ZI-14)**
- [ ] An entering slot whose allocation is `ZONE_ENTRY_STALL_TIMEOUT_SECONDS × TICK_RATE_HZ` ticks old is reclaimed on that tick: leave path; then `ZoneFullResponse(zoneId, Other=255, retryAfterSeconds=0)` to its connection; then the server closes the connection; then `IZoneEntryReclaimSessionSink.ExpireEnteringSession(clientId, deleteTokenIfOwned: true)`. A later ack or RPC from that connection is not processed. An ack received one tick before the timeout prevents all of this.

**Case (b) — duplicate handshake (AC-ZI-28)**
- [ ] In `ZoneEntryAdmission.BeginEntry`, after `_validator.IsValid` passes and **before** `TryPreCheck` / `LoadCharacter`: if another connection holds an **entering** slot for the same `CharacterID` (new reverse lookup on `ZoneConnectionCharacterDirectory`), that slot is reclaimed — leave path, the old connection closed with **no message**, `ExpireEnteringSession(oldClientId, deleteTokenIfOwned: false)` — then admission continues. If `LoadCharacter` succeeds, the occupied count equals the count before the reclamation and the character holds exactly one slot; if it fails, the new connection gets `ZoneFullResponse(LoadCharacterFailed)`, the old slot stays reclaimed, the character holds no slot.
- [ ] **Not stale**: a handshake whose `CharacterID` holds an **acked** slot on another connection is not reclaimed and gets no second slot: it is ignored with one `EntryHandshakeCharacterAlreadyInZone` warning per connection (session stealing, B-NP-7, ends the earlier session first; TD-076 covers the ordering assumption).

**Case (c) — transport drop (AC-ZI-30, EC-ZI-14)**
- [ ] When an entering slot's connection is removed (`IClientConnectionListener.RemoveClient` — the transport close and the heartbeat-timeout disconnect both arrive here), the leave path runs on the next tick, nothing is sent, and `ExpireEnteringSession(clientId, deleteTokenIfOwned: true)` is called. No ghost session is created. An acked slot's removal is not handled here (Story 013 / session layer — ghost as usual).

**Session seam**
- [ ] New `IZoneEntryReclaimSessionSink.ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned)` — contract (doc comment): session `Connected → Disconnected_SessionExpired`, no persistence write, no TTL; if `deleteTokenIfOwned`, delete `ActiveSessionTokens[AccountID]` only when its `sessionId` is this session's. A logging stand-in is the composition default (TD-077).

---

## Implementation Notes

- Knob: `ZONE_ENTRY_STALL_TIMEOUT_SECONDS = 35` (range [31, 60]) as a `public const` default through the constructor; the tick count is computed once from `TICK_RATE_HZ`.
- Stall check: keep entering slots in a small list or flag on the slot; scan once per tick from the existing tick hook (no per-tick allocation). Reclamation in case (a) runs before teardown in the same tick (EC-ZI-15) — the scan runs in the tick step that precedes the close steps; state the hook used.
- `DisconnectType` for `PlayerLeftZone`: (a) and (c) `Timeout = 1`; (b) the value the wire protocol uses for session stealing — check the `DisconnectType` enum; if none fits, use `Timeout` and log the choice in the completion notes.
- Case (b) reverse lookup: `ZoneConnectionCharacterDirectory` gets `TryGetClientId(CharacterID, out uint)` maintained with the forward map.
- Case (c): `ZoneEntryAdmission` already implements `IClientConnectionListener`; record the removal and reclaim on the next tick (do not mutate inside the transport callback if it can run mid-tick).
- The server-side close: use the same transport close seam the snapshot-retransmit path (Story 008) or teardown uses; name it in the completion notes.
- **Leave path is shared**: Story 013 (`ReleasePlayer`) calls the same helper and adds its save ordering; keep the helper free of save logic.

---

## Out of Scope

- The real session-layer transition and token delete behind `IZoneEntryReclaimSessionSink` (`ConnectionStateMachine` has no `Connected → Disconnected_SessionExpired` without persistence) — Networking Core follow-up story, wired by the composition (TD-077).
- Sending `ZoneEntryAck` from the client when the dual gate opens, and the client's teardown on `ZoneFullResponse(Other)` after its gate opened (CR-ZI-17 point 8) — no client zone-entry story exists yet (noted in the EPIC).
- Teardown of an entering slot without a save (AC-ZI-31, EC-ZI-15) — Story 012.
- Ghost / TTL slot release and acked disconnects — Story 013 and the session contract.
- Client retransmit and reconnect behaviour.

---

## QA Test Cases

- **Codecs** — `PlayerLeftZone` and `ZoneEntryAck` round trip; short buffer → false; unknown `DisconnectType` → `Timeout`; registry has 0xE134 / 0xE135 with the right direction.
- **AC-ZI-29 ack** — entering slot + ack → acked; timer passes → nothing reclaimed. Stray acks (no slot / acked / other zone / ×3 from one connection) → no state change, exactly 1 warning per connection.
- **AC-ZI-14 stall** — timeout 35 s at test tick rate; one other client connected; at the timeout tick: entity off roster, other client got `PlayerLeftZone`, stalled client got `ZoneFullResponse(Other, 0)` then closed, session sink called with `deleteTokenIfOwned: true`, slot free, count −1, directory has no entry, persistence fake has no `SaveSession`. Variant: ack one tick before → none of it.
- **Stall before step 10** — slot reclaimed while still in the roster's pending list → removed from pending, never activated.
- **AC-ZI-28 reclaim, load succeeds** — C entering on A (party member); handshake for C on B → A reclaimed before `LoadCharacter` is called (fake records order), `NotifyZoneExit` once, A closed with no message, sink called for A with `deleteTokenIfOwned: false`; B admitted, count equal to before, C holds one slot.
- **AC-ZI-28 reclaim, load fails** — same, `LoadCharacter` fails → B gets `ZoneFullResponse(LoadCharacterFailed)`, C holds no slot.
- **Not stale** — C acked on A; handshake for C on B → no reclamation, no second slot, one warning.
- **AC-ZI-30 drop** — entering slot, `RemoveClient` → next tick leave path, no message, sink called `deleteTokenIfOwned: true`, no `SaveSession`. Acked slot `RemoveClient` → not handled here.
- **Idempotent** — case (c) then case (a) for the same slot → one leave path only.
- **Lifecycle** — last entering slot reclaimed in an Active zone → idle timer starts.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_StaleSlot_tests.cs` (cases a/b/c, ack, leave path) and the codec cases in the existing wire-message test fixture from Story 006 — must exist and pass.

**Status**: [x] Created — 34 tests in `ZoneInstancing_StaleSlot_tests.cs` + codec cases in `ZoneInstancing_WireMessages_tests.cs`, all passing (EditMode 2026-10-23: 3929 passed, 0 failed, 70 DB skips)

---

## Dependencies

- Depends on: Stories 009, 010 (both Complete)
- Unlocks: Story 013 (reuses the leave path)

---

## Completion Notes

**Completed**: 2026-10-23
**Criteria**: 11/11 passing (all covered by automated tests; ADR-014 row is a doc check)
**Deviations** (advisory, user accepted):
1. Case (b) `PlayerLeftZone` uses `DisconnectType.Timeout` — the enum has no session-stealing value (fallback the story allowed).
2. `PlayerLeftZone` goes to every other directory-mapped client rather than a recorded "sent `PlayerJoinedZone`" set; Story 010 adds the directory entry and sends `PlayerJoinedZone` in the same call, so the sets coincide.
3. Close seam: `IInboundTransportDisconnector.DisconnectClient` with the zone client id widened to `ulong` — assumes zone client id == transport client id (Story 014 composition must hold this).
4. Acks are recorded in the receive callback and applied at the next `ZoneEntryReclamation.OnTick`; `OnTick` and `Register` are not wired into the tick pipeline yet (Story 014).
5. Two in-flight loads for the same character (no slot yet) are invisible to case (b) — pre-dates this story, TD-078.
**Test Evidence**: Logic — `tests/EditMode/ZoneInstancing/ZoneInstancing_StaleSlot_tests.cs` (34 tests) + `tests/EditMode/ZoneInstancing/ZoneInstancing_WireMessages_tests.cs` (codec cases)
**Code Review**: Complete — `/code-review` 2026-10-23 CHANGES REQUIRED -> "fix all" applied (stall reply cap-exempt; transport close wrapped so the session sink always runs; ack warning text; 5 tests added; TD-078)
**Implementation notes**: tick hook — `OnTick` belongs in the simulation step before the close steps (EC-ZI-15); the shared leave path is `ZoneEntryReclamation.LeaveZone(EntityID, DisconnectType)` (no save logic) for Story 013. Out-of-scope files touched (valid): `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (ADR-012 shared entries), `docs/tech-debt-register.md` (TD-078).
