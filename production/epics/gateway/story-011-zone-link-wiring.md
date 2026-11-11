# Story 011: Zone link wiring

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — S10 registration, CR-ZI-3 (admitted entries reserve capacity), T-4 leaving Active; AC-ZI-32, AC-ZI-35 (the gateway-facing sends)
**Requirement**: `TR-zi-018`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3), with ADR-009 Amendment 1 (S10, teardown step 2a, `OnDestroy`)
**ADR Decision Summary**: S10 opens the link and sends `RegisterZone { zoneId, templateId, udpPort, capacity, liveAccounts[] }` (admitted-but-not-arrived accounts included). The tick drains the inbox at its start into the `Admit` / `Evict` / `Reconnect` handlers; replies and `SessionReleased` go out through the link. `PlayerCount` (occupied + admitted) is sent on change; `ZoneDraining` when the zone leaves Active. The link stops in teardown step 2a and from the host's `OnDestroy`.

**Engine**: Unity 6.3 LTS (server build) | **Risk**: MEDIUM — Editor Play-mode exit must stop the thread (ADR-016 Verification 2)
**Engine Notes**: Leaving Play mode does not run the teardown coroutine; `ZoneServerHost.OnDestroy` must stop the link (same as the relay listener, ADR-015).

**Control Manifest Rules (this layer)**:
- Required: S10 last, after S9 Active; teardown 2a stops the link beside `NpgsqlRelayListener.Stop()`; `OnDestroy` stops it — ADR-009 Amendment 1, ADR-016
- Required: "Zone leaving Active" bullet (send `ZoneDraining`, reject further `Admit`s, end each not-arrived reservation with `SessionReleased`, keep its token until `CONNECTING_TIMEOUT_SECONDS`)
- Forbidden: stand-ins in the running composition once real types exist (`IGatewayRegistration` stand-in, `LoggingGatewayLinkOutbox`)

---

## Acceptance Criteria

- [x] Real `IGatewayRegistration` at S10 starts `GatewayLinkClient` (Story 010) with a registration callback built from the zone's ID, template, UDP port, `MAX_PLAYERS_PER_ZONE` and its live accounts (occupied sessions + admitted entries); the `IGatewayRegistration` stand-in is removed from the composition
- [x] Real `IGatewayLinkOutbox` enqueues onto the link; `LoggingGatewayLinkOutbox` is removed from the composition (kept for tests only if useful)
- [x] At tick start the inbox is drained: `Admit` → `GatewayAdmitHandler.Handle`, `Evict` → `GatewayEvictHandler`, `Reconnect` → `GatewayReconnectHandler` (Authentication Stories 013/014), with the current server tick
- [x] `PlayerCount { occupied + admitted }` is sent whenever that number changes (at most once per tick), and once right after each (re-)registration
- [x] AC-ZI-32 (send side): 48 occupied, three `Admit`s → two accepted, one rejected (full); `PlayerCount` 50 sent; one admitted entry times out → `PlayerCount` 49 and a new `Admit` accepted; a handshake that turns an admitted entry into a slot leaves `PlayerCount` unchanged (no send)
- [x] AC-ZI-35 (send side): an Active zone with one admitted entry moves to Draining (T-4) → `ZoneDraining` and one `SessionReleased` for that account are sent and `PlayerCount` no longer counts the entry; further `Admit`s are rejected
- [x] Teardown step 2a calls the link's `Stop()` (after `NpgsqlRelayListener.Stop()`); `ZoneServerHost.OnDestroy` stops it too; teardown order tests updated
- [x] ADR-016 Verification 2 (manual, recorded in `production/qa/evidence/gateway-zone-link-editor-evidence.md`): Windows Editor zone in Play mode ↔ local gateway on loopback — the gateway logs `RegisterZone`; exiting Play mode stops the link thread (gateway logs link lost, no thread left running); entering Play mode again re-registers the same ZoneID

---

## Implementation Notes

*Derived from ADR-016 Decision 3 and ADR-009 Amendment 1:*

