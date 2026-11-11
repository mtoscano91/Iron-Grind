# Story 007: Warm spare and `IZoneLauncher`

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-6 "Warm spare", T-1 / T-3, EC-ZI-8/9; AC-ZI-1, AC-ZI-19, AC-ZI-33
**Requirement**: `TR-zi-005`, `TR-zi-001` (allocation at start)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3, warm spare and zone process launch)
**ADR Decision Summary**: Per template the gateway keeps one instance, Active or Loading, below `ZONE_OPEN_THRESHOLD` — started at gateway boot (after `GATEWAY_LINK_RETRY_SECONDS × 2`) and whenever a `PlayerCount`, `ZoneDraining` or lost link leaves none. An instance whose `PlayerCount` fell to 0 after being > 0 is idle-closing and does not count; rule 3 ties go to such an instance, then the lowest ZoneID. Failed starts retried at most once per `ZONE_LOAD_TIMEOUT_SECONDS`. Zones are started through `IZoneLauncher`, never as child processes.

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API.

**Control Manifest Rules (this layer)**:
- Required: warm spare rules per the manifest "Warm spare" bullet — ADR-016 Decision 3
- Required: `ZONE_LOAD_TIMEOUT_SECONDS` (30) from config
- Forbidden: a login waiting for a zone process — answer `ServerStarting`

---

## Acceptance Criteria

- [x] `IZoneLauncher.StartAsync(ZoneID, string zoneTemplate, ushort port, CancellationToken)` interface (ADR-016 Key Interfaces); `WarmSpareKeeper` allocates a ZoneID (Story 002 `IZoneIdAllocator`), picks a UDP port from a configured range, calls the launcher, and records the instance as Loading until its `RegisterZone`
- [x] AC-ZI-33(a): gateway start with no zone registered after `GATEWAY_LINK_RETRY_SECONDS × 2` → exactly one `StartingZone` start
- [x] AC-ZI-33(d): an instance at 20 registered before the boot wait ends → no start
- [x] AC-ZI-19 / AC-ZI-33(b): one instance at 34, `PlayerCount` → 35 → exactly one start at once, with a non-zero ZoneID distinct from the existing one; no second start while it is Loading; after its `RegisterZone` a new character is routed to the spare; while it is Loading, routing goes to the 35-player instance (rule 4)
- [x] AC-ZI-33(c): a start that fails (launcher throws, or no `RegisterZone` within `ZONE_LOAD_TIMEOUT_SECONDS`) is retried no sooner than `ZONE_LOAD_TIMEOUT_SECONDS` later, with a critical log (`ZoneDefinitionLoadFailed` alert) each time
- [x] AC-ZI-33(e): one instance whose `PlayerCount` goes 1 → 0 → exactly one start (the emptied instance is not the spare)
- [x] AC-ZI-33(f): emptied instance and new spare both Active at 0 → a new character is routed to the emptied instance (tie-break), then lowest ZoneID
- [x] AC-ZI-1: no Active instance, spare Loading → `ServerStarting` without waiting, no `Admit`, no second start; after the spare's `RegisterZone` the retried login is routed to it
- [x] `ZoneDraining` or link loss that leaves no instance below threshold → one start
- [x] Port allocation: the keeper takes the lowest free port in [`ZONE_UDP_PORT_FIRST`, `ZONE_UDP_PORT_FIRST` + `ZONE_UDP_PORT_COUNT`); a port held by a Loading instance or by any `RoutingTable` row (link-down rows included, until pruned) is not free; no free port → no start, critical log `ZonePortsExhausted`, retried no sooner than `ZONE_LOAD_TIMEOUT_SECONDS` later
- [x] New options validated: `ZONE_UDP_PORT_FIRST` [1024, 65535], `ZONE_UDP_PORT_COUNT` [1, 256], range ends ≤ 65535 and contains neither `GATEWAY_HTTP_PORT` nor `GATEWAY_ZONE_LINK_PORT`; `ZONE_LOAD_TIMEOUT_SECONDS` [10, 120]
- [x] `RoutingTable.Sweep()` removes a row whose link has stayed down for `GATEWAY_LINK_RETRY_SECONDS × 2`; its port becomes free
- [x] Tests use a fake launcher, fake allocator and fake clock

---

## Implementation Notes

*Derived from ADR-016 Decision 3 and zone-instancing.md CR-ZI-6:*

