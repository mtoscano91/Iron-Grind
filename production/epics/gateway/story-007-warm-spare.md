# Story 007: Warm spare and `IZoneLauncher`

> **Epic**: Gateway
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 5 h
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

- [ ] `IZoneLauncher.StartAsync(ZoneID, string zoneTemplate, ushort port, CancellationToken)` interface (ADR-016 Key Interfaces); `WarmSpareKeeper` allocates a ZoneID (Story 002 `IZoneIdAllocator`), picks a UDP port from a configured range, calls the launcher, and records the instance as Loading until its `RegisterZone`
- [ ] AC-ZI-33(a): gateway start with no zone registered after `GATEWAY_LINK_RETRY_SECONDS × 2` → exactly one `StartingZone` start
- [ ] AC-ZI-33(d): an instance at 20 registered before the boot wait ends → no start
- [ ] AC-ZI-19 / AC-ZI-33(b): one instance at 34, `PlayerCount` → 35 → exactly one start at once, with a non-zero ZoneID distinct from the existing one; no second start while it is Loading; after its `RegisterZone` a new character is routed to the spare; while it is Loading, routing goes to the 35-player instance (rule 4)
- [ ] AC-ZI-33(c): a start that fails (launcher throws, or no `RegisterZone` within `ZONE_LOAD_TIMEOUT_SECONDS`) is retried no sooner than `ZONE_LOAD_TIMEOUT_SECONDS` later, with a critical log (`ZoneDefinitionLoadFailed` alert) each time
- [ ] AC-ZI-33(e): one instance whose `PlayerCount` goes 1 → 0 → exactly one start (the emptied instance is not the spare)
- [ ] AC-ZI-33(f): emptied instance and new spare both Active at 0 → a new character is routed to the emptied instance (tie-break), then lowest ZoneID
- [ ] AC-ZI-1: no Active instance, spare Loading → `ServerStarting` without waiting, no `Admit`, no second start; after the spare's `RegisterZone` the retried login is routed to it
- [ ] `ZoneDraining` or link loss that leaves no instance below threshold → one start
- [ ] Tests use a fake launcher, fake allocator and fake clock

---

## Implementation Notes

*Derived from ADR-016 Decision 3 and zone-instancing.md CR-ZI-6:*

- `loadingInProgress` per template bounds starts to one at a time.
- Port allocation: a config range (e.g. `ZONE_UDP_PORT_FIRST` / `ZONE_UDP_PORT_COUNT`) — new gateway options, validated; a port is free when no registered or Loading instance holds it. If the design wants fixed ports, note it at readiness.
- Emptied-instance flag: set when a zone's `PlayerCount` goes from > 0 to 0; cleared when it rises again. Story 006's router reads it for the tie-break.
- Gateway boot wait comes from Story 005 (`IsBootWaitOver`) or a shared clock helper — one source of truth.

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
- **AC-8 (AC-ZI-1)**: Given only a Loading spare → `ServerStarting`, zero `Admit`s, one start total; spare registers → retried login routed to spare

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_WarmSpare_tests.cs` — must pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002, Story 006
- Unlocks: Story 008, Story 009
