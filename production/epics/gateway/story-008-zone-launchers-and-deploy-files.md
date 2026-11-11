# Story 008: Zone launchers and deploy files

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — T-1 (zone start), CR-ZI-1 (ZoneID never reused)
**Requirement**: `TR-zi-001` (the gateway passes `-zoneId`)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3, zone process launch — amended 2026-11-06, review A2), with ADR-007 (hosting)
**ADR Decision Summary**: Production (Ubuntu): write `/run/irongrind/zone-<zoneId>.env` (`ZONE_TEMPLATE`, `ZONE_PORT`), then `systemctl start irongrind-zone@<zoneId>.service`. The template unit reads `EnvironmentFile=/run/irongrind/zone-%i.env`, runs the zone with `-zoneId %i -zoneTemplate ${ZONE_TEMPLATE} -port ${ZONE_PORT} -serverConfig <path>`, `Restart=no`. A polkit rule lets the `gateway` user start only `irongrind-zone@*` units. Dev (Windows): `Process.Start` on a configured build path, or a no-op when zones are started by hand or from the Editor.

**Engine**: .NET 10 (gateway); systemd / polkit (Ubuntu) | **Risk**: MEDIUM — systemd/polkit behaviour is only verifiable on the VPS
**Engine Notes**: Check the polkit JavaScript rule syntax and `systemctl` exit codes against the Ubuntu version on the VPS on the day; the dev machine cannot run them.

**Control Manifest Rules (this layer)**:
- Required: zones started through `IZoneLauncher`, never as the gateway's child processes in production; `Restart=no` — ADR-016 Decision 3, ADR-007
- Required: polkit rule limited to `irongrind-zone@*` for the `gateway` user

---

## Acceptance Criteria

- [ ] `SystemdZoneLauncher`: writes `/run/irongrind/zone-<id>.env` (mode 0640, `ZONE_TEMPLATE=…`, `ZONE_PORT=…`, overwrite if present) then runs `systemctl start irongrind-zone@<id>.service`; a non-zero exit or a write failure throws with the exit code and stderr in the message
- [ ] `ProcessZoneLauncher` (dev): starts the configured zone executable with `-zoneId <id> -zoneTemplate <t> -port <p> -serverConfig <path>`; `NoOpZoneLauncher` logs the request and returns
- [ ] Launcher choice from config (`ZONE_LAUNCHER`: `systemd` | `process` | `none`), validated at startup
- [ ] Both real launchers run their OS calls through a small runner seam (`IProcessRunner`, `IFileWriter`), so tests assert the exact file content and command line without running `systemctl`
- [ ] `tools/deploy/systemd/irongrind-zone@.service` (template unit: `EnvironmentFile=/run/irongrind/zone-%i.env`, `ExecStart=… -zoneId %i …`, `Restart=no`, `User=` the zone user) and `tools/deploy/polkit/49-irongrind-gateway.rules` (allows `org.freedesktop.systemd1.manage-units` for user `gateway` only when the unit matches `irongrind-zone@*.service` and the verb is `start`) committed, with a short `tools/deploy/README.md`
- [ ] `/run/irongrind/` creation and ownership documented (a `tmpfiles.d` entry `tools/deploy/tmpfiles/irongrind.conf`)

---

## Implementation Notes

*Derived from ADR-016 Decision 3 and ADR-007:*

- The env file is written before `systemctl start`; on a failed start the file may stay (overwritten next time).
- Never pass a token or connection string in the env file or command line.
- Process launcher on Windows: `UseShellExecute = false`, no window; log the PID. Hand-started zones pass their own `-zoneId` (uniqueness not guaranteed in dev — ADR-016).
- The VPS verification (polkit rule actually allows the start, unit actually runs) is part of the VPS story (Authentication Story 017 / deploy work) — record it there.

---

## Out of Scope

- Story 007: when to start
- VPS install and verification (Authentication Story 017 / later deploy story)

---

## QA Test Cases

- **AC-1**: Systemd launcher
  - Given: fake file writer and process runner returning exit 0
  - When: `StartAsync(ZoneID 12, "StartingZone", 7801)`
  - Then: file `/run/irongrind/zone-12.env` with exactly `ZONE_TEMPLATE=StartingZone\nZONE_PORT=7801\n`; command `systemctl start irongrind-zone@12.service` *(code review 2026-11-11: now `systemctl --no-ask-password start …`, killed after 10 s)*
  - Edge cases: runner exit 1 with stderr "Access denied" → exception containing both; file write throws → no `systemctl` call
- **AC-2**: Process launcher → command line contains `-zoneId 12 -zoneTemplate StartingZone -port 7801 -serverConfig <cfg>`; no-op launcher → one log line, no runner call
- **AC-3**: Bad `ZONE_LAUNCHER` value → startup check fails
- **AC-5**: Deploy files — reviewed in code review against ADR-016 Decision 3 wording (`Restart=no`, `%i`, polkit unit match + verb)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_ZoneLauncher_tests.cs` — must pass
- `tools/deploy/` files committed

**Status**: [x] Created — 32 tests in `Gateway_ZoneLauncher_tests.cs` + 9 in `Gateway_LaunchOsSeams_tests.cs` (real process runner and file writer), all passing (dotnet, 2026-11-11; the Unix file-mode test is skipped on Windows and runs in Linux CI); `tools/deploy/` files written

---

## Dependencies

- Depends on: Story 007 (`IZoneLauncher` interface)
- Unlocks: Story 009

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 6/6 passing (AC-1–AC-4 automated; AC-5/AC-6 deploy files reviewed in code review against ADR-016 Decision 3)
**Deviations** (advisory):
- `systemctl --no-ask-password start …`, killed after 10 s (`SystemdZoneLauncher.SYSTEMCTL_TIMEOUT`) — code review; QA case annotated.
- Unit also reads `/etc/irongrind/zone-secrets.env` (`IRONGRIND_PG_PASSWORD`, root 0600) — not named in ADR-016; add an ADR-016 note with the VPS story.
- Install paths (`/opt/irongrind/zone/IronGrind.x86_64`, `/etc/irongrind/zone-server.json`) and user `irongrind-zone` are proposals, unverified (README "confirm on the VPS").
- Ubuntu 22.04 (ADR-007) ships polkit 0.105, which ignores JS `.rules` files — decide Ubuntu 24.04 vs a sudoers wrapper before the VPS story (README "Check the polkit version first").
- `ZONE_LAUNCHER` defaults to `none`; the VPS gateway environment must set `systemd`.
- TR-zi-001 has no `tr-registry.yaml` entry (known since Story 007).
- Story 007 follow-ups S1/S2 not picked up here — carry to Story 009.
**Test Evidence**: Integration — `gateway/IronGrind.Gateway.Tests/Gateway_ZoneLauncher_tests.cs` (32 tests) + `Gateway_LaunchOsSeams_tests.cs` (9 tests, real process runner and file writer); gateway suite 622 passed / 1 skipped (Unix file-mode test on Windows; runs in Linux CI), non-Database; `tools/deploy/` committed with the story
**Code Review**: Complete — CHANGES REQUIRED (zone inherited `IRONGRIND_GATEWAY_DB`) → fix all applied (2026-11-11)