- `loadingInProgress` per template bounds starts to one at a time.
- *(Readiness decision 2026-11-09.)* Port allocation: config range, no fixed ports. New options `ZONE_UDP_PORT_FIRST` = 7777 (the zone's existing port) and `ZONE_UDP_PORT_COUNT` = 16 in `GatewayOptions` + `GatewayOptionsValidator` + `appsettings.json`, validated as in the AC. Lowest free port wins. A port is held by a Loading instance and by every `RoutingTable` row, link-down rows included, until the row is pruned. No free port → no start, `LogCritical` `ZonePortsExhausted`, retried under the same once-per-`ZONE_LOAD_TIMEOUT_SECONDS` rule. Hand-started dev zones may register a port outside the range; such a port is never handed out by the keeper, so it needs no special case.
- *(Readiness decision 2026-11-09.)* New option `ZONE_LOAD_TIMEOUT_SECONDS` = 30, valid [10, 120] (zone-instancing.md Tuning Knobs).
- *(Readiness decision 2026-11-09.)* Template name: add `FillFirstRouter.STARTING_TEMPLATE_NAME = "StartingZone"` next to `STARTING_TEMPLATE` (0) and pass it to `IZoneLauncher.StartAsync`. One template at MVP — no config map yet.
- *(Readiness decision 2026-11-09.)* Event and timer delivery: `WarmSpareKeeper` implements `IZoneLinkEvents`; Story 009 must fan events to it **after** `RoutingTable`, so the keeper reads updated counts (note this ordering in the keeper's doc comment). A `Tick()` method handles the boot wait, load timeouts and retry spacing; Story 009 schedules it alongside `AccountZoneMap.Sweep()`. Each event handler re-evaluates the spare rule at once (no waiting for `Tick()`).
- Emptied instance: see the Story 006 note below — `HasHeldPlayers && PlayerCount == 0`; no new flag.
- *(Readiness decision 2026-11-09.)* Boot wait, one source of truth: the keeper takes the same `TimeProvider` and computes its boot end exactly as `AccountZoneMap` does (construction time + `GATEWAY_LINK_RETRY_SECONDS × 2`), via a shared helper (e.g. `GatewayBootWait`) that both classes use — refactor `AccountZoneMap.IsBootWaitOver` onto it rather than duplicating the formula.
- *(Readiness decision 2026-11-09.)* Late registration: a `RegisterZone` for a ZoneID the keeper already gave up on (timed out and retried with a new ZoneID) is an ordinary registration — it is routed and counted like any other zone. Rare (the zone itself closes at T-3); no special handling.
- *(Added 2026-11-09, Story 006 code review.)* Story 006's `RoutingTable` already records `ZoneRoute.HasHeldPlayers` (count has ever been > 0, kept across re-registration); "emptied" = `HasHeldPlayers && PlayerCount == 0`. Reuse it rather than adding a second flag. The rule 3 tie-break goes in `FillFirstRouter.TryPick` (comment marks the spot).
- *(Added 2026-11-09, Story 006 code review S6.)* `RoutingTable` never removes a row; a closed zone stays as link-down forever, and ZoneIDs are never reused, so rows grow by one per closed instance. Prune a row whose link has stayed down past `GATEWAY_LINK_RETRY_SECONDS × 2` (the map's grace) once this story no longer needs it for the "lost link leaves none" check. *(Readiness 2026-11-09: in scope — `RoutingTable.Sweep()`, scheduled by Story 009; pruning frees the row's port.)*

---

## Out of Scope

- Story 008: the real launchers (systemd, Process.Start)
- Story 009: hosting the keeper in the gateway process

---

## QA Test Cases

- **AC-2 (AC-ZI-33a)**: Given empty table, fake clock → at 2×retry − 1 ms zero starts; at 2×retry exactly one start for template 0
- **AC-3 (AC-ZI-33d)**: Given zone at 20 registers at 1 s → at 2×retry zero starts
- **AC-4 (AC-ZI-19, 33b)**: Given A at 34 → `PlayerCount(A)=35` → one start (ZoneID ≠ A, ≠ 0); `PlayerCount(A)=36` while Loading → no new start; route request while Loading → A; after spare `RegisterZone` → spare
- **AC-5 (AC-ZI-33c)**: launcher throws at t0 → critical log; retry not before t0 + 30 s; exactly one retry at t0 + 30 s
- **AC-6/7 (AC-ZI-33e/f)**: Given A at 1 → 0 → one start; spare registers at 0 → new character → A; then A at 1 → tie gone
- **Ports**: range 7777 × 2 with A on 7777 → spare gets 7778; both held → no start + `ZonePortsExhausted`, no retry before +30 s; A's link down past 2×retry → `RoutingTable.Sweep()` drops A, 7777 free again
- **Options**: count 0 / 257, first 1023, range past 65535, range containing `GATEWAY_HTTP_PORT` or `GATEWAY_ZONE_LINK_PORT`, load timeout 9 / 121 → invalid; 10 / 120 valid
- **AC-8 (AC-ZI-1)**: Given only a Loading spare → `ServerStarting`, zero `Admit`s, one start total; spare registers → retried login routed to spare

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_WarmSpare_tests.cs` — must pass

**Status**: [x] Created — 38 tests, passing

---

## Dependencies

- Depends on: Story 002, Story 006
- Unlocks: Story 008, Story 009

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 13/13 passing (all automated)
**Deviations** (advisory):
- Successful starts impose no retry spacing: the Loading zone's `RegisterZone` clears `_nextAttemptAt`, so a spare needed again at once starts at once (found by the code review's S2 test; matches zone-instancing.md:89, which spaces failed starts only).
- TR-zi-001 / TR-zi-005 have no `tr-registry.yaml` entries (known since readiness).
- Re-review follow-ups, not applied (pick up in Story 008 or 009): **S1** clear the spacing only when the registered zone counts as a spare (`HasSpareLocked()` true afterwards); otherwise keep it and log critical, so a misconfigured zone (capacity 0, template ≠ 0) cannot cause back-to-back starts — today these stop only when the 16 ports are taken. Add a test: zone registers with capacity 0 → no new start before +30 s. **S2** remove the unused `StartPlan.StartedAt`.
**Test Evidence**: Logic — `gateway/IronGrind.Gateway.Tests/Gateway_WarmSpare_tests.cs` (38 tests) + 6 options tests in `Gateway_Options_tests.cs`; gateway suite 582/582 (non-Database)
**Code Review**: Complete — CHANGES REQUIRED → fix all → re-review APPROVED WITH SUGGESTIONS (2026-11-11)
