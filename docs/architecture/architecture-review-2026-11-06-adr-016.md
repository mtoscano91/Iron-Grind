# Architecture Review — ADR-016 only

Date: 2026-11-06
Engine: Unity 6.3 LTS (6000.3)
Scope: ADR-016 Gateway and Authentication Process (Proposed, as of commit `20d40b5`), reviewed alone for acceptance. No traceability matrix rebuilt; `tr-registry.yaml` unchanged.
ADRs read: ADR-016 in full; the cited parts of ADR-006, ADR-007, ADR-009, ADR-012, ADR-015.
GDDs read (cited parts): `authentication.md` (CR-AUTH-4, CR-AUTH-11, account → zone map, gateway failure modes), `auth-wire-messages.md`, `zone-instancing.md` (CR-ZI-6 routing), `networking-session-token.md` (zone restart).
Code read: `SessionTokenStore.cs` (`IssueToken`, `TryValidateAndRotate`), `IZoneEntryTokenValidator.cs`, `IGatewayRegistration.cs`, `ZoneStartupArgs.cs`, `AccountID.cs` / `ZoneID.cs` / `CharacterID.cs`, `CharacterPersistenceConstants.cs`, `TickCompletionConstants.cs`, `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`, `tools/db/migrations/001_create_character_records.sql`, `ProjectSettings.asset`.

## Verdict: CONCERNS — stays Proposed

The decision is sound and all its dependencies are Accepted. Three items to fix before acceptance (A1–A3) and four small items (R1–R4).

## Claims confirmed

| Claim in ADR-016 | Checked against | Result |
|---|---|---|
| `IGatewayRegistration`, `IZoneEntryTokenValidator` (`bool IsValid(in ZoneEntryRequest)`) exist as seams | `src/ServerLogic/ZoneServer/`, `src/ServerLogic/ZoneInstancing/` | ✅ |
| `SessionTokenStore` with `TryValidateAndRotate` (FixedTimeEquals, CAS rotate) | `SessionTokenStore.cs` line 194 | ✅ |
| `AccountID`, `ZoneID` in `src/Foundation/CharacterPersistence/` (namespace `IronGrind.CharacterPersistence`); `CharacterID` in `src/Foundation/Currency/` (`IronGrind.Currency`); `System` only | the three files | ✅ |
| Migration 004 is the next free number | `tools/db/migrations/` (001–003) | ✅ |
| `character_records`: `account_id`, `character_name VARCHAR(26)`, `class_type`, `last_zone_id BIGINT`, other columns have Level-1 defaults | `001_create_character_records.sql` | ✅ |
| `PERSISTENCE_WRITE_TIMEOUT_SECONDS` = 5 | `CharacterPersistenceConstants.cs` line 10 | ✅ |
| Unity `apiCompatibilityLevel` 6 (.NET Standard 2.1) | `ProjectSettings.asset` line 923 | ✅ |
| ADR-009 S10 = gateway registration; teardown step 2a exists (relay listener) | ADR-009 lines 134, 422, 442 | ✅ |
| ADR-007 carries the pending-amendment note | ADR-007 line 6 | ✅ |
| Dependencies Accepted: ADR-004, 006, 007, 009, 011, 013 | Status lines | ✅ |
| GDDs aligned: CR-AUTH-4 gateway text, `LoginResponse`, `LOGIN_HTTP_TIMEOUT_SECONDS`, `MAX_AUTH_PENDING`, gateway routing in CR-ZI-6 | `authentication.md` 32/134/241, `auth-wire-messages.md` 22/73, `zone-instancing.md` 82 | ✅ |

## To fix before acceptance

**A1 — Gateway-link codec in `Foundation` breaks ADR-012's boundary.** Decision 6 puts "the auth body schemas and codecs and the gateway-link message codec" in `src/Foundation/Shared/`, which is in `IronGrind.Foundation` and ships in the client. ADR-012 Decision 6 admits a `Foundation` type only on the shared allow-list with a named client consumer. The link messages (`Admit`, `Evict`, `Reconnect`, `RegisterZone` … all carrying `AccountID`) are zone ↔ gateway only and have no client consumer. The existing allow-list entry for `AccountID` (`AssemblyBoundaryLists.cs` line 26) names "client login and session (wire id)" as its consumer, which CR-AUTH-2 now forbids.
Fix: two shared source folders. `src/Foundation/Shared/` — the client-facing auth bodies and codec, `CharacterID`, `ZoneID`. `src/ServerLogic/Gateway/Shared/` (in `IronGrind.ServerLogic`, out of client builds) — the link codec and `AccountID`, if no client code uses it (check by grep before moving). `gateway/IronGrind.Shared` compiles both folders; the netstandard2.1 / C# 9 / no-Unity rule applies to both. Update or remove the `AccountID` allow-list entry. Add ADR-012 to Depends On and a dated note on ADR-012.

**A2 — How the gateway starts zone processes is unspecified, and two guarantees depend on it.** Decision 3 has the gateway start zones (warm spare, `nextval('zone_id_seq')` "when it starts the process") and relies on running zones surviving a gateway restart and re-registering with `liveAccounts`. ADR-007 line 125 says "the gateway spawns and monitors child processes; systemd provides crash recovery (`Restart=always`). Unit name pattern: `irongrind-zone-{N}.service`." Read together:
- Zones started as children of the gateway's service are in its control group; by default systemd kills them when it stops or restarts the gateway — the opposite of "play in progress is unaffected".
- `Restart=always` restarts a crashed zone with the same arguments, so the same `-zoneId` — a reused ZoneID, against CR-ZI-1 as amended 2026-11-06.