- Composition: `ZoneServerComposition` + `ZoneInstancingWiring` own the wiring; read both first. `AdmittedEntries` (Story 013) already tracks admitted entries and their timeouts — read the count from it.
- Find where the zone leaves Active (T-4 / T-6) in the zone state owner and add the `ZoneDraining` + `SessionReleased` sends there (or an observer the wiring subscribes); AC-ZI-35's client-facing half (late handshake → `ZoneDoesNotExist`) is already Zone Instancing / Authentication 013 scope — verify, don't duplicate.
- `PlayerCount` change detection: compare against the last sent value at the end of the tick.
- *(Added 2026-11-09, Story 006 code review S5.)* `RegisterZone` carries no lifecycle state, so the gateway's routing table marks every (re-)registered zone Active. A zone that is not Active when its link (re)connects must send `ZoneDraining` right after `RegisterZone`; otherwise the gateway routes logins to it until its `Admit` rejections force a re-route.
- Verification 2 needs the real gateway (Story 009) running locally; if Story 009 is not Done yet, run against the gateway with stand-ins replaced as far as they are, and say so in the evidence.

---

## Out of Scope

- Story 012: client entry over the wire (`SessionHandshake` decode)
- The Linux IL2CPP run (ADR-016 Verification 1)

---

## QA Test Cases

- **AC-1**: Given composition with a fake link client → S10 → `Start` called once; registration callback lists occupied + admitted accounts; Edge: S10 failure → S-fail path as for other steps
- **AC-3**: Given inbox holding `Admit`, `Evict`, `Reconnect` → one tick → each handler called once with that tick; replies enqueued
- **AC-5 (AC-ZI-32)**: as stated, advancing ticks programmatically; assert the `PlayerCount` sequence 50 → 49 and no send on handshake conversion
- **AC-6 (AC-ZI-35)**: as stated; assert the order `ZoneDraining`, `SessionReleased`, `PlayerCount`
- **AC-7**: teardown order test includes link `Stop()` at 2a after the relay listener; `OnDestroy` path stops the link once
- **AC-8**: manual — see evidence path

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Gateway/GatewayLink_ZoneWiring_tests.cs` — must pass
- `production/qa/evidence/gateway-zone-link-editor-evidence.md` (ADR-016 Verification 2)

**Status**: [x] Created — 26 tests in `GatewayLink_ZoneWiring_tests.cs` + 3 in `tests/EditMode/Networking/TickLoop_ZoneTickPipeline_tests.cs`, all passing (Unity EditMode, 2026-11-11); `gateway-zone-link-editor-evidence.md` PASS (2026-11-11)

---

## Dependencies

- Depends on: Story 010; Story 009 for the manual Verification 2 run
- Unlocks: Story 012, Authentication Story 017

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 8/8 passing (AC-8 manual: ADR-016 Verification 2 PASS in the Editor, two Play-mode cycles, step 2a 148 / 98 ms)
**Deviations**: (advisory) TR-zi-018 not in `tr-registry.yaml` (TD-098); `IGatewayLinkOutbox` gained `EnqueuePlayerCount` / `EnqueueZoneDraining` (two Authentication test fakes updated); `AdmittedEntries.CopyReservedAccounts` added (outside the brief's file list, read-only helper); new pipeline step 1b (`IZoneGatewayLinkStep`, 8-argument `ZoneTickPipeline` constructor; the older constructors forward a no-op); link frames queued just before a stop are best-effort (documented; the gateway treats the lost link as the zone gone); the AC-5 handshake-conversion test uses the direct slot path (TD-099, Story 012); the composition test can only assert template id 0 (its rig loads no definition). Observed during Verification 2: warm spare in `ZONE_LAUNCHER=none` burns a ZoneID and a `crit` line every 30 s (TD-096). Flaky pre-existing wall-clock test seen once in the full run (TD-097).
**For Story 012**: the handshake decode should feed `ZoneEntryAdmission.BeginEntry`; add the real-handshake "no PlayerCount on conversion" test (TD-099).
**Test Evidence**: Integration — `tests/EditMode/Gateway/GatewayLink_ZoneWiring_tests.cs` (26), `tests/EditMode/Networking/TickLoop_ZoneTickPipeline_tests.cs` (+3), step-count updates in the MovementSystem / StatusEffects PipelineWiring tests; `production/qa/evidence/gateway-zone-link-editor-evidence.md` PASS. Full EditMode 6808 / 6678 passed / 1 failed (TD-097, passed on rerun) / 129 skipped.
**Code Review**: Complete — /code-review 2026-11-11 (unity-specialist + qa-tester), APPROVED WITH SUGGESTIONS, all 9 applied
