# Story 017: End-to-end login → zone entry

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 8 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-1, CR-AUTH-4 (b), CR-AUTH-11, EC-AUTH-10 (gateway down); AC-AUTH-01, 05a, 05b, 21, 27, 29 (d)
**Requirement**: `TR-auth-001`, `TR-auth-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Validation Criteria, Engine Compatibility verification item 2)
**ADR Decision Summary**: The epic closes when AC-AUTH-01, 04, 08, 12, 21, 25, 27, 31 pass against the gateway and a test database; the verification items pass on the Windows Editor, the Linux zone build and an iOS device; and a login → UDP zone entry → disconnect → token reconnect round trip works end to end. The VPS, iOS device and Linux zone build checks moved to Story 019 (split 2026-11-11, at Story 017 readiness).

**Engine**: Unity 6.3 LTS + .NET 10 | **Risk**: MEDIUM — Play-mode exit in the Editor (Verification 2)
**Engine Notes**: Verification 2: Windows Editor zone ↔ local gateway; exiting Play mode stops the link thread, entering again re-registers the same ZoneID. Verification 1 (Linux zone build) → Story 019.

**Control Manifest Rules (this layer)**:
- Required: S10 `Gateway.RegisterZone(zoneId, port)` opens the link and sends `RegisterZone` — ADR-009 Amendment 1, ADR-016
- Guardrail: zone tick within its 50 ms budget while logins are processed — AC-AUTH-05a
- Guardrail: engine behaviour verification-pending items 1–4 — ADR-016 Engine Compatibility (item 2 here; 1, 3, 4 in Story 019)

---

## Acceptance Criteria

- [x] Harness assets: scene `Assets/Scenes/DevHarness/ZoneEntryHarness.unity` holding the `ZoneEntryDevHarness`, and config asset `Assets/Scenes/DevHarness/ZoneEntryHarnessConfig.asset`, pointed at the local gateway (Caddy dev) and the Editor zone
- [x] Story 018 AC-9 closed: a harness run against the local gateway and the Editor zone logs the entry steps in order (login → connect → handshake → snapshot complete → ack → entered); log recorded in the evidence doc
- [x] Story 018 AC-10 closed: a load driver run against the local gateway logs N login latencies; recorded in the evidence doc
- [x] AC-AUTH-01 (real link): a correct login through the gateway, with an Editor zone registered over the real link, returns a non-zero token, valid `CharacterID`, non-zero `zoneId` and the zone's port; the zone received `Admit` with that token before the response (gateway + zone logs with timestamps)
- [x] AC-AUTH-21 (all parts) end to end: the client connects to the zone with the token, the session is `Connected`, `LoadCharacter` ran once in the zone (zone log), and the harness log line `snapshot player name=<n>` — decoded with `EntityStateCodec` from the player's entry in the reassembled snapshot — equals the registered `characterName`
- [x] AC-AUTH-27 end to end: client A in the zone, client B logs in to the same account → A is evicted (saved, closed, no reconnect), B enters
- [x] AC-AUTH-05b: a dev-build-only per-tick timestamp log on the zone (one line per tick: tick number + monotonic timestamp in ms). `ServerTickLoop` only records `actualTickDurationSeconds` (a duration), so this log is new. It has its own EditMode test.
- [x] AC-AUTH-05a: with ≥ 2 clients connected and the load driver holding ≥ `MAX_AUTH_PENDING` (10, `GatewayOptions.MaxAuthPending`) concurrent logins in flight for 60 s, the tick log shows no inter-tick gap > 50 ms in that window
- [x] AC-AUTH-29 (d): with the gateway stopped, the zone keeps ticking and serving its connected players (tick log + a client still moving)
- [x] Round trip (Editor): login → UDP zone entry → transport drop (no logout message exists, TD-103) → token reconnect → `Connected` again with the same character; the evidence notes the session went to `Disconnected_SessionActive` and was rebound by the reconnect
- [x] ADR-016 Verification 2 passes and is recorded
- [x] Evidence in `production/qa/evidence/auth-end-to-end-evidence.md` (Story 019 appends its VPS section to the same file)

---

## Implementation Notes

*Derived from ADR-016 Validation Criteria:*

- This story wires and verifies; it adds little code: the harness scene and config asset, the snapshot event below, the name logging, and the per-tick timestamp log. The load driver (`LoginLoadDriver`) and the harness (`ZoneEntryDevHarness`) exist from Story 018.
- The gateway epic provides the real `IZoneLink`, `IAccountZoneMap`, `IZoneRouter`, the zone's link thread, `IGatewayRegistration` and `IZoneLauncher` (all Done).
- Snapshot to harness *(readiness 2026-11-11)*: `ClientZoneSession` gets one new event (e.g. `SnapshotAssembled`, carrying the reassembled snapshot bytes), raised before the ack is sent. The harness decodes it with `EntityStateCodec.TryReadBody` and logs `snapshot player name=<n>` for each player entry. One EditMode test for the event (raised once, before the ack, with the reassembled bytes), one for the harness's decode-and-log helper.
- Round trip disconnect: there is no logout message (TD-103), so "disconnect" means a transport drop (stop the harness's transport, or kill the client). The zone keeps the character as a ghost in `Disconnected_SessionActive` until the token reconnect rebinds the slot.
- AC-AUTH-16 end to end, the VPS Caddy check (`check-tls-only.sh`), the hostname note on `SetConnectionData` and the Konscious VPS timing moved to Story 019.

---

## Out of Scope

- Login and registration screens (UX specs)
- AC-AUTH-24 (gateway map memory, advisory) — gateway epic
- Everything that needs the VPS, an iOS device or a Linux zone build — Story 019 (ADR-016 Verification 1, 3, 4; Caddy prod; round trip on the VPS; AC-AUTH-16 end to end; Konscious VPS timing; zone host as a hostname)

---

## QA Test Cases

- **AC-1 (AC-AUTH-01, 21)**: Real login to zone entry
  - Setup: dev PostgreSQL, gateway, Caddy dev, Editor zone in Play mode, `ZoneEntryHarness` scene
  - Verify: register, log in, enter the zone
  - Pass condition: harness log `snapshot player name` = registered character name; entry steps logged in order; gateway log `Admit` timestamp < response timestamp; zone log `LoadCharacter` once
- **AC-2 (AC-AUTH-27)**: Session steal across clients
  - Setup: client A in zone
  - Verify: client B logs in to the same account
  - Pass condition: A disconnected with no reconnect; character saved (DB `updated_at` or equivalent advanced); B in zone
- **AC-3 (AC-AUTH-05a/b)**: Tick budget under login load
  - Setup: 2+ clients connected; load driver holds ≥ 10 (`MAX_AUTH_PENDING`) concurrent logins for 60 s
  - Verify: zone tick timestamp log
  - Pass condition: max inter-tick gap ≤ 50 ms during the load window; load driver latencies logged
- **AC-4 (AC-AUTH-29 d)**: Gateway down
  - Verify: stop the gateway process with a client connected
  - Pass condition: client keeps moving for 30 s; zone tick log continuous
- **AC-5 (Verification 2)**: Editor Play-mode exit
  - Verify: exit Play mode, re-enter
  - Pass condition: gateway log shows the old link closed and a `RegisterZone` with the same ZoneID; no orphan link thread (thread list / log)
- **AC-6 (round trip)**: Transport drop and token reconnect
  - Setup: client in zone
  - Verify: stop the harness's transport (not a logout), let the client reconnect with its token
  - Pass condition: zone log shows `Disconnected_SessionActive`, then the reconnect rebinds the slot and the session is `Connected` with the same `CharacterID`

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `production/qa/evidence/auth-end-to-end-evidence.md` (manual runs + logs; also closes Story 018 AC-9/10)
- EditMode tests for the new code: the per-tick timestamp log, `ClientZoneSession`'s snapshot event, the harness's decode-and-log helper (paths fixed at `/dev-story`)

**Status**: [x] Created — `production/qa/evidence/auth-end-to-end-evidence.md` (signed off 2026-11-12); EditMode tests listed in Completion Notes

---

## Dependencies

- Depends on: None open. Story 018 (client zone entry) Complete 2026-11-11; Stories 012–016 Complete; the gateway epic Complete (all 12 stories — zone link both sides, account → zone map, router, `IGatewayRegistration`; Gateway Story 012 is the zone-side `SessionHandshake` decode → `ZoneEntryAdmission.BeginEntry`, the shared `SessionTokenStore` and the `Connected` transition — TD-095 resolved); Zone Instancing Stories 015 (entity state source, TD-100) and 016 (session end inputs, TD-101).
- Unlocks: Story 019 (uses this story's harness, load driver and evidence doc); Authentication epic Definition of Done together with Story 019.

---

## Completion Notes

**Completed**: 2026-11-12
**Criteria**: 12/12 passing (AC-AUTH-05a PASS with note — user decision 2026-11-12: max tick 9.5 ms; two Editor frame stalls of 110.7 / 104.5 ms accepted, rechecked on the VPS in Story 019)
**Deviations** (advisory, accepted):
- AC-AUTH-05a: judged on tick duration ≤ 50 ms (tick log warns on duration > 50 ms or gap ≥ 100 ms; nominal 20 Hz gap is 50 ms) instead of "no inter-tick gap > 50 ms"; Editor frame stalls are not tick work.
- AC-AUTH-29 (d): "a client still moving" → "client stays Entered and Connected for 30 s+" (the harness has no movement input).
- Harness moved to its own assembly `IronGrind.DevHarness.Session` (`UNITY_EDITOR || DEVELOPMENT_BUILD`, client references only); ADR-012 Decision 7 note.
- Round-trip transport drop done by suspending the client process (not a firewall rule).
- Decision: an unrotated token presented directly on a ghost handshake is accepted (CR-TOK-9 allows it; the token is not rotated again on reconnect).
**Scope additions**: defect 7 fixed in this story (user "fix it now"): zone token-path reconnect (CR-TOK-9) — `IZoneReconnect.cs`, `ZoneEntryAdmission.BeginReconnect`, `ZoneSlotPool.RebindClient` / `RevertToGhost` / `TryFindPlayerByCharacter`, `ZoneEntryReclamation.RebindClient` (rebound slot reverts to ghost on stall / drop), `ConnectionStateMachine.TryGetAccountByCharacter` / `RevertToGhost`, `GatewayZoneEntryTokenValidator` (`IZoneReconnectTokenValidator`), `ZoneInstancingWiring.ReconnectSessionAdapter`. Run fixes: `DevEvidenceLog`, `NgoSharedSettings` (one source for the NGO config fields both sides hash), harness `runInBackground` / frame cap (restored on disable), harness settings trim (`ZoneEntryHarnessSettings`).
**Test Evidence**: `production/qa/evidence/auth-end-to-end-evidence.md` (6 manual cases, signed off 2026-11-12); EditMode: `tests/EditMode/Networking/TickTimestampLog_tests.cs`, `ZoneServer_CompositionTickLog_tests.cs`, `DevEvidenceLog_tests.cs`, `NgoSharedSettings_tests.cs`, `tests/EditMode/Client/ClientZoneSession_tests.cs` (snapshot event), `tests/EditMode/DevHarness/SnapshotNameLog_tests.cs`, `ZoneEntryHarnessSettings_tests.cs`, `tests/EditMode/Gateway/ZoneTokenPathReconnect_tests.cs` (20), plus slot pool / session machine reconnect unit tests. Full EditMode 7055 / 6926 passed / 0 failed / 129 skipped.
**Code Review**: Complete — `/code-review` CHANGES REQUIRED → user "fix all" (1–11) applied 2026-11-12.
**Tech debt logged**: TD-105 (pool slot ghost state not set on heartbeat ghosting; removed-client record consumed by a failed reconnect), TD-106 (CR-NET-6.4 stand-ins on token-path reconnect).
**Story 018**: AC-9 and AC-10 closed by this story's AC-1 and AC-3 runs.