Fix: state the launch mechanism in Decision 3. For example, the gateway runs `systemctl start irongrind-zone@<zoneId>.service` (a template unit; the instance name carries the ZoneID; its own unit, so outside the gateway's cgroup), with no automatic restart — a crashed zone's players return to login and get a new zone. Say what dev on Windows does (zones started by hand or by the Editor; the gateway's launcher is a no-op or `Process.Start`). Amend ADR-007 line 125 in the same change.

**A3 — The account → zone map has no rule for link loss, crashed zones or stale messages.**
- (a) **Crashed zone.** Its link drops and never returns (a restarted zone gets a new ZoneID). `authentication.md` says entries are removed "when the zone's link is replaced or lost with no re-registration listing the account", with no deadline. Until they are removed, each fresh login for those accounts sends `Evict` to a zone with no link and fails with `ServerError` — the player is locked out.
- (b) **Transient link loss.** If the entries are dropped at loss and the player logs in to zone B, zone A can re-register listing the same account. No rule says which session wins: two live sessions for one account.
- (c) **Stale messages.** `SessionReleased{accountId}` and `EvictResult` remove the account's entry without checking which zone it points to; a late one from zone A deletes an entry that now points to zone B, and the next login does not evict B. An `AdmitResult` arriving after `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` has no rule.

Fix (Decision 3, then `authentication.md`'s map lifecycle): a zone's message removes an entry only if the entry points to that zone; a zone whose link has been gone for a grace period (e.g. `GATEWAY_LINK_RETRY_SECONDS × 2`, or on process exit if A2's launcher sees it) loses its entries; an `Evict` to a zone with no link fails at once, or waits only for the grace period; a `RegisterZone` listing an account the map assigns to another zone gets an `Evict` for it to the registering zone; a late `AdmitResult(accepted)` is answered with `Evict`, or recorded in the map.

## Small items

- **R1 — `IssueToken` does not accept a token.** Decision 4 step 5: the gateway generates the token and sends it in `Admit`; "the zone calls `SessionTokenStore.IssueToken`". `IssueToken(uint accountId, uint expiresAtTick)` (`SessionTokenStore.cs` line 153) generates its own token and returns it. Name the new store method that stores a given token (for example `AdmitToken(accountId, token, expiresAtTick)`, rejecting a zero or wrong-length token), so the approved GDDs (gateway generates; CR-TOK-8 filled by `Admit`) stand.
- **R2 — Timeout margins.** `GATEWAY_EVICT_TIMEOUT_MS` 7000 is said to exceed "the save's own timeout so a save that finishes in time is never cut off". The steal save can take pool-open `Timeout=5` + `CommandTimeout=5` (ADR-007's connection string), and the tracked-task watchdog is `PERSISTENCE_WATCHDOG_TICKS` = 200 (10 s). The `LOGIN_HTTP_TIMEOUT_SECONDS` bound (~9.5 s) omits the one re-route (+ `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`). Either raise the values (e.g. Evict 11000 ms, login 20 s) or reword the claim and accept that a slow save loses the race.
- **R3 — Migration Plan misses stale artefacts.** `tr-registry.yaml` TR-auth-003 ("standalone .NET sidecar … named pipes") needs a revised text; `production/epics/authentication/EPIC.md` (Untraced-decision warning, TR-auth-003 row, Definition of Done line 40) and `production/epics/zone-instancing/EPIC.md` gap 1 ("Gateway process has no ADR") are stale; `docs/architecture/architecture.md` lines 89 and 141 still show "Sidecar IPC". Also add dated notes to ADR-009 (S10 now real; teardown step 2a also stops the gateway link), ADR-006 (accounts table, `gateway` role and its own pool) and ADR-012 (A1).
- **R4 — Depends On.** Add ADR-012 (A1) and ADR-015 (the link thread copies `NpgsqlRelayListener`'s shape and its Editor teardown finding).

## Seen, outside ADR-016

- The `TR-zi-*` IDs used by `production/epics/zone-instancing/EPIC.md` are not in `tr-registry.yaml`.
- ADR-016's 9 tuning knobs are not in `entities.yaml` or `docs/registry/architecture.yaml`. Consistent with earlier ADR knobs (e.g. `RELAY_LISTENER_STOP_TIMEOUT_MS`); noted only.

## Not done

- `unity-specialist` second opinion not spawned: ADR-016 was validated by it during authoring (6 findings, all applied), as with ADR-015. No finding here is engine-specific.
- Traceability matrix not rebuilt; no new TR-IDs.

## Next

Fresh authoring session: amend ADR-016 for A1–A3 and R1–R4, with `authentication.md`'s account → zone map text (A3) and ADR-007 line 125 (A2) in the same changeset. If the amendment stays within those items, the user can mark ADR-016 Accepted without another review, then follow its Migration Plan (ADR-007 amendment, TR-auth-003 revision, epic updates, `/create-stories authentication`, gateway epic).
