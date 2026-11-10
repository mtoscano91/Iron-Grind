# Story 005: Account → zone map

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — account → zone map lifecycle (CR-AUTH-11), EC-AUTH session stealing, AC-AUTH-24
**Requirement**: No TR-ID — specified by ADR-016 Decision 3 "Account → zone map" (traced to the ADR directly; see the epic's GDD Requirements note)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3, account → zone map, amended 2026-11-06 review A3)
**ADR Decision Summary**: One entry per account with a session in some zone. Added on `AdmitResult(accepted)` for a pending `Admit`; `SessionReleased` / `EvictResult` from zone Z remove the entry only if it points to Z; on link loss the zone's entries stay `GATEWAY_LINK_RETRY_SECONDS × 2` (fresh logins for them get `ServerError` at once), then are removed; a `RegisterZone` replaces all entries pointing to that zone with its `liveAccounts`, and a listed account the map assigns elsewhere gets `Evict` to the registering zone; the map starts empty at gateway boot.

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API.

**Control Manifest Rules (this layer)**:
- Required: map rules exactly as listed in the manifest "Account → zone map" bullet — ADR-016 Decision 3
- Required: `GATEWAY_LINK_RETRY_SECONDS` from config — never hardcoded

---

## Acceptance Criteria

- [ ] `AccountZoneMap : IAccountZoneMap` (replaces `EmptyAccountZoneMap`) — thread-safe; extend the interface as needed (`Remove(account, fromZone)`, `OnRegistered(zone, liveAccounts)`, `OnLinkLost(zone)`), keeping the existing `TryGetZone` / `IsLinkUp` / `SetSession` callers in `SessionEstablishment` / `TokenPath` working
- [ ] Zone-matched removal: `SessionReleased(42)` or `EvictResult` for account 42 from zone 7 removes 42's entry only if it points to 7; from zone 8 it changes nothing
- [ ] Link-loss grace: after zone 7's link is lost, its entries remain and `IsLinkUp(7)` is false (the login flow then answers `ServerError` without `Evict` — existing behaviour); after `GATEWAY_LINK_RETRY_SECONDS × 2` with no re-registration, every entry pointing to 7 is removed; a re-registration within the grace cancels the removal
- [ ] Registration: `RegisterZone(7, liveAccounts=[42, 43])` replaces every entry pointing to 7 with 42 → 7, 43 → 7; if 43 is mapped to zone 8, 43 stays on 8 and an `Evict(43)` is sent to zone 7
- [ ] Boot: the map starts empty; `IsBootWaitOver` is false until `GATEWAY_LINK_RETRY_SECONDS × 2` after start (Story 007 / 009 use it to answer `ServerStarting`)
- [ ] AC-AUTH-24 (advisory): with 200 entries, `GC.GetTotalMemory` delta ≤ 12 800 bytes, asserted over 3 runs
- [ ] Tests use a fake clock and a fake evict sender

---

## Implementation Notes

*Derived from ADR-016 Decision 3:*

- Store `AccountID → ZoneID` in a `ConcurrentDictionary` or a lock-guarded `Dictionary`; zone-matched removal must be atomic (`TryRemove(KeyValuePair)` or compare under the lock).
- Grace timers: driven by the injected clock (a single periodic sweep is fine), not one `Timer` per zone in tests.
- The `Evict` for a conflicting registration goes through the Story 004 link (`IZoneLink.EvictAsync`, fire-and-forget with logging) — inject a narrow sender seam so this story's tests need no link.
- AC-AUTH-24 test: `[Category("Advisory")]` or similar if the project uses one; report the measured delta in the completion notes.
- *(Added 2026-11-09, Story 004 code review S4.)* `LinkZoneLink` now calls `SetSession` on `AdmitResult(accepted)` before the login continues (ADR-016 "Added"). `SessionEstablishment.Accept` still calls `SetSession` afterwards. If the zone's `SessionReleased` lands between the two calls, the second call brings back a removed entry. Decide at readiness whether this story (or Story 009) drops the `SetSession` in `SessionEstablishment.Accept`, which needs `Auth_SessionEstablishment_tests` reworked so the fake link performs the add.

---

## Out of Scope

- Story 004: when `SetSession` is called (live `AdmitResult(accepted)`), late-admission `Evict`
- Story 006: the routing table's own view of link state (zones), not accounts
- Story 009: wiring the map's events to the listener

---

## QA Test Cases

- **AC-2**: Zone-matched removal
  - Given: 42 → 7
  - When: `SessionReleased(42)` from 8 → Then 42 → 7 unchanged; from 7 → Then no entry
- **AC-3**: Grace
  - Given: 42 → 7, 43 → 7; fake clock
  - When: link 7 lost; clock + (2×retry − 1 ms) → entries present, `IsLinkUp(7)` false; + 1 ms → both removed
  - Edge cases: re-registration at grace − 1 ms with `[42]` → 42 → 7 kept, 43 removed, no sweep afterwards
- **AC-4**: Registration conflict
  - Given: 43 → 8
  - When: `RegisterZone(7, [42, 43])`
  - Then: 42 → 7; 43 → 8; one `Evict(43)` to zone 7
- **AC-5**: Boot wait
  - Then: `IsBootWaitOver` false at start, true at exactly 2 × retry
- **AC-6 (AC-AUTH-24)**: Memory
  - Given: `GC.Collect()`, measure; add 200 entries; `GC.Collect()`, measure
  - Then: delta ≤ 12 800 bytes in each of 3 runs

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_AccountZoneMap_tests.cs` — must pass

**Status**: [x] Created — 33 tests, all passing (dotnet, 2026-11-09)

---

## Dependencies

- Depends on: Story 004 (evict sender; can start against a fake)
- Unlocks: Story 009

---

## Completion Notes
**Completed**: 2026-11-09
**Criteria**: 7/7 passing (none deferred)
**Deviations**: Advisory only. Readiness decision (user, 2026-11-09): `SessionEstablishment.Accept` no longer calls `SetSession`; `LinkZoneLink` is the only writer on accept, pinned by `Auth_SessionEstablishment_tests::RunAsync_Accepted_DoesNotWriteTheMapItself`. `LinkZoneLink.HandleEvictResult` performs the zone-matched `Remove` (the `EvictResult` body carries only `RequestId`). Expiry is lazy (purged on every map call) plus `Sweep()` — Story 009 must schedule `Sweep()` to free memory. `IsBootWaitOver` is on the class, not the interface. The constructor takes `GatewayOptions` (Story 009 passes `options.Value`). `ZoneLinkEvictSender` takes `Func<IZoneLink>` to break the DI cycle (link → map → sender → link). AC-AUTH-24 takes the median of 3 attempts per run (single attempts were flaky, one hit 12 920 B); measured 8 672 B in each of 3 runs. Unused `using IronGrind.Currency;` in `ZoneLinkEvictSender.cs`.
**Test Evidence**: Logic: `gateway/IronGrind.Gateway.Tests/Gateway_AccountZoneMap_tests.cs` — 33 tests; CI-filter suite 498/498; map tests 3× repeat clean
**Code Review**: Complete — CHANGES REQUIRED (R1: no test pinned the dropped `SetSession`; S1–S4) → fixed → re-review APPROVED WITH SUGGESTIONS
