# Story 006: Routing table and fill-first router

> **Epic**: Gateway
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-6 (rules 1–5, fill-first invariant, `AdmitResult` rejection); AC-ZI-2, AC-ZI-3, AC-ZI-20, AC-ZI-34
**Requirement**: `TR-zi-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3)
**ADR Decision Summary**: The routing table lives in the gateway, built from `RegisterZone`, `PlayerCount` (occupied + admitted) and `ZoneDraining`; a zone with no live link is unroutable. Rule 1 (party co-location) is deferred at MVP (OQ-ZI-9) — routing starts at rule 2. With no routable zone the login gets `LoginResponse(ServerStarting)`; a rejected `Admit` is re-routed once, then `ServerError`.

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API.

**Control Manifest Rules (this layer)**:
- Required: fill-first routing, warm spare and `ServerStarting` per the manifest "Warm spare" bullet — ADR-016 Decision 3
- Required: `ZONE_OPEN_THRESHOLD` (35) and `MAX_PLAYERS_PER_ZONE` (50) from gateway config — never hardcoded (registry: `design/registry/entities.yaml`)

---

## Acceptance Criteria

- [ ] `RoutingTable` holds per zone: ZoneID, template, UDP port, capacity (from `RegisterZone`), `PlayerCount`, Active/Draining, link up/down; updated from the Story 003 events
- [ ] `FillFirstRouter : IZoneRouter` (replaces `NoZoneRouter`) applies, for the starting template at MVP: rule 2 (`lastZone` Active, link up, below hard cap → that zone), rule 3 (highest `PlayerCount` below `ZONE_OPEN_THRESHOLD`; ties → lower ZoneID, except Story 007's emptied-instance tie-break), rule 4 overflow (no instance below threshold → the lowest count below hard cap), rule 5 (none → no pick); `excluded` (the zone that just rejected `Admit`) is never picked
- [ ] AC-ZI-2: `lastZone` Active → that exact zone; port in the result is that zone's `udpPort`
- [ ] AC-ZI-3: `lastZone` unknown, closed or from a prior run → rules 3–4; never an error for a missing zone
- [ ] AC-ZI-20: A at 20, B at 30 (both < 35) → B; no zone start requested
- [ ] AC-ZI-34 (routing part): A at 40, B at 45, spare Loading → A; every Active instance at hard cap → no pick (the login flow answers `ServerStarting`, no `Admit`, no failure-count change — verify through `SessionEstablishment` with fakes)
- [ ] Draining zones, zones with link down, and zones that have not sent `RegisterZone` are never picked
- [ ] `TryGetPort` returns the registered `udpPort` for a known zone

---

## Implementation Notes

*Derived from zone-instancing.md CR-ZI-6 and ADR-016 Decision 3:*

- `ZONE_OPEN_THRESHOLD` and `MAX_PLAYERS_PER_ZONE` become gateway options (add to `GatewayOptions` + `appsettings.json`, validated: threshold ≤ cap − 2, registry safe ranges). The zone's own `capacity` from `RegisterZone` is the hard cap per zone; config cap is the default and a sanity bound.
- Template: MVP has one template (`StartingZone = 0`); keep the table keyed by template so Story 007 can find "instances of this template".
- Check whether `SessionEstablishment` already maps "no pick" to `ServerStarting` (Authentication Story 010 / `zone-instancing.md` rule 4) — reuse, don't duplicate.
- Rule 1 (party): leave a comment pointing to OQ-ZI-9; no code path.

---

## Out of Scope

- Story 007: starting instances (warm spare), `loadingInProgress`, emptied-instance tie-break
- Story 005: the account map

---

## QA Test Cases

- **AC-3 (AC-ZI-2)**: Given zone 7 Active at 10 → `TryPick(lastZone: 7)` → 7, port = registered port; Edge: zone 7 at hard cap → rule 3
- **AC-4 (AC-ZI-3)**: Given `lastZone: 99` (never registered) and zone 7 Active at 10 → 7; with no Active zone → no pick
- **AC-5 (AC-ZI-20)**: Given A(20), B(30) → B; A's count unchanged; no start request recorded
- **AC-6 (AC-ZI-34)**: Given A(40), B(45) → A; Given A(50), B(50) → no pick, and through `SessionEstablishment` with fakes: `LoginResponse(ServerStarting)`, zero `Admit`s, failure count unchanged
- **AC-7**: Given A Draining, B link down, C unregistered → no pick; `excluded: A` with A the only candidate → no pick

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_Routing_tests.cs` — must pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (events; can start against fakes)
- Unlocks: Story 007, Story 009
