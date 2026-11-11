# Story 018: Client zone entry — login → connect → handshake → ack → token reconnect

> **Epic**: Authentication
> **Status**: Ready — `/story-readiness` 2026-11-11: NEEDS WORK (reassembly owner, heartbeat knob, zone host, assemblies/envelopes, retransmit identity) → fixed → Ready (Decision B)
> **Layer**: Core (client)
> **Type**: Integration
> **Estimate**: 10 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-11 step 6 ("The client connects to the zone and sends `SessionHandshake`; the zone validates the token, loads the character (Character Persistence) and admits the player"), AC-AUTH-21 (c), AC-AUTH-27 (c); `design/gdd/zone-instancing.md` — CR-ZI-8 steps 2–3 and 11 (`ZoneEntryAck` sent "once, when its dual gate opens"), CR-ZI-17 (an entering slot not acked within `ZONE_ENTRY_STALL_TIMEOUT_SECONDS` = 35 is reclaimed); `design/gdd/networking-session.md` — `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` = 10 (client waits for all fragments before sending `ZoneSnapshotRequest`), `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` = 3 ("before the client abandons and starts a fresh reconnect"), `HEARTBEAT_TIMEOUT_SECONDS` = 10; `design/gdd/networking-wire-protocol.md` — `SessionHandshake` 0xE136 (24-byte body: characterId, targetZoneId, 16-byte token), `ZoneEntryAck`, `ZoneStateSnapshotFragment`, `ZoneSnapshotRequest`, `ZoneFullResponse`, `Heartbeat` 0x0210
**Requirement**: `TR-auth-001`, `TR-auth-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 4 — the handshake carries CharacterID + token, never AccountID; the token path of `LoginRequest` for reconnect); ADR-004: Networking — NGO Substrate (`CustomMessagingManager`, 10-byte envelope); ADR-014 (connection-level messages)
**ADR Decision Summary**: The client logs in over HTTPS, connects to the `zoneServerPort` from `LoginResponse`, sends `SessionHandshake` on the R-OD priority path, reassembles the zone snapshot and acks it with `ZoneEntryAck`. After an unexpected disconnect it logs in once on the token path and repeats the entry with the rotated token.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — NGO client start/stop and `CustomMessagingManager` on the client (post-cutoff; NGO 2.13.3)
**Engine Notes**: Reuse the NGO client start/stop pattern from `tests/PlayMode/Networking/ClientMessageOutbox_ngo_run_tests.cs` and `InboundTransportAdapter_ngo_run_tests.cs` (verified against 2.13.3). Set the address and port on `UnityTransport` before `StartClient()`; check the 2.13.3 signature in `docs/engine-reference/unity/` before use.

**Control Manifest Rules (this layer)**:
- Required: the client never sends or logs an AccountID or the token — ADR-016 Decision 4, CR-AUTH-2
- Required: all project messages go through `CustomMessagingManager` with the ADR-004 envelope (10 bytes, or 14 bytes with `SenderEntityId` for a message whose server registration sets `carriesSenderEntityId: true`)
- Forbidden: `Enum.IsDefined` (IL2CPP) — CR-NET-7.4

---

## Decisions

- **A — Entry gate is snapshot-only for now (user decision 2026-11-11).** The GDD gate is `SessionReady` received **and** snapshot reassembled. `SessionReady` has no codec and the zone does not send it (`LoggingZoneEntryResponder` logs "SessionReady not implemented"; schema open, OQ-NC-SER-2). The client acks when the snapshot is reassembled. Deviation tracked as TD-104; restore the two-part gate when `SessionReady` exists.
- **B — No snapshot retransmit for now (user decision 2026-11-11).** The zone ignores a `ZoneSnapshotRequest` unless its 14-byte envelope carries the sender's own player EntityID (`ZoneSnapshotRequestService`). The client learns that ID only from `SessionReady`, which does not exist yet (TD-104). When the snapshot times out, the client therefore skips the request and runs the token reconnect, which gets a fresh snapshot. This departs from the GDD's "retry `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` times, then reconnect". Tracked in TD-104; add the retransmit back when `SessionReady` carries the EntityID.

---

## Acceptance Criteria

- [ ] **Entry sequence**: given a `LoginResponse { success:true, token, characterId, zoneId, zoneServerPort }`, the client session starts an NGO client to `zoneHost:zoneServerPort`, where `zoneHost` is the host part of the gateway URL that `AuthClient` was built with (the gateway and zone share one machine, ADR-016), unless the session config sets a non-empty `ZoneHostOverride`, and, on connect, sends exactly one `SessionHandshake { characterId, targetZoneId = zoneId, token }` (body bytes asserted against `SessionHandshakeCodec`); no AccountID is written anywhere
- [ ] **Snapshot + ack**: a client-side `SnapshotFragmentCollector` follows the reassembly protocol in `networking-wire-protocol.md` (steps 1–5):
  - **Start:** on `fragmentIndex = 0` it allocates `totalFragments × ZoneStateSnapshotFragment.MaxPayloadBytes` and records `snapshotVersion`.
  - **Each fragment:** its payload is copied to `fragmentIndex × MaxPayloadBytes`. A fragment already received is ignored. A fragment whose version is older than the current one (`StaleDiscardComparer.IsNewerVersion`) is dropped. A `fragmentIndex = 0` with a newer version discards the partial buffer and starts again. A fragment before any `fragmentIndex = 0` is dropped.
  - **Size limit:** `totalFragments` above `MAX_CLIENT_SNAPSHOT_FRAGMENTS` (config, default 32; the GDD worst case is 19) → the entry fails with `EntryFailed(SnapshotTooLarge)`.
  - **Completion:** when every fragment is in (Decision A), the client sends exactly one `ZoneEntryAck { zoneId, snapshotVersion }`. A second completion in the same entry attempt sends nothing.
  - **Out of scope:** parsing `EntityState[]` from the buffer.
- [ ] **Snapshot timeout** (Decision B): if no complete snapshot arrives within `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` of sending `SessionHandshake`, the client sends no `ZoneSnapshotRequest`, stops the NGO client, discards the partial buffer and runs the AC-6 token reconnect once. If the entry after that reconnect also times out → `EntryFailed(SnapshotTimeout)`, then `SessionEnded`, with no further login. Only one token reconnect is allowed per entry chain (AC-3 and AC-6 share the same allowance).
- [ ] **Heartbeat**: while connected, the client asks `HeartbeatActivityTracker.IsHeartbeatDue` every update, with the interval set to `HEARTBEAT_INTERVAL_SECONDS` (`networking-wire-protocol.md` tuning table, default 3). It calls `RecordOutboundPacket` for **every** packet it sends (handshake, ack, heartbeat), so a heartbeat is skipped when other traffic went out within the interval. At config load, the interval must be ≥ 1 and < `HEARTBEAT_TIMEOUT_SECONDS / 2` (that is, < 5); anything else is rejected. `Leave()` stops heartbeats.
- [ ] **Entry rejected**: `ZoneFullResponse`, or a transport close before the ack → `EntryFailed(reason)` reported once, NGO client shut down, no ack sent
- [ ] **Token reconnect**: an unexpected transport close after the ack → exactly one `AuthClient.Login(username, "", currentToken, …)`; on success the entry sequence repeats with the new token, `zoneId` and port; on failure the client reports `SessionEnded` and does **not** retry (this is how an evicted client — AC-AUTH-27 (c) — stops: its token was deleted, CR-TOK-6)
- [ ] **Deliberate stop**: `Leave()` shuts the NGO client down with no reconnect attempt
- [ ] **Token hygiene**: the token is held in one private 16-byte buffer, replaced on rotation, zeroed on `Leave()` / `SessionEnded`; no captured log line contains it (test scans logs for its hex and base64 forms)
- [ ] **Dev harness**: a DevHarness MonoBehaviour `ZoneEntryDevHarness` takes username / password / character name / gateway URL from a ScriptableObject or the command line, runs register-if-needed → login → entry, and logs each step with a timestamp (`login ok zone=N port=P`, `handshake sent`, `snapshot v=V`, `ack sent`, `disconnected`, `reconnect ok` / `reconnect failed`) — Story 017's evidence reads these lines
- [ ] **Load driver** *(for Story 017 AC-AUTH-05a)*: a mode that fires N concurrent logins (config, default `MAX_AUTH_PENDING`) for D seconds and logs per-login latency; logins only, no zone entry

---

## Implementation Notes

- Pure C# core `ClientZoneSession` in `src/Client/Session/` behind seams: `IZoneClientTransport` (start / stop / send / on-message / on-disconnect), the existing `AuthClient`, and a clock seam. States: `Idle → LoggingIn → Connecting → AwaitingEntry → Entered`, `Entered → Reconnecting → Connecting …`, any → `Ended`. EditMode tests drive it with a fake transport and fake clock.
- NGO adapter `NgoZoneClientTransport` in `src/Client/Session/Unity/`: registers named handlers for the server → client entry messages and decodes them with the existing Foundation codecs (`ZoneStateSnapshotFragmentCodec`, `ZoneFullResponseCodec`, `ZoneSessionEndedCodec`).
- New assembly `IronGrind.Client.Session.Unity` (`src/Client/Session/Unity/`). It references `IronGrind.Client`, `IronGrind.Foundation`, `Unity.Netcode.Runtime` and `Unity.Networking.Transport`, with the same define constraint as `IronGrind.Client`. `ClientZoneSession` stays in `IronGrind.Client`, which gets **no** NGO reference. `IronGrind.DevHarness` adds the new assembly. The PlayMode test assembly adds it plus the NGO assemblies.
- Client → server envelopes match the server registrations: `SessionHandshake`, `ZoneEntryAck` and `Heartbeat` use the 10-byte envelope (`MessageEnvelopeCodec.Write(…, ServerMessageEnvelope)`), with no sender id. `ServerTickNumber` is the tick from the last envelope received, or 0 before any. `ZoneSnapshotRequest` (14-byte envelope) is not sent; Decision B.
- Snapshot reassembly: the client owns the fragment buffer (`SnapshotFragmentCollector`, `src/Client/Session/`). `ZoneSnapshotReassemblyTracker` is not used: it counts retransmit attempts the client does not make (Decision B). The client keeps its own deadline: handshake send time + `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` (config, default 10, from `networking-session.md`; no code constant exists), measured on the clock seam.
- The zone does not tell an evicted client why it was closed (no "session stolen" `DisconnectReason`; eviction closes the transport). The client needs no such signal: its reconnect login fails because the token was deleted.
- TD-103: an intentional `Leave()` looks like a dropped client to the zone (5-minute ghost). Expected; not fixed here.
- Message IDs: the R-OD messages used are in 0xE000–0xEFFF (0xE136 handshake, 0xE13x zone-entry block); `Heartbeat` 0x0210 is connection-level as registered by Zone Instancing Story 016.
- Constants (`HEARTBEAT_INTERVAL_SECONDS`, `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS`, `MAX_CLIENT_SNAPSHOT_FRAGMENTS`, `ZoneHostOverride`, load-driver N and D) are config values, not literals; config defaults come from the existing definitions (`HeartbeatActivityTracker.HEARTBEAT_INTERVAL_SECONDS`, `ZoneStateSnapshotFragment.MaxPayloadBytes`).
- Files: `src/Client/Session/` (core, collector, config), `src/Client/Session/Unity/IronGrind.Client.Session.Unity.asmdef` + adapter, `src/DevHarness/IronGrind.DevHarness.asmdef` (reference added), the PlayMode test asmdef (references added), `ZoneEntryDevHarness` in `src/DevHarness/`.
- Performance: client only, one session; no zone tick impact. The reassembly buffer is at most `MAX_CLIENT_SNAPSHOT_FRAGMENTS × MaxPayloadBytes`, allocated once per entry attempt.

---

## Out of Scope

- Login / registration UI, character select (UX specs)
- Rendering the zone from the snapshot, spawning entities, sending movement intents
- `SessionReady` schema and server send (Decision A, TD-104)
- Clean logout message (TD-103)
- iOS device run (Story 017, ADR-016 Verification 3)

---

## QA Test Cases

- **AC-1/2 (EditMode)**: fake transport + fake auth → login ok → connect → one handshake (24-byte body, fields match); gateway `https://127.0.0.1:8443` + port 7777 → transport started with `127.0.0.1:7777`; override `10.0.0.5` → `10.0.0.5:7777`; feed all fragments → one ack with zone and version; feed a duplicate completion → no second ack; fragments out of order → one ack; duplicate fragment → ignored; newer-version index 0 mid-stream → restart, ack carries the new version; `totalFragments` = 33 → `EntryFailed(SnapshotTooLarge)`
- **AC-3**: no complete snapshot for 10 s after the handshake (including no fragment at all) → no `ZoneSnapshotRequest` sent, transport stopped, one token-path login; a second timeout → `EntryFailed(SnapshotTimeout)` + `SessionEnded`, login count still 1
- **AC-4**: idle after the ack, interval 3 → heartbeats at 3, 6 and 9 s; an ack sent at t = 1 → the first heartbeat moves to t = 4; none after `Leave()`; intervals 5 and 0 → config rejected
- **AC-5**: `ZoneFullResponse` → one `EntryFailed`, transport stopped, no ack
- **AC-6**: disconnect after ack → one token-path login with the old token; success → second handshake carries the new token; failure → `SessionEnded`, no further login
- **AC-7/8**: `Leave()` → no login call; token buffer all zero; captured logs contain neither the token's hex nor base64
- **AC-9/10 (manual, Editor)**: harness against local gateway + Editor zone → log lines present in order; load driver run logs N latencies

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Client/ClientZoneSession_tests.cs` (AC-1 to AC-8)
- `tests/PlayMode/Client/NgoZoneClientTransport_ngo_run_tests.cs` (adapter connect / send / receive on loopback)
- Harness and load-driver manual run noted in `production/qa/evidence/auth-end-to-end-evidence.md` (shared with Story 017)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Authentication Story 015 (`AuthClient`, Complete); Gateway Story 012 (zone-side handshake intake, Complete); Zone Instancing Stories 015–016 (entity state source, heartbeat timeout, Complete)
- Unlocks: Authentication Story 017
